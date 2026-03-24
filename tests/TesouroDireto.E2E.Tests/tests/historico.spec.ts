import { test, expect } from "@playwright/test";
import { navigateTo, selectFirstTitulo } from "./helpers";

test.describe("Historico Page", () => {
  test.beforeEach(async ({ page }) => {
    await navigateTo(page, "/historico");
  });

  test("should display titulo selector", async ({ page }) => {
    await expect(page.locator("h1")).toContainText("Historico");
    await expect(page.locator("select#titulo")).toBeVisible();
  });

  test("should display chart and table after selecting titulo", async ({
    page,
  }) => {
    await selectFirstTitulo(page);

    await expect(page.locator("canvas#priceChart")).toBeVisible();
    await expect(page.locator("[data-testid='tabela-precos']")).toBeVisible();
    await expect(
      page.locator("[data-testid='tabela-precos'] tbody tr")
    ).not.toHaveCount(0);
  });

  test("should display record count", async ({ page }) => {
    await selectFirstTitulo(page);

    await expect(
      page.locator("p.text-muted:has-text('registro(s)')")
    ).toBeVisible();
  });

  test("should show empty state for impossible date range", async ({
    page,
  }) => {
    await selectFirstTitulo(page);
    await expect(page.locator("[data-testid='tabela-precos']")).toBeVisible();

    await page.fill("input#dataInicio", "2099-01-01");
    await page.fill("input#dataFim", "2099-01-31");

    await expect(page.locator("[data-testid='vazio']")).toBeVisible();
  });
});
