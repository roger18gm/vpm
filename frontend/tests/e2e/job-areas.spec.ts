import { expect, test } from "@playwright/test";

test("manager adds a job area and sees progress on job detail", async ({ page }) => {
  const email = `owner-${Date.now()}@example.com`;
  const password = "Password123!";

  await page.goto("/login");
  await expect(page.getByRole("heading", { name: "Create the first account" })).toBeVisible();
  await page.getByLabel("Email").fill(email);
  await page.getByRole("textbox", { name: /^Password/ }).fill(password);
  const bootstrapResponse = page.waitForResponse(
    (response) => response.url().includes("/api/auth/bootstrap") && response.ok()
  );
  await page.getByRole("button", { name: "Create account" }).click();
  await bootstrapResponse;

  await page.goto("/jobs/new");
  await page.getByLabel("Job title *").fill("Interior repaint");
  await page.getByRole("button", { name: "Save job" }).click();
  await expect(page.getByRole("heading", { name: "Interior repaint" })).toBeVisible();

  await page.getByRole("link", { name: "View areas" }).click();
  await expect(page.getByRole("heading", { name: "Areas" })).toBeVisible();

  await page.getByLabel("Area name").fill("Kitchen");
  await page.getByRole("button", { name: "Add area" }).click();
  await expect(page.getByLabel("Name for Kitchen")).toBeVisible();

  await page.getByLabel("Status for Kitchen").selectOption("completed");
  await expect(page.getByText("Completed").first()).toBeVisible();

  await page.getByRole("link", { name: "← Job" }).click();
  await expect(page.getByText("1 of 1 completed")).toBeVisible();
});
