import http from "k6/http";
import { check } from "k6";
import { Rate } from "k6/metrics";
import { apiBase } from "../lib/config.js";
import { apiKey, authHeaders, pickCodigo } from "../lib/http.js";

const etag304 = new Rate("etag_304_rate");

const FLOW = __ENV.FLOW || "titulos";
const VUS = Number(__ENV.VUS) || 24;
const DURATION = __ENV.DURATION || "90s";

const base = apiBase();
apiKey();

export const options = {
  scenarios: {
    steady: {
      executor: "constant-vus",
      exec: "run",
      vus: VUS,
      duration: DURATION,
      tags: { scenario: "steady", flow: FLOW },
    },
  },
};

export function setup() {
  if (FLOW === "titulos") {
    return {};
  }
  return { codigo: pickCodigo(base) };
}

function flowTitulos() {
  const res = http.get(`${base}/v1/titulos`, { headers: authHeaders() });
  check(res, { "titulos: status 200": (r) => r.status === 200 });
  const etag = res.headers["Etag"];
  const res2 = http.get(`${base}/v1/titulos`, {
    headers: authHeaders({ "If-None-Match": etag }),
  });
  const is304 = res2.status === 304;
  check(res2, { "titulos: status 304 com If-None-Match": () => is304 });
  etag304.add(is304);
}

function flowHistorico(data) {
  const res = http.get(`${base}/v1/titulos/${data.codigo}/precos?page=1&pageSize=100`, {
    headers: authHeaders(),
  });
  check(res, { "historico: status 200": (r) => r.status === 200 });
}

function flowPrecoAtual(data) {
  const res = http.get(`${base}/v1/titulos/${data.codigo}/preco-atual`, {
    headers: authHeaders(),
  });
  check(res, { "preco-atual: status 200": (r) => r.status === 200 });
}

function flowSimulador(data) {
  const payload = JSON.stringify({
    codigo: data.codigo,
    valorInvestido: 1000.0,
    dataCompra: "2026-08-08",
    taxaContratada: 6.5,
    projecaoAnual: 4.5,
  });
  const res = http.post(`${base}/v1/simulador`, payload, {
    headers: authHeaders({ "Content-Type": "application/json" }),
  });
  check(res, { "simulador: status 200": (r) => r.status === 200 });
}

export function run(data) {
  if (FLOW === "historico") {
    flowHistorico(data);
  } else if (FLOW === "preco-atual") {
    flowPrecoAtual(data);
  } else if (FLOW === "simulador") {
    flowSimulador(data);
  } else {
    flowTitulos();
  }
}
