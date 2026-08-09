import { WebSocket } from "k6/websockets";
import { setInterval, clearInterval, setTimeout, clearTimeout } from "k6/timers";
import { Gauge, Counter, Rate } from "k6/metrics";
import { webBase } from "../lib/config.js";
import { fetchPage, extractCircuitStartArgs, negotiate, wsUrlFromWebUrl } from "../lib/blazor.js";
import { encodeInvocation, encodePing } from "../lib/msgpack.js";

const circuitsLive = new Gauge("blazor_circuits_live");
const circuitOpened = new Counter("blazor_circuit_opened");
const circuitClosed = new Counter("blazor_circuit_closed");
const circuitFailed = new Rate("blazor_circuit_failed");

const HOLD_DURATION_MS = 75000;
const PING_INTERVAL_MS = 15000;
const HANDSHAKE_TEXT = `${JSON.stringify({ protocol: "blazorpack", version: 1 })}\x1e`;

const base = webBase();
const interactivePath = __ENV.WEB_PATH || "/titulos";

export const options = {
  scenarios: {
    hold_circuits: {
      executor: "ramping-vus",
      exec: "holdCircuit",
      startVUs: 0,
      stages: [
        { duration: "1m", target: 25 },
        { duration: "1m", target: 50 },
        { duration: "1m", target: 100 },
        { duration: "2m", target: 200 },
        { duration: "1m", target: 400 },
        { duration: "1m", target: 0 },
      ],
      gracefulRampDown: "30s",
      tags: { scenario: "hold_circuits", medida: "circuitos_simultaneos_nao_rps" },
    },
  },
  thresholds: {
    blazor_circuit_failed: ["rate<0.05"],
  },
};

function stripRecordSeparator(text) {
  return text.charAt(text.length - 1) === "\x1e" ? text.slice(0, -1) : text;
}

function handshakeText(data) {
  if (typeof data === "string") return stripRecordSeparator(data);
  const bytes = new Uint8Array(data);
  let end = bytes.indexOf(0x1e);
  if (end < 0) end = bytes.length;
  let text = "";
  for (let i = 0; i < end; i++) text += String.fromCharCode(bytes[i]);
  return text;
}

export function holdCircuit() {
  const wsBase = wsUrlFromWebUrl(base);
  const html = fetchPage(base, interactivePath);
  const { descriptorsJson, applicationState } = extractCircuitStartArgs(html);
  const connectionToken = negotiate(base);
  const wsUrl = `${wsBase}/_blazor?id=${connectionToken}`;

  return new Promise((resolve) => {
    const ws = new WebSocket(wsUrl);
    ws.binaryType = "arraybuffer";
    let pingTimer = null;
    let holdTimer = null;
    let settled = false;
    let circuitEstablished = false;

    const finish = (asFailure) => {
      if (settled) return;
      settled = true;
      if (pingTimer !== null) clearInterval(pingTimer);
      if (holdTimer !== null) clearTimeout(holdTimer);
      circuitFailed.add(asFailure ? 1 : 0);
      if (circuitEstablished) {
        circuitClosed.add(1);
        circuitsLive.add(0);
      }
      try {
        ws.close();
      } catch (alreadyClosedError) {
        void alreadyClosedError;
      }
      resolve();
    };

    ws.onopen = () => {
      ws.send(HANDSHAKE_TEXT);
    };

    ws.onmessage = (event) => {
      if (circuitEstablished) return;

      const text = handshakeText(event.data);
      if (text.charAt(0) !== "{") return;

      let handshakeAck;
      try {
        handshakeAck = JSON.parse(text);
      } catch (e) {
        finish(true);
        return;
      }
      if (handshakeAck && handshakeAck.error) {
        finish(true);
        return;
      }

      circuitEstablished = true;
      circuitOpened.add(1);
      circuitsLive.add(1);

      ws.send(encodeInvocation("StartCircuit", [descriptorsJson, applicationState]));

      pingTimer = setInterval(() => {
        try {
          ws.send(encodePing());
        } catch (e) {
          finish(true);
        }
      }, PING_INTERVAL_MS);

      holdTimer = setTimeout(() => finish(false), HOLD_DURATION_MS);
    };

    ws.onerror = () => finish(true);
    ws.onclose = () => finish(!circuitEstablished);
  });
}
