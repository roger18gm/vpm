# VisionPaint Auth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add ASP.NET Identity-based login with cookie sessions, map authenticated users to `person`, and protect the backend with company membership and role checks.

**Architecture:** The backend is the only trust boundary. ASP.NET Identity manages login accounts and cookie sessions, while the existing domain tables keep business data separate. The API will resolve the current user to `person`, then authorize access through `company_member` and role-aware checks.

**Tech Stack:** ASP.NET Core, ASP.NET Identity, Entity Framework Core, PostgreSQL, React, Vite, HTTP-only cookies, CORS with credentials.

---

### Task 1: Add Identity-backed auth data model

**Files:**
- Modify: `backend/VisionPaint.csproj`
- Modify: `backend/Data/AppDbContext.cs`
- Create: `backend/Models/ApplicationUser.cs`
- Create: `backend/Models/ApplicationRole.cs`
- Create: `backend/Models/CompanyMember.cs`
- Create: `backend/Models/Person.cs`
- Create: `backend/Models/Company.cs`
- Create: `backend/Models/Client.cs`
- Create: `backend/Models/ClientContact.cs`
- Create: `backend/Models/JobAssignment.cs`
- Create: `backend/Models/TimeEntry.cs`
- Create: `backend/Models/TimeBreak.cs`
- Create: `backend/Models/JobNote.cs`
- Create: `backend/Models/JobArea.cs`
- Create: `backend/Models/JobPhoto.cs`
- Create: `backend/Models/ChecklistTemplate.cs`
- Create: `backend/Models/ChecklistTemplateItem.cs`
- Create: `backend/Models/JobChecklistItem.cs`
- Create: `backend/Models/JobStatusHistory.cs` if it needs additional auth fields

- [ ] **Step 1: Write the failing test**

Create a backend test project file if needed and add a model-mapping test that asserts `AppDbContext` exposes `DbSet` properties for the auth-related domain tables and that `Job` maps to `job` with `company_id` and `status`.

```csharp
[Fact]
public void AppDbContext_exposes_auth_and_domain_sets()
{
    using var context = CreateContext();

    Assert.NotNull(context.Set<Person>());
    Assert.NotNull(context.Set<Company>());
    Assert.NotNull(context.Set<CompanyMember>());
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend`

Expected: fail because the Identity classes and/or DbSets do not exist yet.

- [ ] **Step 3: Write minimal implementation**

Add the package references for ASP.NET Identity EF Core, update `AppDbContext` to inherit from `IdentityDbContext<ApplicationUser>`, add the new model classes, and keep the existing `job` mapping intact.

```csharp
public class ApplicationUser : IdentityUser
{
    public int? PersonId { get; set; }
    public Person? Person { get; set; }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/VisionPaint.csproj backend/Data/AppDbContext.cs backend/Models
git commit -m "feat: add identity data model"
```

### Task 2: Add auth endpoints and session plumbing

**Files:**
- Modify: `backend/Program.cs`
- Create: `backend/Controllers/AuthController.cs`
- Modify: `backend/Controllers/JobsController.cs`
- Create: `backend/Services/CurrentUserService.cs`
- Create: `backend/Services/CurrentCompanyService.cs`
- Create: `backend/Authorization/CompanyRoleRequirement.cs`
- Create: `backend/Authorization/CompanyRoleHandler.cs`

- [ ] **Step 1: Write the failing test**

Add controller tests that assert:
- anonymous requests to `/api/jobs` get `401`
- a logged-in user without membership gets `403`
- a company member can list jobs

```csharp
[Fact]
public async Task Anonymous_request_to_jobs_is_unauthorized()
{
    var response = await Client.GetAsync("/api/jobs");
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend`

Expected: fail because the auth pipeline and policies are not wired yet.

- [ ] **Step 3: Write minimal implementation**

Configure ASP.NET Identity, cookie auth, CORS with credentials, and authorization policies. Add `AuthController` with `login`, `logout`, and `me` endpoints. Update `JobsController` to require authenticated company members and to use a current-user service instead of trusting incoming IDs.

```csharp
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CompanyMember", policy =>
        policy.Requirements.Add(new CompanyRoleRequirement("owner", "admin", "manager", "crew")));
});
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/Program.cs backend/Controllers/AuthController.cs backend/Controllers/JobsController.cs backend/Services backend/Authorization
git commit -m "feat: add cookie auth and authorization"
```

### Task 3: Add login UI and cookie-aware API calls

**Files:**
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/hooks/useJobs.ts`
- Create: `frontend/src/hooks/useSession.ts`
- Create: `frontend/src/components/LoginForm.tsx`
- Modify: `frontend/src/main.tsx`

- [ ] **Step 1: Write the failing test**

Add a frontend test or a focused TypeScript check that expects API requests to include credentials and that the app renders a login form when no session exists.

```ts
expect(fetch).toHaveBeenCalledWith(
  expect.stringContaining("/api/jobs"),
  expect.objectContaining({ credentials: "include" })
);
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm run build`

Expected: fail because the session hook and login UI do not exist yet.

- [ ] **Step 3: Write minimal implementation**

Add a login form, store session state in a small hook, and update all API calls to use `credentials: "include"`.

```ts
await fetch(`${API_URL}/jobs`, {
  method: "GET",
  credentials: "include",
});
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npm run build`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src
git commit -m "feat: add browser session login flow"
```

### Task 4: Seed and verify the first auth path

**Files:**
- Create: `backend/README.md` or `docs/auth-checklist.md` if needed
- Modify: `backend/appsettings.local.json` if local auth settings are required
- Create: `database/seed.sql` if we want repeatable local data

- [ ] **Step 1: Write the failing test**

Create a smoke test or manual verification script that checks:
- login succeeds
- cookie persists on refresh
- `/api/jobs` is accessible only when signed in

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend`

Expected: fail until the login path is wired and seeded.

- [ ] **Step 3: Write minimal implementation**

Create one seeded `ApplicationUser`, link it to a `person`, and seed one `company_member` row with an owner role so the login flow can be exercised end to end.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend database docs/superpowers/plans
git commit -m "test: verify auth flow"
```

## Spec Coverage Check

- Session auth and login/logout: Task 2
- Mapping auth user to `person`: Task 1 and Task 2
- Company membership and roles: Task 2
- Browser cookie flow: Task 3
- Internal staff only first pass: Task 2 and Task 4
- Tests and verification: every task includes a failing test, implementation, and pass check

## Notes

- Keep the backend as the only authority for auth and authorization.
- Do not introduce JWTs in this pass.
- Keep the browser cookie HTTP-only and secure.
- If a task reveals a need for a schema change, create a new migration rather than editing the old one.
