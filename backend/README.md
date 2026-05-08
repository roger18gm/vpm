## Run the backend

```powershell
cd backend
dotnet restore
dotnet run
```

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
