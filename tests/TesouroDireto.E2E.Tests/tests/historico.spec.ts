import { test, expect } from "@playwright/test";

test.describe("Historico Page", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/historico");
  });

  test("should have Historico link in navigation", async ({ page }) => {
    await page.goto("/");
    const navLink = page.locator('a[href="historico"]');
    await expect(navLink).toBeVisible();
  });

  test("should display titulo selector", async ({ page }) => {
    await expect(page.locator("h1")).toContainText("Histórico");
    await expect(page.locator("select#titulo")).toBeVisible();
  });

  test("should display chart canvas after selecting titulo", async ({
    page,
  }) => {
    const select = page.locator("select#titulo");
    await select.selectOption({ index: 1 });
    await expect(page.locator("canvas#priceChart")).toBeVisible({
      timeout: 10_000,
    });
  });

  test("should display data table after selecting titulo", async ({
    page,
  }) => {
    const select = page.locator("select#titulo");
    await select.selectOption({ index: 1 });
    await expect(page.locator("table")).toBeVisible({ timeout: 10_000 });
  });
});
