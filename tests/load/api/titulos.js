import http from "k6/http";
import { check } from "k6";
import { Rate } from "k6/metrics";
import { apiBase, THRESHOLDS } from "../lib/config.js";
import { apiKey, authHeaders } from "../lib/http.js";

const etag304 = new Rate("etag_304_rate");

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
  thresholds: Object.assign({}, THRESHOLDS, { etag_304_rate: ["rate>0.9"] }),
};

function flow() {
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

export function smoke() {
  flow();
}

export function ramp() {
  flow();
}
