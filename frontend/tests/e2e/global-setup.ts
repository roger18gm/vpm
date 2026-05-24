import { execSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { loadEnvFile, prepareE2eDatabase } from "./prepare-database";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "..");

export default async function globalSetup() {
  loadEnvFile(path.join(repoRoot, "backend.Tests", ".env"));
  await prepareE2eDatabase();

  execSync("dotnet build backend/VisionPaint.csproj --configuration Release", {
    cwd: repoRoot,
    stdio: "inherit",
  });
}
