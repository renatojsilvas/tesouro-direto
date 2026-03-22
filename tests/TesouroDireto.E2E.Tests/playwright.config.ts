import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests",
  timeout: 30_000,
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
      testMatch: /(simulador|titulos|historico)\.spec\.ts/,
      use: {
        baseURL: process.env.WEB_URL ?? "http://localhost:5275",
      },
    },
  ],
});
