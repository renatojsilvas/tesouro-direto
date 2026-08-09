import http from "k6/http";
import { check } from "k6";
import { Rate } from "k6/metrics";
import { apiBase } from "../lib/config.js";
import { requireEnv } from "../lib/config.js";

const got429 = new Rate("got_429");

const base = apiBase();
const clientApiKey = requireEnv("CLIENT_API_KEY");

export const options = {
  scenarios: {
    validacaoRateLimitClientKey: {
      executor: "constant-arrival-rate",
      exec: "validarRateLimit",
      rate: 2,
      timeUnit: "1s",
      duration: "35s",
      preAllocatedVUs: 10,
      maxVUs: 30,
      tags: { scenario: "rate-limit-validation" },
    },
  },
  thresholds: {
    got_429: ["rate>0"],
  },
};

function clientAuthHeaders() {
  return { "X-Api-Key": clientApiKey };
}

export function validarRateLimit() {
  const res = http.get(`${base}/v1/titulos`, { headers: clientAuthHeaders() });
  const is429 = res.status === 429;
  if (is429) {
    check(res, { "rate-limit: Retry-After presente no 429": (r) => r.headers["Retry-After"] !== undefined });
  }
  got429.add(is429);
}
