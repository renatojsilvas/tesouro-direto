import { test, expect } from "@playwright/test";

test.describe("Titulos Page", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/titulos");
  });

  test("should have Titulos link in navigation", async ({ page }) => {
    await page.goto("/");
    const navLink = page.locator('a[href="titulos"]');
    await expect(navLink).toBeVisible();
  });

  test("should display titulos table", async ({ page }) => {
    await expect(page.locator("h1")).toContainText("Títulos");
    await expect(page.locator("table")).toBeVisible({ timeout: 10_000 });
    const rows = page.locator("table tbody tr");
    await expect(rows).not.toHaveCount(0);
  });

  test("should have indexador filter", async ({ page }) => {
    const filter = page.locator("select#indexador");
    await expect(filter).toBeVisible();
  });

  test("should have status filter", async ({ page }) => {
    const filter = page.locator("select#status");
    await expect(filter).toBeVisible();
  });

  test("should show result count", async ({ page }) => {
    await expect(page.locator("[data-testid='count']")).toBeVisible({
      timeout: 10_000,
    });
  });
});
