import { expect, test } from "./app-test";

test("manager applies surface prep and sees progress on job detail", async ({ page }) => {
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

  await page.getByRole("link", { name: "View checklist" }).click();
  await expect(page.getByRole("heading", { name: "Checklist" })).toBeVisible();
  await page.getByLabel("Checklist template").selectOption({ label: "Surface prep" });
  await page.getByRole("button", { name: "Apply" }).click();
  await expect(page.getByLabel("Status for Sanding")).toBeVisible();

  await page.getByLabel("Status for Sanding").selectOption("done");
  await expect(page.getByLabel("Status for Sanding")).toHaveValue("done");

  await page.getByRole("link", { name: "← Job" }).click();
  await expect(page.getByText("1 of 4 done")).toBeVisible();
});
