import path from "node:path";
import { fileURLToPath } from "node:url";
import { loadEnvFile } from "./prepare-database";
import { startE2eServers } from "./start-e2e-servers";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
loadEnvFile(path.join(repoRoot, "backend.Tests", ".env"));
await startE2eServers();
