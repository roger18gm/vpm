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

The Firebase app (`https://visionpainting.web.app`) calls this API cross-origin with cookies. After deploying, confirm in **Azure Portal → vision-paint-api → Configuration → Application settings**:

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

- `POST /api/auth/login` → `{ accessToken, refreshToken, accessTokenExpiresAt, user }`
- `POST /api/auth/refresh` → new token pair
- `POST /api/auth/logout` → client discards tokens (stateless)

Local dev sets `VISIONPAINT_JWT_SIGNING_KEY` in `Properties/launchSettings.json`.

## Run the backend tests

The integration tests use a local PostgreSQL 17 instance and create a disposable test database for each run.

1. Make sure PostgreSQL is running locally.
2. Provide `VISIONPAINT_TEST_PGADMIN` (admin connection string that can create databases) using either:
   - **`backend.Tests/.env`** — copy `backend.Tests/.env.example` to `.env` and set your password (loaded automatically before tests), or
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
