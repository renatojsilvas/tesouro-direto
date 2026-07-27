import { test, expect } from "@playwright/test";
import { navigateTo, selectFirstTitulo } from "./helpers";

async function selectSelic2029(page: import("@playwright/test").Page) {
  const select = page.locator("select#titulo");
  await expect(select.locator("option")).not.toHaveCount(1);

  const option = select
    .locator("option", { hasText: "Selic" })
    .filter({ hasText: "2029" });
  const value = await option.first().getAttribute("value");

  await select.selectOption(value ?? "");
}

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

    await expect(page.locator("[data-testid='pg-info']")).toContainText(
      "registro(s)"
    );
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

  test("should paginate precos across pages", async ({ page }) => {
    await selectSelic2029(page);
    await expect(page.locator("[data-testid='tabela-precos']")).toBeVisible();

    await expect(page.locator("[data-testid='pg-first']")).toBeDisabled();
    await expect(page.locator("[data-testid='pg-prev']")).toBeDisabled();
    await expect(page.locator("[data-testid='pg-next']")).toBeEnabled();

    const primeiraLinha = page
      .locator("[data-testid='tabela-precos'] tbody tr")
      .first();
    const primeiraLinhaTexto = await primeiraLinha.textContent();

    await page.click("[data-testid='pg-next']");

    await page.waitForFunction(
      (textoAnterior) =>
        document
          .querySelector("[data-testid='tabela-precos'] tbody tr")
          ?.textContent !== textoAnterior,
      primeiraLinhaTexto
    );

    await expect(page.locator("[data-testid='pg-first']")).toBeEnabled();
    await expect(page.locator("[data-testid='pg-prev']")).toBeEnabled();

    await page.click("[data-testid='pg-last']");

    await page.waitForFunction(
      () =>
        document
          .querySelector("[data-testid='pg-next']")
          ?.hasAttribute("disabled") === true
    );

    await expect(page.locator("[data-testid='pg-next']")).toBeDisabled();
    await expect(page.locator("[data-testid='pg-last']")).toBeDisabled();
  });
});
