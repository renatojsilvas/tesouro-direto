import { test, expect } from "@playwright/test";
import { navigateTo } from "./helpers";

test.describe("Titulos Page", () => {
  test.beforeEach(async ({ page }) => {
    await navigateTo(page, "/titulos");
  });

  test("should display titulos table with data", async ({ page }) => {
    await expect(page.locator("table")).toBeVisible();
    await expect(page.locator("table tbody tr")).not.toHaveCount(0);
  });

  test("should show count", async ({ page }) => {
    await expect(page.locator("[data-testid='count']")).toContainText(
      "título(s)"
    );
  });

  test("should have filters", async ({ page }) => {
    await expect(page.locator("select#indexador")).toBeVisible();
    await expect(page.locator("select#status")).toBeVisible();
  });

  test("should filter and still show data", async ({ page }) => {
    await expect(page.locator("table")).toBeVisible();
    await page.locator("select#indexador").selectOption("Selic");

    await page.waitForFunction(
      () => document.querySelectorAll("table tbody tr").length > 0
    );

    await expect(page.locator("[data-testid='count']")).toContainText(
      "título(s)"
    );
  });
});
