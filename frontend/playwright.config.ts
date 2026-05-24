import { defineConfig } from "@playwright/test";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { loadEnvFile, resolveDbConnection } from "./tests/e2e/prepare-database";

const frontendDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(frontendDir, "..");
const backendDll = path.join(repoRoot, "backend", "bin", "Release", "net10.0", "VisionPaint.dll");

loadEnvFile(path.join(repoRoot, "backend.Tests", ".env"));

const apiOrigin = process.env.PLAYWRIGHT_API_URL ?? "http://127.0.0.1:5100";
const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? "http://127.0.0.1:5173";
const dbConnection = resolveDbConnection();
// Avoid picking up a dev API on the same port (wrong DB, canBootstrap: false).
const reuseExistingServer = process.env.PLAYWRIGHT_REUSE_SERVERS === "true";

export default defineConfig({
  testDir: "./tests/e2e",
  timeout: 60_000,
  forbidOnly: !!process.env.CI,
  retries: 0,
  globalSetup: "./tests/e2e/global-setup.ts",
  expect: {
    timeout: 15_000,
  },
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL,
    trace: "retain-on-failure",
    actionTimeout: 15_000,
  },
  webServer: process.env.PLAYWRIGHT_SKIP_WEBSERVER
    ? undefined
    : [
        {
          command: `dotnet exec "${backendDll}" --urls ${apiOrigin}`,
          cwd: repoRoot,
          url: `${apiOrigin}/api/auth/status`,
          timeout: 180_000,
          reuseExistingServer,
          env: {
            ...process.env,
            ASPNETCORE_ENVIRONMENT: "E2E",
            ConnectionStrings__DefaultConnection: dbConnection,
            VISIONPAINT_CORS_ORIGINS: "http://127.0.0.1:5173,http://localhost:5173",
          },
        },
        {
          command: "npm run dev -- --host 127.0.0.1 --port 5173",
          cwd: frontendDir,
          url: baseURL,
          timeout: 180_000,
          reuseExistingServer,
          env: {
            ...process.env,
            VITE_API_URL: `${apiOrigin}/api`,
          },
        },
      ],
});
