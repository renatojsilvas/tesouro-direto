import http from "k6/http";
import { check } from "k6";
import { apiBase, THRESHOLDS } from "../lib/config.js";
import { apiKey, authHeaders, pickCodigo } from "../lib/http.js";

const base = apiBase();
apiKey();

export const options = {
  scenarios: {
    smoke: {
      executor: "constant-vus",
      exec: "smoke",
      vus: 2,
      duration: "30s",
      tags: { scenario: "smoke" },
    },
    ramp: {
      executor: "ramping-vus",
      exec: "ramp",
      startVUs: 0,
      stages: [
        { duration: "30s", target: 10 },
        { duration: "30s", target: 25 },
        { duration: "30s", target: 50 },
        { duration: "30s", target: 100 },
        { duration: "1m", target: 200 },
        { duration: "30s", target: 0 },
      ],
      startTime: "35s",
      tags: { scenario: "ramp" },
    },
  },
  thresholds: THRESHOLDS,
};

export function setup() {
  return { codigo: pickCodigo(base) };
}

function flow(data) {
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

export function smoke(data) {
  flow(data);
}

export function ramp(data) {
  flow(data);
}
