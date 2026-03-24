import { Page, expect } from "@playwright/test";

/**
 * Navigate and wait for Blazor Server to be interactive.
 */
export async function navigateTo(page: Page, path: string) {
  await page.goto(path);
  await page.waitForFunction(
    () => typeof (window as any).Blazor !== "undefined"
  );
}

/**
 * Select first real titulo from dropdown, with retry if Blazor missed the event.
 */
export async function selectFirstTitulo(page: Page) {
  const select = page.locator("select#titulo");
  await expect(select.locator("option")).not.toHaveCount(1);
  await select.selectOption({ index: 1 });

  const value = await select.inputValue();
  if (!value || value === "") {
    await page.waitForTimeout(1000);
    await select.selectOption({ index: 1 });
  }
}
