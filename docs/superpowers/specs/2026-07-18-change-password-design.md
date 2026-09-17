# In-App Change Password Design

**Status:** Approved for implementation planning (2026-07-18)  
**Depends on:** JWT auth, refresh-token sessions  
**Related:** [Forgot password](2026-07-11-forgot-password-design.md) (unsigned reset via email)

## Problem

Users who receive a temporary password from a manager (or who simply want to rotate credentials) need a signed-in way to set a new password without using the forgot-password email flow.

## Goal

Any authenticated user can change their password from Account by proving they know the current password. Other devices are signed out; the current device stays signed in.

## Decisions

| Decision | Choice |
|----------|--------|
| Approach | Single authenticated endpoint + Account form |
| Session policy | Keep current session; revoke all other refresh sessions |
| Email | Out of scope (follow-up feature) |
| Token response | Do not re-issue tokens on success |
| Password rules | Same as create-user / reset: minimum 8 characters |

## API

### `POST /api/auth/change-password` `[Authorize]`

Request:

```json
{
  "currentPassword": "…",
  "newPassword": "…"
}
```

Responses:

| Status | When |
|--------|------|
| `200` | Password updated |
| `401` | Missing/invalid access token |
| `400` | Wrong current password, new password length &lt; 8, or new equals current |

Behavior:

1. Resolve `auth_user` from JWT subject (`ClaimTypes.NameIdentifier`)
2. Verify `currentPassword` with `IPasswordHasher<AuthUser>`
3. Reject if `newPassword` fails policy or equals current
4. Set `password_hash`, `updated_at`
5. Revoke open `refresh_token` rows for that user where `session_id` ≠ current session claim (`TokenService.SessionIdClaimType`), reason `password_change`
6. Leave the current session’s refresh tokens intact

## Frontend

**SCR-013 Account** (`frontend/src/views/AccountView.vue`):

- Add **Change password** section above Sign out
- Fields: current password, new password, confirm (toggle visibility like login)
- Client validation: required fields, new ≥ 8, new === confirm
- Call auth store helper → `POST /api/auth/change-password`
- On success: clear fields; show short success message; stay on page
- On error: show API message inline

No new route.

## Out of scope

- Change email
- Manager-initiated password set without current password
- Brevo / email confirmation of password change
- Audit log / “reason for change”

## Acceptance tests

| # | Scenario | Expected |
|---|----------|----------|
| 1 | Valid change | 200; login with new password works; other session refresh → 401; current session refresh still works |
| 2 | Wrong current password | 400; hash unchanged |
| 3 | New password too short | 400 |
| 4 | No bearer token | 401 |

## References

- Logout session claim pattern: `AuthController.Logout`
- Password hashing: existing `IPasswordHasher<AuthUser>`
- Account UI: `docs/design/ui-spec.md` SCR-013
- Backlog: `docs/backlog/feature-ideas.md` (Auth)
