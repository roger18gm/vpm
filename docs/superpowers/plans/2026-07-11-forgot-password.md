# Forgot Password (Brevo) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Note:** Skip `git commit` steps if the human asked not to commit.

**Goal:** Let users reset a forgotten password via email link (Brevo), using hashed one-time DB tokens and revoking all refresh sessions on success.

**Architecture:** Add `password_reset_token` table and `PasswordResetService`. Expose anonymous `POST /api/auth/forgot-password` and `POST /api/auth/reset-password`. Send mail through `IEmailSender` → `BrevoEmailSender` (live Brevo in app environments). Integration tests replace `IEmailSender` with a recording double. Vue guest routes `/forgot-password` and `/reset-password` plus a login link.

**Tech Stack:** ASP.NET Core, EF Core, PostgreSQL, Brevo HTTP API (`POST https://api.brevo.com/v3/smtp/email`), Vue 3, Pinia, xUnit

**Spec:** [docs/superpowers/specs/2026-07-11-forgot-password-design.md](../specs/2026-07-11-forgot-password-design.md)

---

## File map

| File | Responsibility |
|------|----------------|
| `database/migrations/20260711_150000_password_reset_token.sql` | New table |
| `database/schema.sql` / `database/schema.md` | Keep schema docs in sync |
| `backend/Models/PasswordResetToken.cs` | EF entity |
| `backend/Data/AppDbContext.cs` | DbSet + mapping |
| `backend/Models/AuthContracts.cs` | Request DTOs |
| `backend/Services/IEmailSender.cs` | Abstraction |
| `backend/Services/BrevoOptions.cs` / `AppOptions.cs` / `AuthOptions.cs` | Config |
| `backend/Services/BrevoEmailSender.cs` | Brevo HTTP client |
| `backend/Services/PasswordResetService.cs` | Token + password + revoke logic |
| `backend/Controllers/AuthController.cs` | Two new endpoints |
| `backend/Program.cs` | DI + options |
| `backend/appsettings.json` | Non-secret defaults (FrontendBaseUrl, token hours, Brevo section placeholders) |
| `backend.Tests/Infrastructure/RecordingEmailSender.cs` | Test double |
| `backend.Tests/Infrastructure/TestWebApplicationFactory.cs` | Register recording sender + App config |
| `backend.Tests/IntegrationTests/PasswordResetIntegrationTests.cs` | Acceptance tests |
| `frontend/src/views/ForgotPasswordView.vue` | Request form |
| `frontend/src/views/ResetPasswordView.vue` | Set new password |
| `frontend/src/views/LoginView.vue` | Link + success query banner |
| `frontend/src/router/index.ts` | Guest routes |
| `frontend/src/stores/auth.ts` | API helpers (optional thin methods) |
| `docs/design/ui-spec.md` / `screen-map.md` | Document routes |

---

### Task 1: Migration + EF model

**Files:**
- Create: `database/migrations/20260711_150000_password_reset_token.sql`
- Modify: `database/schema.sql` (append table)
- Modify: `database/schema.md` (document table)
- Create: `backend/Models/PasswordResetToken.cs`
- Modify: `backend/Data/AppDbContext.cs`

- [ ] **Step 1: Add migration SQL**

```sql
create table if not exists public.password_reset_token (
    id uuid primary key default gen_random_uuid(),
    auth_user_id uuid not null references public.auth_user(id) on delete cascade,
    token_hash text not null,
    expires_at timestamp with time zone not null,
    used_at timestamp with time zone null,
    created_at timestamp with time zone not null default now()
);

create unique index if not exists password_reset_token_token_hash_uidx
    on public.password_reset_token (token_hash);

create index if not exists password_reset_token_auth_user_id_idx
    on public.password_reset_token (auth_user_id);
```

Mirror the same `create table` into `database/schema.sql` and add a short section in `database/schema.md`.

- [ ] **Step 2: Add entity + DbContext mapping**

```csharp
namespace VisionPaint.Models;

public sealed class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid AuthUserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

In `AppDbContext`:

```csharp
public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
```

Map to `password_reset_token` with snake_case column names (same style as `RefreshToken`).

- [ ] **Step 3: Commit (optional)**

```bash
git add database/migrations/20260711_150000_password_reset_token.sql database/schema.sql database/schema.md backend/Models/PasswordResetToken.cs backend/Data/AppDbContext.cs
git commit -m "Add password_reset_token table and EF model"
```

---

### Task 2: Config + `IEmailSender` + Brevo sender

**Files:**
- Create: `backend/Services/IEmailSender.cs`
- Create: `backend/Services/BrevoOptions.cs`
- Create: `backend/Services/AppOptions.cs`
- Create: `backend/Services/AuthOptions.cs` (or extend existing if present — prefer new small options types)
- Create: `backend/Services/BrevoEmailSender.cs`
- Modify: `backend/Program.cs`
- Modify: `backend/appsettings.json` (defaults only; no secrets)

- [ ] **Step 1: Add interfaces and options**

```csharp
namespace VisionPaint.Services;

public interface IEmailSender
{
    Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default);
}

public sealed class BrevoOptions
{
    public const string SectionName = "Brevo";
    public string ApiKey { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = "VisionPaint";
    public string ApiBaseUrl { get; set; } = "https://api.brevo.com";
}

public sealed class AppOptions
{
    public const string SectionName = "App";
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public int PasswordResetTokenHours { get; set; } = 1;
}
```

Resolve API key from config **or** env `VISIONPAINT_BREVO_API_KEY` (same pattern as JWT signing key resolver if one exists).

- [ ] **Step 2: Implement `BrevoEmailSender`**

Use `IHttpClientFactory` named client `"Brevo"`:

- `POST {ApiBaseUrl}/v3/smtp/email`
- Header: `api-key: {ApiKey}`
- Body:

```json
{
  "sender": { "name": "...", "email": "..." },
  "to": [{ "email": "to@example.com" }],
  "subject": "...",
  "htmlContent": "...",
  "textContent": "..."
}
```

On non-success status: throw `InvalidOperationException` with status code (caller logs and swallows for forgot-password).

- [ ] **Step 3: Register in `Program.cs`**

```csharp
builder.Services.Configure<BrevoOptions>(builder.Configuration.GetSection(BrevoOptions.SectionName));
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddHttpClient("Brevo");
builder.Services.AddSingleton<IEmailSender, BrevoEmailSender>();
```

Add to `appsettings.json` (no real API key):

```json
"App": {
  "FrontendBaseUrl": "http://localhost:5173"
},
"Auth": {
  "PasswordResetTokenHours": 1
},
"Brevo": {
  "ApiKey": "",
  "SenderEmail": "",
  "SenderName": "VisionPaint",
  "ApiBaseUrl": "https://api.brevo.com"
}
```

Document in `backend/README.md`: set `VISIONPAINT_BREVO_API_KEY`, `Brevo:SenderEmail`, and verify sender in Brevo dashboard.

- [ ] **Step 4: Build**

Run: `dotnet build backend/VisionPaint.csproj`

Expected: succeeded.

- [ ] **Step 5: Commit (optional)**

```bash
git add backend/Services backend/Program.cs backend/appsettings.json backend/README.md
git commit -m "Add Brevo IEmailSender for transactional email"
```

---

### Task 3: Recording email sender + test factory wiring

**Files:**
- Create: `backend.Tests/Infrastructure/RecordingEmailSender.cs`
- Modify: `backend.Tests/Infrastructure/TestWebApplicationFactory.cs`
- Modify: `backend.Tests/Infrastructure/BackendIntegrationFixture.cs` (expose recording sender)

- [ ] **Step 1: Recording sender**

```csharp
namespace VisionPaint.Tests.Infrastructure;

public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<(string To, string Subject, string Html, string? Text)> Sent { get; } = new();

    public Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        Sent.Enqueue((toEmail, subject, htmlBody, textBody));
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Replace `IEmailSender` in test host**

In `TestWebApplicationFactory.ConfigureWebHost`, after config:

```csharp
builder.ConfigureTestServices(services =>
{
    services.RemoveAll<IEmailSender>();
    services.AddSingleton<RecordingEmailSender>();
    services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<RecordingEmailSender>());
});
```

Also set in-memory:

```csharp
["App:FrontendBaseUrl"] = "http://localhost:5173",
["Auth:PasswordResetTokenHours"] = "1"
```

Expose `RecordingEmailSender` from `BackendIntegrationFixture` via `Factory.Services.GetRequiredService<RecordingEmailSender>()` (create a scope if needed — singleton is fine).

- [ ] **Step 3: Commit (optional)**

```bash
git add backend.Tests/Infrastructure
git commit -m "Add recording email sender for auth integration tests"
```

---

### Task 4: Failing integration tests for forgot/reset

**Files:**
- Create: `backend.Tests/IntegrationTests/PasswordResetIntegrationTests.cs`
- Modify: `backend/Models/AuthContracts.cs` (add DTOs early so tests compile — or use anonymous JSON)

- [ ] **Step 1: Add request DTOs**

In `AuthContracts.cs`:

```csharp
public sealed record ForgotPasswordRequest([Required, EmailAddress] string Email);

public sealed record ResetPasswordRequest(
    [Required] string Token,
    [Required, MinLength(8)] string NewPassword);
```

- [ ] **Step 2: Write tests** (file uses `IClassFixture<BackendIntegrationFixture>`, reset DB each test like other auth tests)

```csharp
[Fact]
public async Task ForgotPassword_unknown_email_returns_generic_200_and_sends_nothing()
{
    using var response = await _fixture.Client.PostAsJsonAsync(
        "/api/auth/forgot-password",
        new ForgotPasswordRequest("nobody@example.com"));
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.True(_fixture.EmailSender.Sent.IsEmpty);
}

[Fact]
public async Task ForgotPassword_known_user_creates_token_and_sends_email()
{
    var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
        "Owner", $"owner-{Guid.NewGuid():N}@example.com", "Password123!"));
    _fixture.EmailSender.Sent.Clear(); // if queue API differs, drain

    using var response = await _fixture.Client.PostAsJsonAsync(
        "/api/auth/forgot-password",
        new ForgotPasswordRequest(owner.User.Email));
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.True(_fixture.EmailSender.Sent.TryPeek(out var mail));
    Assert.Contains(owner.User.Email, mail.To, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("/reset-password?token=", mail.Html);

    await using var db = /* open AppDbContext with fixture connection */;
    Assert.Equal(1, await db.PasswordResetTokens.CountAsync());
}

[Fact]
public async Task ResetPassword_valid_token_updates_password_and_revokes_refresh()
{
    // bootstrap → capture refresh token → forgot → parse token from email HTML
    // → reset with new password → login with new password succeeds
    // → refresh with old refresh token returns 401
}

[Fact]
public async Task ResetPassword_bad_token_returns_400()
{
    using var response = await _fixture.Client.PostAsJsonAsync(
        "/api/auth/reset-password",
        new ResetPasswordRequest("not-a-real-token", "Password123!"));
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task ResetPassword_used_token_returns_400()
{
    // forgot → reset once → reset again with same raw token → 400
}

[Fact]
public async Task ResetPassword_expired_token_returns_400()
{
    // forgot → set expires_at in past via DB → reset → 400
}
```

Helper to parse token: regex `token=([^&\s\"']+)` from HTML.

- [ ] **Step 3: Run tests — expect FAIL (404 or missing endpoints)**

```bash
dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~PasswordResetIntegrationTests
```

Expected: FAIL (endpoints not found or service missing).

- [ ] **Step 4: Commit (optional)**

```bash
git add backend/Models/AuthContracts.cs backend.Tests/IntegrationTests/PasswordResetIntegrationTests.cs
git commit -m "test: fail until forgot/reset password endpoints exist"
```

---

### Task 5: `PasswordResetService` + AuthController endpoints

**Files:**
- Create: `backend/Services/PasswordResetService.cs`
- Modify: `backend/Controllers/AuthController.cs`
- Modify: `backend/Program.cs` (register service)

- [ ] **Step 1: Implement service**

Core logic:

```csharp
public sealed class PasswordResetService
{
    // dependencies: AppDbContext, IPasswordHasher<AuthUser>, IEmailSender,
    // IOptions<AppOptions>, IOptions<AuthOptions>, ILogger<PasswordResetService>

    public async Task ForgotPasswordAsync(string email, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await _db.AuthUsers.FirstOrDefaultAsync(u => u.Email == normalized && u.IsActive, ct);
        if (user is null) return;

        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = HashToken(raw);

        var now = DateTimeOffset.UtcNow;
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            AuthUserId = user.Id,
            TokenHash = hash,
            ExpiresAt = now.AddHours(_auth.PasswordResetTokenHours),
            CreatedAt = now
        });
        await _db.SaveChangesAsync(ct);

        var link = $"{_app.FrontendBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(raw)}";
        var html = $"<p>Reset your VisionPaint password:</p><p><a href=\"{link}\">{link}</a></p><p>This link expires in {_auth.PasswordResetTokenHours} hour(s).</p>";
        var text = $"Reset your VisionPaint password: {link}";

        try
        {
            await _email.SendAsync(user.Email, "Reset your VisionPaint password", html, text, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", user.Email);
        }
    }

    public async Task<(bool Ok, string? Error)> ResetPasswordAsync(string rawToken, string newPassword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return (false, "Password must be at least 8 characters.");

        var hash = HashToken(rawToken);
        var token = await _db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.UsedAt is not null || token.ExpiresAt <= DateTimeOffset.UtcNow)
            return (false, "Invalid or expired reset link.");

        var user = await _db.AuthUsers.FirstOrDefaultAsync(u => u.Id == token.AuthUserId && u.IsActive, ct);
        if (user is null)
            return (false, "Invalid or expired reset link.");

        user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;
        token.UsedAt = now;

        var siblings = await _db.PasswordResetTokens
            .Where(t => t.AuthUserId == user.Id && t.UsedAt == null && t.Id != token.Id)
            .ToListAsync(ct);
        foreach (var s in siblings) s.UsedAt = now;

        var refresh = await _db.RefreshTokens
            .Where(t => t.AuthUserId == user.Id && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var t in refresh)
        {
            t.RevokedAt = now;
            t.RevokeReason = "password_reset";
        }

        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    private static string HashToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
```

- [ ] **Step 2: Controller endpoints**

```csharp
[HttpPost("forgot-password")]
[AllowAnonymous]
public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
{
    await _passwordReset.ForgotPasswordAsync(request.Email, cancellationToken);
    return Ok(new { message = "If an account exists for that email, we sent reset instructions." });
}

[HttpPost("reset-password")]
[AllowAnonymous]
public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
{
    var (ok, error) = await _passwordReset.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken);
    if (!ok) return BadRequest(new { message = error });
    return Ok(new { message = "Password updated. You can sign in." });
}
```

Inject `PasswordResetService` into `AuthController`.

- [ ] **Step 3: Run PasswordResetIntegrationTests**

```bash
dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~PasswordResetIntegrationTests
```

Expected: all PASS.

- [ ] **Step 4: Commit (optional)**

```bash
git add backend/Services/PasswordResetService.cs backend/Controllers/AuthController.cs backend/Program.cs
git commit -m "Implement forgot and reset password with session revoke"
```

---

### Task 6: Frontend guest routes and views

**Files:**
- Create: `frontend/src/views/ForgotPasswordView.vue`
- Create: `frontend/src/views/ResetPasswordView.vue`
- Modify: `frontend/src/views/LoginView.vue`
- Modify: `frontend/src/router/index.ts`
- Modify: `frontend/src/stores/auth.ts` (add `forgotPassword` / `resetPassword` helpers using `request`)

- [ ] **Step 1: Auth store helpers**

```typescript
async function forgotPassword(email: string) {
  await request("/auth/forgot-password", {
    method: "POST",
    body: JSON.stringify({ email }),
  });
}

async function resetPassword(token: string, newPassword: string) {
  await request("/auth/reset-password", {
    method: "POST",
    body: JSON.stringify({ token, newPassword }),
  });
}
```

Export both.

- [ ] **Step 2: Router — add sibling guest routes** (same `GuestLayout` pattern as login)

```typescript
{
  path: "/forgot-password",
  component: () => import("@/layouts/GuestLayout.vue"),
  meta: { guest: true },
  children: [{ path: "", name: "forgot-password", component: () => import("@/views/ForgotPasswordView.vue") }],
},
{
  path: "/reset-password",
  component: () => import("@/layouts/GuestLayout.vue"),
  meta: { guest: true },
  children: [{ path: "", name: "reset-password", component: () => import("@/views/ResetPasswordView.vue") }],
},
```

- [ ] **Step 3: `ForgotPasswordView.vue`**

- Email field + submit
- On success always show: “If an account exists for that email, we sent reset instructions.”
- Link back to login
- Match LoginView styling (VisionPaint eyebrow, card, VpInput/VpButton)

- [ ] **Step 4: `ResetPasswordView.vue`**

- Read `route.query.token` (string); if missing, show error
- New password + confirm; min 8; must match
- On success: `router.replace({ name: "login", query: { reset: "1" } })`

- [ ] **Step 5: `LoginView.vue`**

- Under the form (login mode only): `RouterLink` to `/forgot-password` — “Forgot password?”
- If `route.query.reset === "1"`, set `message` to “Password updated. Sign in with your new password.”

- [ ] **Step 6: Build**

```bash
cd frontend
npm run build
```

Expected: PASS.

- [ ] **Step 7: Commit (optional)**

```bash
git add frontend/src/views/ForgotPasswordView.vue frontend/src/views/ResetPasswordView.vue frontend/src/views/LoginView.vue frontend/src/router/index.ts frontend/src/stores/auth.ts
git commit -m "Add forgot and reset password guest screens"
```

---

### Task 7: Docs + verification

**Files:**
- Modify: `docs/design/ui-spec.md` (SCR-001 + new screens)
- Modify: `docs/design/screen-map.md`
- Modify: `docs/backlog/feature-ideas.md` (mark forgot-password in progress/done when shipped)

- [ ] **Step 1: Update ui-spec / screen-map**

- SCR-001: Forgot password link
- New SCR (e.g. SCR-001a Forgot password `/forgot-password`, SCR-001b Reset `/reset-password`)
- APIs: `POST /api/auth/forgot-password`, `POST /api/auth/reset-password`

- [ ] **Step 2: Full verification**

```bash
dotnet test backend.Tests/backend.Tests.csproj --filter "FullyQualifiedName~PasswordResetIntegrationTests|FullyQualifiedName~AuthIntegrationTests"
cd frontend
npm run build
```

Expected: PASS.

- [ ] **Step 3: Manual Brevo smoke (local)**

1. Set `VISIONPAINT_BREVO_API_KEY`, `Brevo:SenderEmail` (verified), `App:FrontendBaseUrl=http://localhost:5173`
2. Run API + Vite; request reset for a real inbox you control
3. Open link; set new password; confirm old refresh fails and new login works

- [ ] **Step 4: Commit docs (optional)**

```bash
git add docs/design/ui-spec.md docs/design/screen-map.md docs/backlog/feature-ideas.md
git commit -m "Document forgot-password screens and APIs"
```

---

## Spec coverage

| Spec item | Task |
|-----------|------|
| `password_reset_token` table | 1 |
| Brevo + `IEmailSender` | 2 |
| CI recording sender | 3 |
| Forgot/reset APIs + hash tokens + revoke all refresh | 5 |
| Generic forgot response | 5 |
| Frontend routes/UI | 6 |
| Acceptance scenarios 1–6 | 4, 5, 7 |
| Manual Brevo smoke | 7 |
| Signed-in change-password out of scope | — |

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-11-forgot-password.md`.

**1. Subagent-Driven (recommended)** — fresh subagent per task  
**2. Inline Execution** — this session with executing-plans  

Which approach?
