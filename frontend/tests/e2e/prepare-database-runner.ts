import path from "node:path";
import { fileURLToPath } from "node:url";
import { loadEnvFile, prepareE2eDatabase } from "./prepare-database";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
loadEnvFile(path.join(repoRoot, "backend.Tests", ".env"));
await prepareE2eDatabase();
