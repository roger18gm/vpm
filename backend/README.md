## Run the backend

```powershell
cd backend
dotnet restore
dotnet run --launch-profile http
```

Uses `http://localhost:5000` with `ASPNETCORE_ENVIRONMENT=Development` (see `Properties/launchSettings.json`). Match `VITE_API_URL` in `frontend/.env`.

Set `ConnectionStrings__DefaultConnection` (or User Secrets) to your PostgreSQL connection string.

If you run with `ASPNETCORE_ENVIRONMENT=Production` locally, use HTTPS or cookies will expect a secure connection.

## Azure production (vision-paint-api)

The Firebase app (`https://visionpainting.web.app`) calls this API cross-origin with cookies. After deploying, confirm in **Azure Portal -> vision-paint-api -> Configuration -> Application settings**:

| Setting | Example |
|---------|---------|
| `ConnectionStrings__DefaultConnection` | Supabase/Postgres connection string |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `VISIONPAINT_CORS_ORIGINS` | (optional) comma-separated extra origins; overrides `appsettings.Production.json` when set |

Production CORS defaults are in `appsettings.Production.json` (`visionpainting.web.app` and `visionpainting.firebaseapp.com`).

If login still fails after CORS is fixed, check **Log stream** for database or migration errors (a missing connection string often returns HTTP 500 without CORS headers, which Chrome reports as a CORS error).

## JWT authentication

The API uses **Bearer JWT** (not cookies). Suited for the Firebase SPA and future mobile clients.

| Setting | Description |
|---------|-------------|
| `VISIONPAINT_JWT_SIGNING_KEY` | Required in Azure (min 32 characters). Generate a random secret; never commit it. |
| `Jwt:Issuer` / `Jwt:Audience` | Optional; default `VisionPaint` |

Endpoints:

- `POST /api/auth/login` -> `{ accessToken, refreshToken, accessTokenExpiresAt, user }`
- `POST /api/auth/refresh` -> new token pair, rotating the refresh token
- `POST /api/auth/logout` -> revokes the active refresh-token session and the client discards tokens

Local dev sets `VISIONPAINT_JWT_SIGNING_KEY` in `Properties/launchSettings.json`.

## Job photo storage

| Environment | Storage |
|-------------|---------|
| Development, Testing, E2E | Local files under `%TEMP%/visionpaint-photos`, served at `/api/local-files/...` |
| Production (Azure) | Supabase Storage via service role (`Supabase.Storage` client) |

Azure application settings:

| Setting | Description |
|---------|-------------|
| `Supabase:Url` | Project URL (e.g. `https://xxxx.supabase.co`) |
| `Supabase:ServiceRoleKey` | Service role key (server only) |
| `Supabase:Bucket` | Bucket name (default `job-photos`) |
| `Supabase:PublicBucket` | Set `true` if the bucket is public (skips signed URLs) |

Signed read URLs use the Supabase C# storage client (`CreateSignedUrl`). Upload uses `Upload` with the service role key in server-side headers only.

## Demo accounts (local / dev)

After owner bootstrap, run `database/seed/dev-crew-demo.sql` in the Supabase SQL editor (or psql). It creates:

| Email | Role | Name |
|-------|------|------|
| `manager@visionpaint.local` | manager | Demo Manager |
| `crew1@visionpaint.local` | crew | Alex Rivera |
| `crew2@visionpaint.local` | crew | Jordan Lee |
| `crew3@visionpaint.local` | crew | Sam Ortiz |

All seeded accounts use the **same password as the bootstrap owner**. Assign crew to jobs via **Assign crew** in the app (`PUT /api/jobs/{id}/assignments`).

## CI test prerequisites

GitHub Actions backend integration tests and UI E2E tests expect:

- a PostgreSQL 17 service container
- a `CI_POSTGRES_PASSWORD` GitHub secret
- backend CI sets `VISIONPAINT_TEST_PGADMIN` from that secret
- UI E2E uses the same `frontend/tests/e2e/prepare-database.ts` script as local runs (via Playwright `globalSetup`) against `visionpaint_e2e`

## Run the backend tests

The integration tests use a local PostgreSQL 17 instance and create a disposable test database for each run.

1. Make sure PostgreSQL is running locally.
2. Provide `VISIONPAINT_TEST_PGADMIN` (admin connection string that can create databases) using either:
   - **`backend.Tests/.env`** - copy `backend.Tests/.env.example` to `.env` and set your password (loaded automatically before tests), or
   - **Shell variable** (overrides `.env` if both are set):

```powershell
$env:VISIONPAINT_TEST_PGADMIN='Host=127.0.0.1;Port=5432;Username=postgres;Password=<your-password>;Database=postgres'
```

3. Run the test project from the repo root:

```powershell
dotnet test backend.Tests/backend.Tests.csproj --no-restore
```

To run a single test:

```powershell
$env:VISIONPAINT_TEST_PGADMIN='Host=127.0.0.1;Port=5432;Username=postgres;Password=<your-password>;Database=postgres'
dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~AuthIntegrationTests.Login_restores_the_session_after_logout --no-restore
```
