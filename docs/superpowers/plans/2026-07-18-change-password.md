# In-App Change Password Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Note:** Skip `git commit` steps if the human asked not to commit.

**Goal:** Let signed-in users change their password from Account while staying logged in on the current device and signing out other sessions.

**Architecture:** Add `POST /api/auth/change-password` that verifies the current password, updates the hash, and revokes all refresh tokens for the user except those matching the access token’s `session_id` claim. Account page gains a change-password form wired through the auth store.

**Tech Stack:** ASP.NET Core, EF Core, JWT, Vue 3, Pinia, xUnit

**Spec:** [docs/superpowers/specs/2026-07-18-change-password-design.md](../specs/2026-07-18-change-password-design.md)

---

## File map

| File | Responsibility |
|------|----------------|
| `backend/Models/AuthContracts.cs` | `ChangePasswordRequest` |
| `backend/Controllers/AuthController.cs` | Endpoint + revoke-other-sessions helper |
| `backend.Tests/IntegrationTests/ChangePasswordIntegrationTests.cs` | Acceptance tests |
| `frontend/src/stores/auth.ts` | `changePassword` helper |
| `frontend/src/views/AccountView.vue` | Form UI |
| `docs/design/ui-spec.md` | SCR-013 update |
| `docs/backlog/feature-ideas.md` | Mark done when shipped |

---

### Task 1: Request DTO + failing integration tests

**Files:**
- Modify: `backend/Models/AuthContracts.cs`
- Create: `backend.Tests/IntegrationTests/ChangePasswordIntegrationTests.cs`

- [ ] **Step 1: Add DTO**

```csharp
public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(8)] string NewPassword);
```

- [ ] **Step 2: Write integration tests**

Use `IClassFixture<BackendIntegrationFixture>` + DB reset like other auth tests.

```csharp
[Fact]
public async Task ChangePassword_updates_hash_keeps_current_session_revokes_others()
{
    var email = $"owner-{Guid.NewGuid():N}@example.com";
    var sessionA = await _fixture.AuthClient.BootstrapAsync(
        new BootstrapRequest("Owner", email, "Password123!"));

    // Second login = other session
    var sessionB = await _fixture.AuthClient.LoginAsync(new LoginRequest(email, "Password123!"));

    _fixture.AuthClient.SetBearerToken(sessionA.AccessToken);
    using var response = await _fixture.Client.PostAsJsonAsync(
        "/api/auth/change-password",
        new ChangePasswordRequest("Password123!", "NewPassword123!"));
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    // New password works
    var login = await _fixture.AuthClient.LoginAsync(new LoginRequest(email, "NewPassword123!"));
    Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));

    // Other session refresh revoked
    using var refreshB = await _fixture.Client.PostAsJsonAsync(
        "/api/auth/refresh",
        new RefreshTokenRequest(sessionB.RefreshToken));
    Assert.Equal(HttpStatusCode.Unauthorized, refreshB.StatusCode);

    // Current session refresh still works
    using var refreshA = await _fixture.Client.PostAsJsonAsync(
        "/api/auth/refresh",
        new RefreshTokenRequest(sessionA.RefreshToken));
    Assert.Equal(HttpStatusCode.OK, refreshA.StatusCode);
}

[Fact]
public async Task ChangePassword_wrong_current_returns_400()
{
    var email = $"owner-{Guid.NewGuid():N}@example.com";
    var tokens = await _fixture.AuthClient.BootstrapAsync(
        new BootstrapRequest("Owner", email, "Password123!"));
    _fixture.AuthClient.SetBearerToken(tokens.AccessToken);

    using var response = await _fixture.Client.PostAsJsonAsync(
        "/api/auth/change-password",
        new ChangePasswordRequest("WrongPassword!", "NewPassword123!"));
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task ChangePassword_short_new_password_returns_400()
{
    var email = $"owner-{Guid.NewGuid():N}@example.com";
    var tokens = await _fixture.AuthClient.BootstrapAsync(
        new BootstrapRequest("Owner", email, "Password123!"));
    _fixture.AuthClient.SetBearerToken(tokens.AccessToken);

    using var response = await _fixture.Client.PostAsJsonAsync(
        "/api/auth/change-password",
        new ChangePasswordRequest("Password123!", "short"));
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task ChangePassword_unauthenticated_returns_401()
{
    _fixture.AuthClient.SetBearerToken(null);
    using var response = await _fixture.Client.PostAsJsonAsync(
        "/api/auth/change-password",
        new ChangePasswordRequest("Password123!", "NewPassword123!"));
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}
```

Note: bootstrap then login creates two distinct `session_id` values (each `CreateTokenResponseAsync` uses `Guid.NewGuid()` for session). Confirm refresh-token rows: after bootstrap there is one session; second login adds another.

- [ ] **Step 3: Run tests — expect FAIL (404 / missing endpoint)**

```bash
dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~ChangePasswordIntegrationTests
```

Expected: FAIL until Task 2.

- [ ] **Step 4: Commit (optional)**

```bash
git add backend/Models/AuthContracts.cs backend.Tests/IntegrationTests/ChangePasswordIntegrationTests.cs
git commit -m "test: fail until change-password endpoint exists"
```

---

### Task 2: Implement `POST /api/auth/change-password`

**Files:**
- Modify: `backend/Controllers/AuthController.cs`

- [ ] **Step 1: Add helper to revoke other sessions**

Near `RevokeRefreshTokenSessionAsync`:

```csharp
private async Task RevokeOtherRefreshTokenSessionsAsync(
    Guid authUserId,
    Guid keepSessionId,
    string reason,
    CancellationToken cancellationToken)
{
    var tokens = await _db.RefreshTokens
        .Where(token =>
            token.AuthUserId == authUserId
            && token.SessionId != keepSessionId
            && token.RevokedAt == null)
        .ToListAsync(cancellationToken);

    if (tokens.Count == 0)
    {
        return;
    }

    var now = DateTimeOffset.UtcNow;
    foreach (var token in tokens)
    {
        token.RevokedAt = now;
        token.RevokeReason = reason;
    }

    await _db.SaveChangesAsync(cancellationToken);
}
```

- [ ] **Step 2: Add endpoint**

```csharp
[HttpPost("change-password")]
[Authorize]
public async Task<IActionResult> ChangePassword(
    [FromBody] ChangePasswordRequest request,
    CancellationToken cancellationToken)
{
    var authUserIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
    var sessionIdText = User.FindFirstValue(TokenService.SessionIdClaimType);
    if (!Guid.TryParse(authUserIdText, out var authUserId)
        || !Guid.TryParse(sessionIdText, out var sessionId))
    {
        return Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
    {
        return BadRequest(new { message = "Password must be at least 8 characters." });
    }

    var authUser = await _db.AuthUsers.FirstOrDefaultAsync(
        user => user.Id == authUserId && user.IsActive,
        cancellationToken);
    if (authUser is null)
    {
        return Unauthorized();
    }

    var verification = _passwordHasher.VerifyHashedPassword(
        authUser,
        authUser.PasswordHash,
        request.CurrentPassword);
    if (verification == PasswordVerificationResult.Failed)
    {
        return BadRequest(new { message = "Current password is incorrect." });
    }

    if (request.CurrentPassword == request.NewPassword)
    {
        return BadRequest(new { message = "New password must be different from the current password." });
    }

    authUser.PasswordHash = _passwordHasher.HashPassword(authUser, request.NewPassword);
    authUser.UpdatedAt = DateTimeOffset.UtcNow;
    await _db.SaveChangesAsync(cancellationToken);

    await RevokeOtherRefreshTokenSessionsAsync(
        authUserId,
        sessionId,
        "password_change",
        cancellationToken);

    return Ok(new { message = "Password updated." });
}
```

Place after `logout` (or near other password endpoints).

- [ ] **Step 3: Run tests**

```bash
dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~ChangePasswordIntegrationTests
```

Expected: all PASS.

If session-keep assertion fails: inspect whether access JWT includes `session_id` (logout already relies on it — should be fine).

- [ ] **Step 4: Commit (optional)**

```bash
git add backend/Controllers/AuthController.cs
git commit -m "Add authenticated change-password endpoint"
```

---

### Task 3: Frontend store + Account UI

**Files:**
- Modify: `frontend/src/stores/auth.ts`
- Modify: `frontend/src/views/AccountView.vue`

- [ ] **Step 1: Auth store helper**

```typescript
async function changePassword(currentPassword: string, newPassword: string) {
  await request<{ message: string }>("/auth/change-password", {
    method: "POST",
    body: JSON.stringify({ currentPassword, newPassword }),
  });
}
```

Export `changePassword` from the store return object.

- [ ] **Step 2: Account form**

In `AccountView.vue`:

- Add refs: `currentPassword`, `newPassword`, `confirmPassword`, `busy`, `error`, `success`
- Section title “Change password”
- Three `VpInput` fields with `type="password"` and `show-password-toggle`
- Submit button; client validation (required, length ≥ 8, new === confirm)
- On success: clear fields; `success = "Password updated."`
- Place section above Sign out, below role / manage-users link

Match existing Account card styling (muted labels, spacing).

- [ ] **Step 3: Build**

```bash
cd frontend
npm run build
```

Expected: PASS.

- [ ] **Step 4: Commit (optional)**

```bash
git add frontend/src/stores/auth.ts frontend/src/views/AccountView.vue
git commit -m "Add change password form on Account"
```

---

### Task 4: Docs + verification

**Files:**
- Modify: `docs/design/ui-spec.md` (SCR-013)
- Modify: `docs/backlog/feature-ideas.md`

- [ ] **Step 1: Update SCR-013**

Document change-password fields, `POST /api/auth/change-password`, session policy (keep current / revoke others).

- [ ] **Step 2: Mark backlog item done**

Strikethrough change-password in Auth section; leave change-email open.

- [ ] **Step 3: Full verification**

```bash
dotnet test backend.Tests/backend.Tests.csproj --filter "FullyQualifiedName~ChangePasswordIntegrationTests|FullyQualifiedName~AuthIntegrationTests|FullyQualifiedName~PasswordResetIntegrationTests"
cd frontend
npm run build
```

Expected: PASS.

Manual: sign in on two browsers → change password in one → other loses refresh; first stays signed in; login with new password works.

- [ ] **Step 4: Commit docs (optional)**

```bash
git add docs/design/ui-spec.md docs/backlog/feature-ideas.md
git commit -m "Document in-app change password on Account"
```

---

## Spec coverage

| Spec item | Task |
|-----------|------|
| `POST /api/auth/change-password` | 2 |
| Verify current; min 8; reject same password | 2 |
| Keep current session; revoke others | 1, 2 |
| Account form | 3 |
| Acceptance scenarios 1–4 | 1, 2, 4 |
| Change email out of scope | — |

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-18-change-password.md`.

**1. Subagent-Driven (recommended)** — fresh subagent per task  
**2. Inline Execution** — this session  

Which approach?
