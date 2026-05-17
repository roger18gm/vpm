## Run the backend

```powershell
cd backend
dotnet restore
dotnet run
```

Set `ConnectionStrings__DefaultConnection` (or User Secrets) to your PostgreSQL connection string.

## Azure production (vision-paint-api)

The Firebase app (`https://visionpainting.web.app`) calls this API cross-origin with cookies. After deploying, confirm in **Azure Portal → vision-paint-api → Configuration → Application settings**:

| Setting | Example |
|---------|---------|
| `ConnectionStrings__DefaultConnection` | Supabase/Postgres connection string |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `VISIONPAINT_CORS_ORIGINS` | (optional) comma-separated extra origins; overrides `appsettings.Production.json` when set |

Production CORS defaults are in `appsettings.Production.json` (`visionpainting.web.app` and `visionpainting.firebaseapp.com`).

If login still fails after CORS is fixed, check **Log stream** for database or migration errors (a missing connection string often returns HTTP 500 without CORS headers, which Chrome reports as a CORS error).

## Run the backend tests

The integration tests use a local PostgreSQL 17 instance and create a disposable test database for each run.

1. Make sure PostgreSQL is running locally.
2. Set `VISIONPAINT_TEST_PGADMIN` to an admin connection string that can create databases.
3. Run the test project from the repo root:

```powershell
$env:VISIONPAINT_TEST_PGADMIN='Host=127.0.0.1;Port=5432;Username=postgres;Password=<your-password>;Database=postgres'
dotnet test backend.Tests/backend.Tests.csproj --no-restore
```

To run a single test:

```powershell
$env:VISIONPAINT_TEST_PGADMIN='Host=127.0.0.1;Port=5432;Username=postgres;Password=<your-password>;Database=postgres'
dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~AuthIntegrationTests.Login_restores_the_session_after_logout --no-restore
```
