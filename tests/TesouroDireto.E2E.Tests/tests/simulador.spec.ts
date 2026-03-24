import { test, expect } from "@playwright/test";
import { navigateTo, selectFirstTitulo } from "./helpers";

test.describe("Simulador Page", () => {
  test.beforeEach(async ({ page }) => {
    await navigateTo(page, "/simulador");
  });

  test("should display form", async ({ page }) => {
    await expect(page.locator("h1")).toContainText("Simulador");
    await expect(page.locator("select#titulo")).toBeVisible();
    await expect(page.locator("input#valorInvestido")).toBeVisible();
    await expect(page.locator("button#simular")).toBeVisible();
  });

  test("should load titulos in dropdown", async ({ page }) => {
    await expect(page.locator("select#titulo option")).not.toHaveCount(1);
  });

  test("should show error for empty submission", async ({ page }) => {
    await page.click("button#simular");
    await expect(page.locator("[data-testid='erro']")).toBeVisible();
  });

  test("should simulate and show results with values", async ({ page }) => {
    await selectFirstTitulo(page);
    await page.fill("input#valorInvestido", "10000");
    await page.fill("input#dataCompra", "2024-01-02");
    await page.fill("input#taxaContratada", "12");
    await page.click("button#simular");

    // Wait for either result or error
    const resultado = page.locator("[data-testid='resultado']");
    const erro = page.locator("[data-testid='erro']");
    await expect(resultado.or(erro)).toBeVisible();

    // If error, fail with message
    if (await erro.isVisible()) {
      const msg = await erro.textContent();
      throw new Error(`Simulation error: ${msg}`);
    }

    // Verify result has values (not empty/NaN)
    await expect(page.locator("[data-testid='valor-bruto']")).toContainText(
      "R$"
    );
    await expect(page.locator("[data-testid='valor-liquido']")).toContainText(
      "R$"
    );
  });
});
