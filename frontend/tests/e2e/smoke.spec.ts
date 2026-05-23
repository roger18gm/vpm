import { expect, test } from "@playwright/test";

test("bootstrap, create a job, archive it, and sign out", async ({ page }) => {
  await page.goto("/");

  await expect(page.getByRole("heading", { name: "Create the first account" })).toBeVisible();

  await page.getByLabel("Email").fill("owner@example.com");
  await page.getByLabel("Password").fill("Password123!");
  await page.getByRole("button", { name: "Create account" }).click();

  await expect(page).toHaveURL(/\/dashboard/);

  await page.getByRole("link", { name: "Jobs" }).click();
  await expect(page.getByRole("heading", { name: "Jobs" })).toBeVisible();

  await page.getByRole("link", { name: "New" }).click();
  await expect(page.getByRole("heading", { name: "New job" })).toBeVisible();

  await page.getByLabel("Job title *").fill("Deck repaint");
  await page.getByLabel("Priority").selectOption("high");
  await page.getByRole("button", { name: "Save job" }).click();

  await expect(page.getByRole("heading", { name: "Deck repaint" })).toBeVisible();

  page.once("dialog", (dialog) => dialog.accept());
  await page.getByRole("button", { name: "Archive" }).click();

  await expect(page).toHaveURL(/\/jobs/);
  await expect(page.getByRole("link", { name: "Deck repaint" })).toHaveCount(0);

  await page.goto("/account");
  await page.getByRole("button", { name: "Sign out" }).click();

  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
});
