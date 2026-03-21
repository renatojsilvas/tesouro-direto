import { test, expect } from "@playwright/test";

test("GET /health should return 200 healthy", async ({ request }) => {
  const response = await request.get("/health");

  expect(response.status()).toBe(200);
  expect(await response.text()).toContain("healthy");
});

test("GET /metrics should return 200 with Prometheus format", async ({
  request,
}) => {
  const response = await request.get("/metrics");

  expect(response.status()).toBe(200);
  const body = await response.text();
  expect(body).toContain("process_cpu_seconds_total");
});
