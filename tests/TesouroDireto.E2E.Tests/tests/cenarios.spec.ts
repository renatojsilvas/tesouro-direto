import { test, expect } from "@playwright/test";

test.describe("Simulador Cenarios Page", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/simulador/cenarios");
  });

  test("should display cenarios form", async ({ page }) => {
    await expect(page.locator("h1")).toContainText("Cenários");
    await expect(page.locator("select#titulo")).toBeVisible();
    await expect(page.locator("button#adicionar-cenario")).toBeVisible();
    await expect(page.locator("button#simular")).toBeVisible();
  });

  test("should add and remove cenarios", async ({ page }) => {
    await page.click("button#adicionar-cenario");
    await page.click("button#adicionar-cenario");
    const cenarios = page.locator("[data-testid='cenario']");
    await expect(cenarios).toHaveCount(3); // 1 default + 2 added
  });
});
