import { defineConfig } from "@playwright/test";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { loadEnvFile } from "./tests/e2e/prepare-database";

const frontendDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(frontendDir, "..");
const isCi = !!process.env.CI;

loadEnvFile(path.join(repoRoot, "backend.Tests", ".env"));

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? "http://127.0.0.1:5173";

export default defineConfig({
  testDir: "./tests/e2e",
  timeout: 60_000,
  forbidOnly: isCi,
  retries: 0,
  workers: isCi ? 1 : undefined,
  fullyParallel: !isCi,
  globalSetup: "./tests/e2e/global-setup.ts",
  globalTeardown: "./tests/e2e/global-teardown.ts",
  expect: {
    timeout: 15_000,
  },
  reporter: isCi ? [["github"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL,
    trace: isCi ? "off" : "retain-on-failure",
    video: isCi ? "off" : "retain-on-failure",
    screenshot: isCi ? "off" : "only-on-failure",
    actionTimeout: 15_000,
    launchOptions: isCi
      ? {
          args: [
            "--disable-dev-shm-usage",
            "--no-sandbox",
            "--disable-gpu",
            "--disable-extensions",
            "--disable-background-networking",
          ],
        }
      : undefined,
  },
});
