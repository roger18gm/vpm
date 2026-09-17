# Forgot Password (Email Reset) Design

**Status:** Approved for implementation planning (2026-07-11)  
**Scope slice:** Forgot password from login only (signed-in change-password is a later backlog item)

## Problem

Users who forget their password (or receive a temporary password from a manager) have no self-serve way to set a new one. VisionPaint uses custom `AuthUser` + JWT refresh tokens and has no password-reset or email-sending path today.

## Goal

Allow an unauthenticated user to request a reset email, open a one-time link, set a new password, and be forced to sign in again on all devices.

## Decisions

| Decision | Choice |
|----------|--------|
| Provider | Brevo transactional API |
| Abstraction | `IEmailSender` with `BrevoEmailSender` implementation |
| Dev / Production | Use Brevo in all app environments when configured (no LoggingEmailSender for normal local runs) |
| CI / automated tests | Recording `IEmailSender` test double — no live Brevo network dependency |
| Token storage | Database table; store **hash** only; raw token only in email link |
| Token lifetime | 1 hour (configurable) |
| After successful reset | Update password hash; mark token used; revoke **all** refresh tokens for that user |
| Email enumeration | Always return the same generic success from forgot-password |
| Out of scope | Signed-in change-password; manager-initiated reset; email verification on create user |

## Data model

### `password_reset_token`

| Column | Notes |
|--------|--------|
| `id` | uuid PK |
| `auth_user_id` | FK → `auth_user` |
| `token_hash` | hash of the raw token (never store raw) |
| `expires_at` | |
| `used_at` | null until consumed |
| `created_at` | |

Index on `token_hash` for lookup. On successful reset, mark this token used and invalidate other unused tokens for the same user (set `used_at` or delete).

## API

### `POST /api/auth/forgot-password`

Request: `{ "email": "…" }`  
Response: always `200` with a generic message (e.g. “If an account exists for that email, we sent reset instructions.”).

Behavior when a matching **active** `auth_user` exists:

1. Generate a high-entropy raw token
2. Insert `password_reset_token` with hashed token and `expires_at = now + PasswordResetTokenHours`
3. Email link: `{FrontendBaseUrl}/reset-password?token={rawToken}` via `IEmailSender`
4. If Brevo/send fails: log error; still return generic 200

### `POST /api/auth/reset-password`

Request: `{ "token": "…", "newPassword": "…" }`  
Success: `200`  
Failure: `400` for invalid, expired, or already-used token, or password that fails policy (same minimum as create-user: length ≥ 8)

On success:

1. Verify token hash + not expired + `used_at` is null
2. Set `auth_user.password_hash` via existing `IPasswordHasher<AuthUser>`
3. Set `used_at` on the token; invalidate other unused tokens for that user
4. Revoke all `refresh_token` rows for that `auth_user_id` (same notion of revoke as logout)

## Email layer

```csharp
public interface IEmailSender
{
    Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default);
}
```

- **`BrevoEmailSender`:** HTTP call to Brevo transactional send API
- **Config (secrets):**
  - `Brevo:ApiKey` / `VISIONPAINT_BREVO_API_KEY`
  - `Brevo:SenderEmail`, `Brevo:SenderName` (verified in Brevo)
  - `App:FrontendBaseUrl`
  - `Auth:PasswordResetTokenHours` (default `1`)
- **Message:** subject “Reset your VisionPaint password”; short HTML + text with link; note that the link expires in one hour

## Frontend

| Route | Purpose |
|-------|---------|
| `/login` | Add “Forgot password?” → `/forgot-password` |
| `/forgot-password` | Email form; always show generic confirmation after submit |
| `/reset-password` | Read `token` query; new password + confirm; on success → `/login` with success banner |

Guest layout only. Client validation: password ≥ 8 characters; passwords must match.

## Security notes

- Never put the raw token in logs or API responses (except the email body via Brevo)
- Constant-time compare of token hashes where practical
- Rate limiting can be a follow-up; not required for v1 of this slice
- HTTPS required in deployed environments (already project requirement M11)

## Acceptance tests

| # | Scenario | Expected |
|---|----------|----------|
| 1 | Forgot-password, unknown email | 200 generic; no token row |
| 2 | Forgot-password, known active user | 200; token row; test double receives email with raw token in link |
| 3 | Reset with valid token | New password works; token used; all refresh tokens revoked |
| 4 | Reset with expired / used / bad token | 400; password unchanged |
| 5 | Before reset completes, old password still works | Login succeeds |
| 6 | After reset, old refresh token | Refresh fails (401) |

Manual smoke: real Brevo + verified sender + inbox receives link.

## References

- JWT auth: `docs/superpowers/specs/2026-05-17-jwt-auth.md`
- `auth_user` / `refresh_token`: `database/schema.md`
- Login UI: SCR-001 in `docs/design/ui-spec.md`
- Create-user password min length: `frontend/src/components/user/CreateUserModal.vue` / `UsersController`
