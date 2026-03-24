import { test, expect } from "@playwright/test";
import { navigateTo } from "./helpers";

test.describe("Simulador Cenarios Page", () => {
  test.beforeEach(async ({ page }) => {
    await navigateTo(page, "/simulador/cenarios");
  });

  test("should display form with default cenarios", async ({ page }) => {
    await expect(page.locator("h1")).toContainText("Cenario");
    await expect(page.locator("select#titulo")).toBeVisible();
    await expect(page.locator("button#simular")).toBeVisible();

    const cenarios = page.locator("[data-testid='cenario']");
    await expect(cenarios).toHaveCount(3);
  });

  test("should add and remove cenarios", async ({ page }) => {
    const cenarios = page.locator("[data-testid='cenario']");
    await expect(cenarios).toHaveCount(3);

    await page.locator("button#adicionar-cenario").click();
    await expect(cenarios).toHaveCount(4);

    await page
      .locator("[data-testid='cenario'] .btn-outline-danger")
      .first()
      .click();
    await expect(cenarios).toHaveCount(3);
  });

  test("should show error for empty submission", async ({ page }) => {
    await page.click("button#simular");
    await expect(page.locator(".alert-danger")).toBeVisible();
  });

  test("should simulate and show comparison table", async ({ page }) => {
    // Wait for dropdown to have options
    await expect(page.locator("select#titulo option")).not.toHaveCount(1);
    await page.locator("select#titulo").selectOption({ index: 1 });

    // Retry select if Blazor missed it
    const value = await page.locator("select#titulo").inputValue();
    if (!value || value === "") {
      await page.waitForTimeout(1000);
      await page.locator("select#titulo").selectOption({ index: 1 });
    }

    await page.fill("input#valorInvestido", "10000");
    await page.fill("input#dataCompra", "2024-01-02");
    await page.fill("input#taxaContratada", "12");
    await page.click("button#simular");

    // Wait for result or error
    const resultados = page.locator("[data-testid='resultados']");
    const erro = page.locator(".alert-danger");
    await expect(resultados.or(erro)).toBeVisible();

    if (await erro.isVisible()) {
      const msg = await erro.textContent();
      throw new Error(`Simulation error: ${msg}`);
    }

    // Table should have scenario columns and R$ values
    await expect(resultados.locator("table")).toBeVisible();
    await expect(resultados.locator("table tbody tr")).not.toHaveCount(0);
  });
});
