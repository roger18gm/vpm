import { defineConfig } from "@playwright/test";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { loadEnvFile } from "./tests/e2e/prepare-database";

const frontendDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(frontendDir, "..");

loadEnvFile(path.join(repoRoot, "backend.Tests", ".env"));

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? "http://127.0.0.1:5173";

export default defineConfig({
  testDir: "./tests/e2e",
  timeout: 60_000,
  forbidOnly: !!process.env.CI,
  retries: 0,
  globalSetup: "./tests/e2e/global-setup.ts",
  globalTeardown: "./tests/e2e/global-teardown.ts",
  expect: {
    timeout: 15_000,
  },
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL,
    trace: process.env.CI ? "off" : "retain-on-failure",
    actionTimeout: 15_000,
    launchOptions: process.env.CI
      ? {
          args: ["--disable-dev-shm-usage", "--no-sandbox", "--disable-gpu"],
        }
      : undefined,
  },
});
