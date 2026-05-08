# VisionPaint Test Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a durable backend integration test suite plus focused frontend browser/component coverage for auth, job management, and session behavior.

**Architecture:** Backend tests will run against a real disposable Postgres database and exercise the API through `WebApplicationFactory` so migrations, auth, transactions, and company scoping are validated end to end. Frontend coverage will use Playwright for browser/session flows and React Testing Library for hook and component state, with a shared API helper to keep auth headers and cookie-backed requests consistent.

**Tech Stack:** .NET 10, xUnit, ASP.NET Core `WebApplicationFactory`, Postgres, Playwright, React Testing Library, Vite, TypeScript.

---

### Task 1: Scaffold backend test project

**Files:**
- Create: `backend.Tests/backend.Tests.csproj`
- Create: `backend.Tests/Infrastructure/TestWebApplicationFactory.cs`
- Create: `backend.Tests/IntegrationTests/AuthIntegrationTests.cs`
- Create: `backend.Tests/IntegrationTests/JobsIntegrationTests.cs`
- Modify: `backend/Program.cs`
- Modify: `VisionPaint.sln`

- [ ] **Step 1: Add the test project skeleton**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\backend\VisionPaint.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Make `Program` visible to the test host**

```csharp
public partial class Program
{
}
```

- [ ] **Step 3: Add the test web application factory**

```csharp
using Microsoft.AspNetCore.Mvc.Testing;

namespace VisionPaint.Tests.Infrastructure;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}
```

- [ ] **Step 4: Add empty integration test shells**

```csharp
namespace VisionPaint.Tests.IntegrationTests;

public sealed class AuthIntegrationTests
{
}
```

```csharp
namespace VisionPaint.Tests.IntegrationTests;

public sealed class JobsIntegrationTests
{
}
```

- [ ] **Step 5: Wire the project into the solution**

Add the project entry to `VisionPaint.sln` so `dotnet test` can discover it from the repo root.

- [ ] **Step 6: Verify the project loads**

Run: `dotnet test backend.Tests/backend.Tests.csproj --list-tests`
Expected: the project restores successfully and the test runner discovers the project, even before any facts are added.

### Task 2: Add backend integration test infrastructure

**Files:**
- Create: `backend.Tests/Infrastructure/PostgresTestDatabase.cs`
- Create: `backend.Tests/Infrastructure/TestDatabaseInitializer.cs`
- Create: `backend.Tests/Infrastructure/TestAuthClient.cs`

- [ ] **Step 1: Define the disposable Postgres test database helper**

Use a fixture that creates one temporary database per test run, applies the repo migrations, and exposes the connection string to the web host.

- [ ] **Step 2: Add a database reset helper**

Reset the database between tests so each test starts from a known state without depending on ordering.

- [ ] **Step 3: Add authenticated request helpers**

Create a small client wrapper that captures cookies and default headers so auth flows are easy to express in tests.

- [ ] **Step 4: Verify the fixture boots**

Run: `dotnet test backend.Tests/backend.Tests.csproj`
Expected: the test host starts, connects to the disposable database, and exits cleanly.

### Task 3: Cover auth flows with integration tests

**Files:**
- Modify: `backend.Tests/IntegrationTests/AuthIntegrationTests.cs`

- [ ] **Step 1: Write bootstrap coverage**

Verify that the first account creation writes `auth_user`, `person`, and `company_member`, and that the response includes both the authenticated user payload and CSRF token.

- [ ] **Step 2: Write login coverage**

Verify valid login succeeds, invalid password fails, and the returned session can be used for later authenticated calls.

- [ ] **Step 3: Write status/me coverage**

Verify anonymous status exposes bootstrap availability and authenticated status/me reflect the cookie-backed session.

- [ ] **Step 4: Verify the auth tests**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~AuthIntegrationTests`
Expected: auth tests pass against the disposable Postgres database.

### Task 4: Cover job persistence and authorization

**Files:**
- Modify: `backend.Tests/IntegrationTests/JobsIntegrationTests.cs`

- [ ] **Step 1: Write create-job coverage**

Verify job creation stores the job and initial `job_status_history` record in a single transaction and applies company/person ownership from the authenticated user.

- [ ] **Step 2: Write update-job coverage**

Verify status changes produce history rows, no-op status updates do not create duplicate history, and invalid status or priority values return `400`.

- [ ] **Step 3: Write archive-job coverage**

Verify archive transitions the job to `cancelled`, sets `closed_at`, and records an archive history entry.

- [ ] **Step 4: Write company-scope coverage**

Verify a user cannot see or mutate jobs from another company.

- [ ] **Step 5: Verify the jobs tests**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~JobsIntegrationTests`
Expected: job tests pass and prove the transaction and scoping behavior.

### Task 5: Add frontend browser coverage

**Files:**
- Create: `frontend/tests/e2e/auth.spec.ts`
- Create: `frontend/tests/e2e/jobs.spec.ts`
- Modify: `frontend/playwright.config.ts`

- [ ] **Step 1: Add Playwright config**

Configure the test runner to point at the local frontend and API URLs used by the repo.

- [ ] **Step 2: Write auth flow tests**

Cover bootstrap, login, refresh persistence, and logout in a real browser.

- [ ] **Step 3: Write job flow tests**

Cover job creation and archive behavior in the browser, including visible error handling.

- [ ] **Step 4: Verify browser tests**

Run: `npx playwright test`
Expected: the auth and job browser flows pass locally against the dev stack.

### Task 6: Add frontend component and hook tests

**Files:**
- Create: `frontend/src/hooks/__tests__/useAuth.test.tsx`
- Create: `frontend/src/hooks/__tests__/useJobs.test.tsx`
- Create: `frontend/src/__tests__/App.test.tsx`

- [ ] **Step 1: Write hook tests**

Cover the state transitions in `useAuth` and `useJobs`, including success and error handling.

- [ ] **Step 2: Write app rendering tests**

Cover loading, unauthenticated, and authenticated dashboard rendering.

- [ ] **Step 3: Verify the RTL tests**

Run: `npm test`
Expected: the hook and app tests pass quickly without the browser.

