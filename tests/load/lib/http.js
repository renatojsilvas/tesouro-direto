import http from "k6/http";
import { check } from "k6";
import { requireEnv } from "./config.js";

export function apiKey() {
  return requireEnv("API_KEY");
}

export function authHeaders(extra) {
  return Object.assign({ "X-Api-Key": apiKey() }, extra || {});
}

export function pickCodigo(base) {
  const res = http.get(`${base}/v1/titulos?vencido=false`, { headers: authHeaders() });
  check(res, { "pickCodigo: status 200": (r) => r.status === 200 });
  const items = res.json();
  if (!Array.isArray(items) || items.length === 0) {
    throw new Error(
      "[load] GET /v1/titulos?vencido=false retornou lista vazia — não há título não vencido para reusar nos fluxos"
    );
  }
  const codigo = items[0].codigo;
  if (!codigo) {
    throw new Error("[load] item de /v1/titulos?vencido=false sem campo codigo");
  }
  return codigo;
}
