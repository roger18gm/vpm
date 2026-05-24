import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { waitForUrl } from "./e2e-wait";
import { loadEnvFile, prepareE2eDatabase, resolveDbConnection } from "./prepare-database";
import { stopE2eServers } from "./stop-e2e-servers";

const frontendDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const repoRoot = path.resolve(frontendDir, "..");
const apiOrigin = process.env.PLAYWRIGHT_API_URL ?? "http://127.0.0.1:5100";
const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? "http://127.0.0.1:5173";

export default async function globalSetup() {
  loadEnvFile(path.join(repoRoot, "backend.Tests", ".env"));

  if (process.env.PLAYWRIGHT_SKIP_WEBSERVER === "true") {
    await prepareE2eDatabase();
    return;
  }

  stopE2eServers();
  await prepareE2eDatabase();

  const dbConnection = resolveDbConnection();

  const backend = spawn(
    "dotnet",
    [
      "run",
      "--project",
      "backend/VisionPaint.csproj",
      "--configuration",
      "Release",
      "--no-launch-profile",
      "--urls",
      apiOrigin,
    ],
    {
      cwd: repoRoot,
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: "E2E",
        ConnectionStrings__DefaultConnection: dbConnection,
        VISIONPAINT_CORS_ORIGINS: "http://127.0.0.1:5173,http://localhost:5173",
      },
      detached: true,
      stdio: "ignore",
      windowsHide: true,
    }
  );
  backend.unref();

  const viteBin = path.join(frontendDir, "node_modules", "vite", "bin", "vite.js");
  const vite = spawn(process.execPath, [viteBin, "--host", "127.0.0.1", "--port", "5173"], {
    cwd: frontendDir,
    env: {
      ...process.env,
      VITE_API_URL: `${apiOrigin}/api`,
      PLAYWRIGHT: "1",
    },
    detached: true,
    stdio: "ignore",
    windowsHide: true,
  });
  vite.unref();

  await waitForUrl(`${apiOrigin}/api/auth/status`);
  await waitForUrl(baseURL);
  console.log(`E2E API ready at ${apiOrigin}`);
  console.log(`E2E UI ready at ${baseURL}`);
}
