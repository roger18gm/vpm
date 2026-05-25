import path from "node:path";
import { fileURLToPath } from "node:url";
import { loadEnvFile, prepareE2eDatabase } from "./prepare-database";
import { startE2eServers } from "./start-e2e-servers";
import { stopE2eServers } from "./stop-e2e-servers";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");

export default async function globalSetup() {
  loadEnvFile(path.join(repoRoot, "backend.Tests", ".env"));

  if (process.env.PLAYWRIGHT_SKIP_WEBSERVER === "true") {
    if (process.env.PLAYWRIGHT_SKIP_DB_PREP !== "true") {
      await prepareE2eDatabase();
    }
    return;
  }

  stopE2eServers();
  await prepareE2eDatabase();
  await startE2eServers();
}
