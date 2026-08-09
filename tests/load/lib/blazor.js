import http from "k6/http";

const COMPONENT_MARKER_RE = /<!--Blazor:(\{[\s\S]*?\})-->/g;
const APP_STATE_PAIRED_RE = /<!--Blazor-Server-Component-State-->([\s\S]*?)<!--\/Blazor-Server-Component-State-->/;
const APP_STATE_INLINE_RE = /<!--Blazor-Server-Component-State:([\s\S]*?)-->/;
const APP_STATE_GENERIC_BASE64_RE = /<!--([A-Za-z0-9+/=]{20,})-->/;

export function wsUrlFromWebUrl(webBase) {
  if (webBase.startsWith("https://")) {
    return `wss://${webBase.slice("https://".length)}`;
  }
  if (webBase.startsWith("http://")) {
    return `ws://${webBase.slice("http://".length)}`;
  }
  throw new Error(`[blazor] WEB_URL precisa iniciar com http:// ou https://: ${webBase}`);
}

export function fetchHome(webBase) {
  const res = http.get(`${webBase}/`);
  if (res.status !== 200) {
    throw new Error(`[blazor] GET / falhou com status ${res.status}`);
  }
  return res.body;
}

export function extractCircuitStartArgs(html) {
  const componentMarkers = [];
  let match;
  COMPONENT_MARKER_RE.lastIndex = 0;
  while ((match = COMPONENT_MARKER_RE.exec(html)) !== null) {
    let markerJson;
    try {
      markerJson = JSON.parse(match[1]);
    } catch (e) {
      throw new Error(`[blazor] marcador <!--Blazor:{...}--> com JSON inválido: ${match[1]}`);
    }
    componentMarkers.push(markerJson);
  }
  if (componentMarkers.length === 0) {
    throw new Error(
      "[blazor] nenhum marcador <!--Blazor:{...}--> encontrado em / — a home não parece renderizar um root component com render mode Server"
    );
  }

  let applicationState = "";
  const paired = APP_STATE_PAIRED_RE.exec(html);
  const inline = APP_STATE_INLINE_RE.exec(html);
  const generic = APP_STATE_GENERIC_BASE64_RE.exec(html);
  if (paired) {
    applicationState = paired[1].trim();
  } else if (inline) {
    applicationState = inline[1].trim();
  } else if (generic) {
    applicationState = generic[1].trim();
  }

  return {
    descriptorsJson: JSON.stringify(componentMarkers),
    applicationState,
  };
}

export function negotiate(webBase) {
  const res = http.post(`${webBase}/_blazor/negotiate?negotiateVersion=1`, null, {
    headers: { "Content-Type": "text/plain;charset=UTF-8" },
  });
  if (res.status !== 200) {
    throw new Error(`[blazor] negotiate falhou com status ${res.status}`);
  }
  let body;
  try {
    body = res.json();
  } catch (e) {
    throw new Error("[blazor] negotiate não retornou JSON válido");
  }
  if (!body || !body.connectionToken) {
    throw new Error("[blazor] resposta de negotiate sem connectionToken");
  }
  return body.connectionToken;
}
