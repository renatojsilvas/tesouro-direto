import { test, expect } from "@playwright/test";
import { navigateTo } from "./helpers";

test.describe("Tributos Page", () => {
  test.beforeEach(async ({ page }) => {
    await navigateTo(page, "/tributos");
  });

  test("should display tributos table with data", async ({ page }) => {
    await expect(page.locator("h1")).toContainText("Tributos");
    await expect(
      page.locator("[data-testid='tabela-tributos']")
    ).toBeVisible();
    await expect(
      page.locator("[data-testid='tabela-tributos'] tbody tr")
    ).not.toHaveCount(0);
  });

  test("should open and close create form", async ({ page }) => {
    await page.click("button#novo-tributo");
    await expect(page.locator("[data-testid='form-tributo']")).toBeVisible();
    await expect(page.locator("input#nome")).toBeVisible();

    await page.click("[data-testid='btn-cancelar']");
    await expect(page.locator("[data-testid='form-tributo']")).toBeHidden();
  });

  test("should open edit form", async ({ page }) => {
    await expect(
      page.locator("[data-testid='tabela-tributos']")
    ).toBeVisible();

    await page.locator("[data-testid='btn-editar']").first().click();
    await expect(page.locator("[data-testid='form-tributo']")).toBeVisible();
    await expect(
      page.locator("[data-testid='form-tributo'] .card-title")
    ).toHaveText("Editar Tributo");
  });

  test("should create a new tributo", async ({ page }) => {
    const uniqueName = `E2E Tax ${Date.now()}`;

    await page.click("button#novo-tributo");
    await page.locator("input#nome").fill(uniqueName);
    await page.locator("select#baseCalculo").selectOption("Rendimento");
    await page.locator("select#tipoCalculo").selectOption("AliquotaFixa");
    await page.locator("input#ordem").fill("99");

    const faixaInputs = page.locator(
      "[data-testid='form-tributo'] table tbody tr:first-child input"
    );
    await faixaInputs.nth(0).fill("0");
    await faixaInputs.nth(1).fill("999");
    await faixaInputs.nth(3).fill("10");

    await page.locator("[data-testid='btn-salvar']").click();

    // Wait for success or error
    const sucesso = page.locator("[data-testid='sucesso']");
    const erro = page.locator(".alert-danger");
    await expect(sucesso.or(erro)).toBeVisible();

    if (await erro.isVisible()) {
      const msg = await erro.textContent();
      throw new Error(`Create tributo error: ${msg}`);
    }

    await expect(sucesso).toContainText("Tributo criado");
  });
});
