import { test, expect } from "@playwright/test";

test.describe("Simulador Page", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/simulador");
  });

  test("should have Simulador link in navigation", async ({ page }) => {
    await page.goto("/");
    const navLink = page.locator('a[href="simulador"]');
    await expect(navLink).toBeVisible();
    await expect(navLink).toContainText("Simulador");
  });

  test("should display simulador form", async ({ page }) => {
    await expect(page.locator("h1")).toContainText("Simulador");
    await expect(page.locator("select#titulo")).toBeVisible();
    await expect(page.locator("input#valorInvestido")).toBeVisible();
    await expect(page.locator("input#dataCompra")).toBeVisible();
    await expect(page.locator("input#taxaContratada")).toBeVisible();
    await expect(page.locator("input#projecaoAnual")).toBeVisible();
    await expect(page.locator("button#simular")).toBeVisible();
  });

  test("should load titulos in dropdown", async ({ page }) => {
    const options = page.locator("select#titulo option");
    await expect(options).not.toHaveCount(0);
  });

  test("should show results after simulation", async ({ page }) => {
    // Select first titulo
    const select = page.locator("select#titulo");
    await select.selectOption({ index: 1 });

    // Fill form
    await page.fill("input#valorInvestido", "10000");
    await page.fill("input#dataCompra", "2024-01-02");
    await page.fill("input#taxaContratada", "12");

    // Submit
    await page.click("button#simular");

    // Wait for results
    await expect(page.locator("[data-testid='resultado']")).toBeVisible({
      timeout: 10_000,
    });
    await expect(page.locator("[data-testid='valor-bruto']")).toBeVisible();
    await expect(page.locator("[data-testid='valor-liquido']")).toBeVisible();
    await expect(
      page.locator("[data-testid='rendimento-bruto']")
    ).toBeVisible();
  });

  test("should show error for invalid input", async ({ page }) => {
    await page.click("button#simular");
    await expect(page.locator("[data-testid='erro']")).toBeVisible({
      timeout: 5_000,
    });
  });
});
