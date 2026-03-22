import { test, expect } from "@playwright/test";

test.describe("Tributos Page", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/tributos");
  });

  test("should have Tributos link in navigation", async ({ page }) => {
    await page.goto("/");
    const navLink = page.locator('a[href="tributos"]');
    await expect(navLink).toBeVisible();
  });

  test("should display tributos table", async ({ page }) => {
    await expect(page.locator("h1")).toContainText("Tributos");
    await expect(page.locator("table")).toBeVisible({ timeout: 10_000 });
  });

  test("should have new tributo button", async ({ page }) => {
    await expect(page.locator("button#novo-tributo")).toBeVisible();
  });

  test("should show create form when clicking new", async ({ page }) => {
    await page.click("button#novo-tributo");
    await expect(page.locator("[data-testid='form-tributo']")).toBeVisible();
    await expect(page.locator("input#nome")).toBeVisible();
  });
});
