function utf8Bytes(text) {
  const bytes = [];
  for (let i = 0; i < text.length; i++) {
    let codePoint = text.charCodeAt(i);
    if (codePoint >= 0xd800 && codePoint <= 0xdbff && i + 1 < text.length) {
      const low = text.charCodeAt(i + 1);
      if (low >= 0xdc00 && low <= 0xdfff) {
        codePoint = 0x10000 + ((codePoint - 0xd800) << 10) + (low - 0xdc00);
        i++;
      }
    }
    if (codePoint < 0x80) {
      bytes.push(codePoint);
    } else if (codePoint < 0x800) {
      bytes.push(0xc0 | (codePoint >> 6), 0x80 | (codePoint & 0x3f));
    } else if (codePoint < 0x10000) {
      bytes.push(
        0xe0 | (codePoint >> 12),
        0x80 | ((codePoint >> 6) & 0x3f),
        0x80 | (codePoint & 0x3f)
      );
    } else {
      bytes.push(
        0xf0 | (codePoint >> 18),
        0x80 | ((codePoint >> 12) & 0x3f),
        0x80 | ((codePoint >> 6) & 0x3f),
        0x80 | (codePoint & 0x3f)
      );
    }
  }
  return bytes;
}

function pushSizedHeader(out, length, fixPrefix, fixMax, header8, header16, header32) {
  if (length <= fixMax) {
    out.push(fixPrefix | length);
    return;
  }
  if (header8 !== null && length < 0x100) {
    out.push(header8, length);
    return;
  }
  if (length < 0x10000) {
    out.push(header16, (length >> 8) & 0xff, length & 0xff);
    return;
  }
  out.push(
    header32,
    (length >>> 24) & 0xff,
    (length >>> 16) & 0xff,
    (length >>> 8) & 0xff,
    length & 0xff
  );
}

function encodeString(text, out) {
  const bytes = utf8Bytes(text);
  pushSizedHeader(out, bytes.length, 0xa0, 31, 0xd9, 0xda, 0xdb);
  for (const byte of bytes) out.push(byte);
}

function encodeBinary(value, out) {
  const bytes = value instanceof ArrayBuffer ? new Uint8Array(value) : value;
  const length = bytes.length;
  if (length < 0x100) {
    out.push(0xc4, length);
  } else if (length < 0x10000) {
    out.push(0xc5, (length >> 8) & 0xff, length & 0xff);
  } else {
    out.push(
      0xc6,
      (length >>> 24) & 0xff,
      (length >>> 16) & 0xff,
      (length >>> 8) & 0xff,
      length & 0xff
    );
  }
  for (let i = 0; i < length; i++) out.push(bytes[i]);
}

function encodeNumber(value, out) {
  if (!Number.isFinite(value)) {
    throw new Error(`[msgpack] número não finito não suportado: ${value}`);
  }
  if (!Number.isInteger(value)) {
    const buffer = new ArrayBuffer(8);
    new DataView(buffer).setFloat64(0, value, false);
    out.push(0xcb);
    for (const byte of new Uint8Array(buffer)) out.push(byte);
    return;
  }
  if (value >= 0 && value <= 127) {
    out.push(value);
    return;
  }
  if (value < 0 && value >= -32) {
    out.push(value & 0xff);
    return;
  }
  if (value >= 0) {
    if (value < 0x100) {
      out.push(0xcc, value);
    } else if (value < 0x10000) {
      out.push(0xcd, (value >> 8) & 0xff, value & 0xff);
    } else {
      out.push(
        0xce,
        (value >>> 24) & 0xff,
        (value >>> 16) & 0xff,
        (value >>> 8) & 0xff,
        value & 0xff
      );
    }
    return;
  }
  if (value >= -0x80) {
    out.push(0xd0, value & 0xff);
  } else if (value >= -0x8000) {
    out.push(0xd1, (value >> 8) & 0xff, value & 0xff);
  } else {
    out.push(
      0xd2,
      (value >>> 24) & 0xff,
      (value >>> 16) & 0xff,
      (value >>> 8) & 0xff,
      value & 0xff
    );
  }
}

function encodeValue(value, out) {
  if (value === null || value === undefined) {
    out.push(0xc0);
    return;
  }
  if (typeof value === "boolean") {
    out.push(value ? 0xc3 : 0xc2);
    return;
  }
  if (typeof value === "number") {
    encodeNumber(value, out);
    return;
  }
  if (typeof value === "string") {
    encodeString(value, out);
    return;
  }
  if (value instanceof Uint8Array || value instanceof ArrayBuffer) {
    encodeBinary(value, out);
    return;
  }
  if (Array.isArray(value)) {
    pushSizedHeader(out, value.length, 0x90, 15, null, 0xdc, 0xdd);
    for (const item of value) encodeValue(item, out);
    return;
  }
  if (value instanceof Map) {
    const entries = Array.from(value.entries());
    pushSizedHeader(out, entries.length, 0x80, 15, null, 0xde, 0xdf);
    for (const [key, mapValue] of entries) {
      encodeValue(key, out);
      encodeValue(mapValue, out);
    }
    return;
  }
  if (typeof value === "object") {
    const keys = Object.keys(value);
    pushSizedHeader(out, keys.length, 0x80, 15, null, 0xde, 0xdf);
    for (const key of keys) {
      encodeString(key, out);
      encodeValue(value[key], out);
    }
    return;
  }
  throw new Error(`[msgpack] tipo não suportado: ${typeof value}`);
}

export function encode(value) {
  const out = [];
  encodeValue(value, out);
  return new Uint8Array(out).buffer;
}

function encodeVarIntLength(length) {
  const out = [];
  let remaining = length;
  do {
    let byte = remaining & 0x7f;
    remaining >>>= 7;
    if (remaining > 0) byte |= 0x80;
    out.push(byte);
  } while (remaining > 0);
  return out;
}

export function frame(payload) {
  const payloadBytes = payload instanceof ArrayBuffer ? new Uint8Array(payload) : payload;
  const lengthPrefix = encodeVarIntLength(payloadBytes.length);
  const framed = new Uint8Array(lengthPrefix.length + payloadBytes.length);
  framed.set(lengthPrefix, 0);
  framed.set(payloadBytes, lengthPrefix.length);
  return framed.buffer;
}

const SIGNALR_MESSAGE_TYPE_INVOCATION = 1;
const SIGNALR_MESSAGE_TYPE_PING = 6;

export function encodeInvocation(target, args, options) {
  const opts = options || {};
  const headers = opts.headers || {};
  const invocationId = opts.invocationId !== undefined ? opts.invocationId : null;
  const streamIds = opts.streamIds || [];
  const invocationMessage = [
    SIGNALR_MESSAGE_TYPE_INVOCATION,
    headers,
    invocationId,
    target,
    args,
    streamIds,
  ];
  return frame(new Uint8Array(encode(invocationMessage)));
}

export function encodePing() {
  return frame(new Uint8Array(encode([SIGNALR_MESSAGE_TYPE_PING])));
}
