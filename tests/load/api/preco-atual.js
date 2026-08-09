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
  const res = http.get(`${base}/v1/titulos/${data.codigo}/preco-atual`, { headers: authHeaders() });
  check(res, { "preco-atual: status 200": (r) => r.status === 200 });
}

export function smoke(data) {
  flow(data);
}

export function ramp(data) {
  flow(data);
}
