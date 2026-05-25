# Branch protection for `main`

Pull requests into `main` must pass both CI workflows before merge:

| Check name | Workflow | Job |
|------------|----------|-----|
| `Backend CI / backend-test` | [backend-ci.yml](../../.github/workflows/backend-ci.yml) | `backend-test` |
| `UI E2E / playwright` | [e2e-ui.yml](../../.github/workflows/e2e-ui.yml) | `playwright` |

The Azure deploy workflow ([main_vision-paint-api.yml](../../.github/workflows/main_vision-paint-api.yml)) is **not** a required PR check. It runs on push to `main` after merge.

## Option A — GitHub CLI (recommended)

1. Install GitHub CLI (if needed):

   ```powershell
   winget install GitHub.cli
   ```

   Restart the terminal so `gh` is on your PATH.

2. Log in (one time):

   ```powershell
   gh auth login
   ```

   Use **GitHub.com**, **HTTPS**, and sign in in the browser. Your account needs **admin** on `roger18gm/vpm`.

3. Apply protection:

   ```powershell
   cd C:\Users\Roger\Desktop\VisionPaint
   .\scripts\configure-main-branch-protection.ps1
   ```

4. Confirm in the repo: **Settings → Branches → Branch protection rules** (or **Rulesets**) for `main`.

## Option B — GitHub website

1. Open [Branches settings](https://github.com/roger18gm/vpm/settings/branches).
2. Add or edit a rule for **`main`**.
3. Enable **Require a pull request before merging** (0 approvals is fine for solo work).
4. Enable **Require status checks to pass before merging**.
5. Enable **Require branches to be up to date before merging** (strict).
6. Search and select these checks (names must match a recent green run on `main` or a PR):
   - `Backend CI / backend-test`
   - `UI E2E / playwright`
7. Save.

If a check does not appear in the search box, merge or push a commit that runs both workflows successfully first.

## Verify

Open a test PR or run:

```powershell
gh pr checks
```

Both checks should be required and must pass before the merge button is enabled.
