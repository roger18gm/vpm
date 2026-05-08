# VisionPaint Auth Design

## Goal

Add authentication and authorization to VisionPaint using ASP.NET Identity and cookie-based sessions, while keeping the browser out of the database and keeping the backend as the only trust boundary.

## Scope

In scope:

- user sign-in and sign-out
- session persistence with HTTP-only cookies
- mapping authenticated users to `person`
- company membership and role-based authorization
- backend guards on existing and future API endpoints
- first-pass support for internal staff only

Out of scope:

- Supabase Auth
- direct frontend-to-database access
- RLS policies for the first auth pass
- client portal / external customer login

## Design

Use ASP.NET Identity for account management and session issuance. The backend will authenticate the browser with an HTTP-only, secure cookie. The React app will call the API with credentials included, and the backend will resolve the signed-in user from the cookie on each request.

The domain model stays separate from the login model:

- `AspNetUsers` holds login identity
- `person` holds the business-facing human record
- `company_member` defines access to a company and a role within it

This keeps identity and business authorization distinct. A person can later become a client contact without creating a second human record, because `client_contact` can reference the same `person`.

## Authorization Rules

Authorization happens in the backend.

- `owner` and `admin` can manage company settings and membership
- `manager` can create and update jobs
- `crew` can view assigned company data and update only the workflows we explicitly allow later

The first implementation will protect the jobs API and the account/member lookup paths. Additional endpoints will follow the same pattern.

## Cookie Session Flow

1. User submits username/password to the backend.
2. ASP.NET Identity validates the credentials.
3. Backend issues an HTTP-only session cookie.
4. React sends requests with `credentials: include`.
5. Backend uses the cookie to identify the current user.
6. Backend loads the matching `person` and checks `company_member` before allowing access.

## Cross-Site Note

Because the frontend and backend are hosted separately, the auth cookie must be configured for cross-site requests.

Expected requirements:

- `Secure` cookie
- `HttpOnly` cookie
- `SameSite=None`
- CORS with credentials enabled
- frontend `fetch` calls include credentials

This setup should be tested in Chrome and Safari. Safari is the browser most likely to expose third-party cookie behavior issues if the deployment shape changes.

## Data Model Additions

Likely backend auth tables or mappings:

- `AspNetUsers` and related ASP.NET Identity tables
- `person.auth_user_id` to link the login user to the business record
- `company_member` to store roles and active membership

The existing `company`, `person`, `client`, and `job` schema stays as the business model.

## Error Handling

The backend should return clear 401 and 403 responses:

- 401 when a session is missing or expired
- 403 when a user is logged in but lacks the required company role
- 404 when a requested record does not belong to the current company or should not be visible

The frontend should treat 401 as a sign-out or re-login case and 403 as an access denied state.

## Testing

Verify the auth layer with a small set of checks:

- sign in creates a valid session cookie
- sign out clears the session
- authenticated requests can read allowed jobs
- unauthenticated requests are rejected
- authenticated but unauthorized users are rejected
- session survives a page refresh
- cookie-based requests work from the deployed frontend origin

Also verify the mapping between an authenticated user and `person`, since that is the bridge between login identity and business permissions.

## Recommendation

Proceed with ASP.NET Identity and cookie sessions. It fits the current architecture better than a custom JWT flow and keeps the browser session story simple.
