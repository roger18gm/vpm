import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import pg from "pg";

const { Client } = pg;

const frontendDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const repoRoot = path.resolve(frontendDir, "..");

export function loadEnvFile(filePath: string) {
  if (!fs.existsSync(filePath)) {
    return;
  }

  for (const line of fs.readFileSync(filePath, "utf8").split(/\r?\n/u)) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) {
      continue;
    }

    const separator = trimmed.indexOf("=");
    if (separator === -1) {
      continue;
    }

    const key = trimmed.slice(0, separator).trim();
    const value = trimmed.slice(separator + 1).trim();
    if (key && process.env[key] === undefined) {
      process.env[key] = value;
    }
  }
}

export function resolveDbConnection() {
  if (process.env.PLAYWRIGHT_DB_CONNECTION) {
    return process.env.PLAYWRIGHT_DB_CONNECTION;
  }

  const admin = process.env.VISIONPAINT_TEST_PGADMIN;
  if (admin) {
    return formatAdoNetConnectionString(toPgConfig(replaceDatabaseName(admin, "visionpaint_e2e")));
  }

  return "Host=127.0.0.1;Port=5432;Username=postgres;Password=postgres;Database=visionpaint_e2e";
}

function formatAdoNetConnectionString(config: pg.ClientConfig) {
  const parts = [
    `Host=${config.host ?? "127.0.0.1"}`,
    `Port=${config.port ?? 5432}`,
    `Username=${config.user ?? "postgres"}`,
    `Password=${quoteConnectionValue(String(config.password ?? ""))}`,
    `Database=${config.database ?? "postgres"}`,
  ];

  if (config.ssl) {
    parts.push("SSL Mode=Require", "Trust Server Certificate=true");
  }

  return parts.join(";");
}

function quoteConnectionValue(value: string) {
  return /[;'=]/.test(value) ? `'${value.replace(/'/g, "''")}'` : value;
}

function replaceDatabaseName(connectionString: string, database: string) {
  if (/Database=/iu.test(connectionString)) {
    return connectionString.replace(/Database=[^;]*/iu, `Database=${database}`);
  }

  return `${connectionString};Database=${database}`;
}

function parseConnectionString(connectionString: string) {
  return Object.fromEntries(
    connectionString
      .split(";")
      .filter(Boolean)
      .map((segment) => {
        const separator = segment.indexOf("=");
        return [segment.slice(0, separator).trim(), segment.slice(separator + 1).trim()];
      })
  );
}

function buildAdminConnectionFromParts() {
  const host = process.env.PLAYWRIGHT_DB_HOST ?? process.env.DB_HOST;
  const port = process.env.PLAYWRIGHT_DB_PORT ?? process.env.DB_PORT ?? "5432";
  const user = process.env.PLAYWRIGHT_DB_USER ?? process.env.DB_USER ?? "postgres";
  const password = process.env.PLAYWRIGHT_DB_PASSWORD ?? process.env.DB_PASSWORD;
  const database = process.env.PLAYWRIGHT_DB_NAME ?? process.env.DB_NAME ?? "postgres";

  if (!host || !password) {
    return null;
  }

  return `Host=${host};Port=${port};Username=${user};Password=${password};Database=${database}`;
}

function toPgConfig(connectionString: string) {
  const parts = parseConnectionString(connectionString);
  const config: pg.ClientConfig = {
    host: parts.Host ?? "127.0.0.1",
    port: Number(parts.Port ?? "5432"),
    user: parts.Username ?? "postgres",
    password: parts.Password ?? "",
    database: parts.Database ?? "postgres",
  };

  const sslMode = parts["SSL Mode"] ?? parts.SslMode;
  if (sslMode && !/^disable$/iu.test(sslMode)) {
    config.ssl = { rejectUnauthorized: false };
  }

  return config;
}

export async function prepareE2eDatabase() {
  if (process.env.PLAYWRIGHT_SKIP_DB_SETUP === "true") {
    return;
  }

  loadEnvFile(path.join(repoRoot, "backend.Tests", ".env"));

  const admin =
    process.env.VISIONPAINT_TEST_PGADMIN ??
    process.env.PLAYWRIGHT_DB_ADMIN ??
    buildAdminConnectionFromParts();
  if (!admin) {
    throw new Error(
      "E2E database setup requires VISIONPAINT_TEST_PGADMIN (local: backend.Tests/.env) or CI admin connection env vars."
    );
  }

  const database = "visionpaint_e2e";
  const adminConfig = toPgConfig(replaceDatabaseName(admin, "postgres"));
  const adminClient = new Client(adminConfig);

  await adminClient.connect();
  try {
    await adminClient.query(`DROP DATABASE IF EXISTS ${database} WITH (FORCE)`);
    await adminClient.query(`CREATE DATABASE ${database}`);
  } finally {
    await adminClient.end();
  }

  const appConfig = toPgConfig(replaceDatabaseName(admin, database));
  const appClient = new Client(appConfig);
  await appClient.connect();

  try {
    const migrationsDir = path.join(repoRoot, "database", "migrations");
    const migrations = fs
      .readdirSync(migrationsDir)
      .filter((file) => file.endsWith(".sql"))
      .sort();

    for (const migration of migrations) {
      const sql = fs.readFileSync(path.join(migrationsDir, migration), "utf8");
      await appClient.query(sql);
    }
  } finally {
    await appClient.end();
  }
}
