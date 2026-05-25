import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { waitForUrl } from "./e2e-wait";
import { resolveDbConnection } from "./prepare-database";

const frontendDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const repoRoot = path.resolve(frontendDir, "..");
const apiOrigin = process.env.PLAYWRIGHT_API_URL ?? "http://127.0.0.1:5100";
const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? "http://127.0.0.1:5173";

export async function startE2eServers(): Promise<void> {
  const isCi = process.env.CI === "true";
  const publishedApiDll = process.env.PLAYWRIGHT_API_DLL;
  const dbConnection = resolveDbConnection();

  const backendArgs =
    isCi && publishedApiDll
      ? [publishedApiDll, "--urls", apiOrigin]
      : isCi
        ? [
            "run",
            "--project",
            path.join(repoRoot, "backend/VisionPaint.csproj"),
            "--configuration",
            "Release",
            "--no-build",
            "--no-launch-profile",
            "--urls",
            apiOrigin,
          ]
        : [
            "run",
            "--project",
            path.join(repoRoot, "backend/VisionPaint.csproj"),
            "--configuration",
            "Release",
            "--no-launch-profile",
            "--urls",
            apiOrigin,
          ];

  const backend = spawn("dotnet", backendArgs, {
    cwd: repoRoot,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: "E2E",
      ConnectionStrings__DefaultConnection: dbConnection,
      VISIONPAINT_CORS_ORIGINS: "http://127.0.0.1:5173,http://localhost:5173",
      DOTNET_EnableDiagnostics: "0",
    },
    detached: true,
    stdio: "ignore",
    windowsHide: true,
  });
  backend.unref();

  const viteBin = path.join(frontendDir, "node_modules", "vite", "bin", "vite.js");
  const viteArgs = isCi
    ? [viteBin, "preview", "--host", "127.0.0.1", "--port", "5173", "--strictPort"]
    : [viteBin, "--host", "127.0.0.1", "--port", "5173"];
  const vite = spawn(process.execPath, viteArgs, {
    cwd: frontendDir,
    env: {
      ...process.env,
      VITE_API_URL: `${apiOrigin}/api`,
      PLAYWRIGHT: "1",
      NODE_OPTIONS: isCi ? "--max-old-space-size=768" : process.env.NODE_OPTIONS,
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
