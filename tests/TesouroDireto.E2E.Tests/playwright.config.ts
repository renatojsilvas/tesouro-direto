import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests",
  timeout: 60_000,
  expect: {
    timeout: 15_000,
  },
  use: {
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },
  retries: 0,
  projects: [
    {
      name: "api",
      testMatch: /health\.spec\.ts/,
      use: {
        baseURL: process.env.API_URL ?? "http://localhost:5000",
      },
    },
    {
      name: "web",
      testMatch: /(simulador|titulos|historico|tributos|cenarios)\.spec\.ts/,
      retries: 2,
      use: {
        baseURL: process.env.WEB_URL ?? "http://localhost:5275",
      },
    },
  ],
  workers: 1,
});
