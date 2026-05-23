# CI Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split backend CI, UI E2E CI, and backend deploy responsibilities so backend integration tests gate deployment while Playwright provides a separate PR quality signal.

**Architecture:** Add one dedicated backend CI workflow and one dedicated Playwright workflow alongside the existing Azure deployment workflow. Both test workflows will use a PostgreSQL service container and a shared secret-backed admin password, while deployment remains focused on publish and release.

**Tech Stack:** GitHub Actions, .NET 10, PostgreSQL 17 service containers, Playwright, Azure Web Apps, Vite/React.

---

### Task 1: Add backend CI workflow

**Files:**
- Create: `.github/workflows/backend-ci.yml`
- Reference: `.github/workflows/main_vision-paint-api.yml`
- Reference: `backend/README.md`

- [ ] **Step 1: Write the failing workflow file skeleton**

Create `.github/workflows/backend-ci.yml` with:

```yaml
name: Backend CI

on:
  pull_request:
  push:
    branches:
      - main
  workflow_dispatch:

jobs:
  backend-test:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:17
        env:
          POSTGRES_PASSWORD: ${{ secrets.CI_POSTGRES_PASSWORD }}
        ports:
          - 5432:5432
        options: >-
          --health-cmd="pg_isready -U postgres"
          --health-interval=10s
          --health-timeout=5s
          --health-retries=5
    steps:
      - uses: actions/checkout@v4
```

- [ ] **Step 2: Add .NET setup and restore/build steps**

Extend the workflow with:

```yaml
      - name: Set up .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.x"

      - name: Restore
        run: dotnet restore VisionPaint.sln

      - name: Build
        run: dotnet build VisionPaint.sln --configuration Release --no-restore
```

- [ ] **Step 3: Add backend integration test step**

Add:

```yaml
      - name: Run backend integration tests
        run: dotnet test backend.Tests/backend.Tests.csproj --configuration Release --no-build
        env:
          VISIONPAINT_TEST_PGADMIN: Host=localhost;Port=5432;Username=postgres;Password=${{ secrets.CI_POSTGRES_PASSWORD }};Database=postgres
```

- [ ] **Step 4: Review workflow for repo fit**

Check:
- workflow name is distinct from existing files
- solution path is `VisionPaint.sln`
- test project path is `backend.Tests/backend.Tests.csproj`
- secret name is consistent with the design doc

Expected: no path or naming mismatches remain.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/backend-ci.yml
git commit -m "ci: add backend integration workflow"
```

### Task 2: Add UI E2E workflow scaffold

**Files:**
- Create: `.github/workflows/e2e-ui.yml`
- Reference: `.github/workflows/firebase-hosting-pull-request.yml`
- Reference: `frontend/package.json`
- Reference: `backend/README.md`

- [ ] **Step 1: Write the workflow shell with Postgres service**

Create `.github/workflows/e2e-ui.yml` with:

```yaml
name: UI E2E

on:
  pull_request:
  push:
    branches:
      - main
  workflow_dispatch:

jobs:
  playwright:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:17
        env:
          POSTGRES_PASSWORD: ${{ secrets.CI_POSTGRES_PASSWORD }}
        ports:
          - 5432:5432
        options: >-
          --health-cmd="pg_isready -U postgres"
          --health-interval=10s
          --health-timeout=5s
          --health-retries=5
    steps:
      - uses: actions/checkout@v4
```

- [ ] **Step 2: Add runtime setup placeholders for backend and frontend**

Add:

```yaml
      - name: Set up .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.x"

      - name: Set up Node
        uses: actions/setup-node@v4
        with:
          node-version: "22"

      - name: Restore backend
        run: dotnet restore VisionPaint.sln

      - name: Install frontend dependencies
        run: npm ci
        working-directory: ./frontend
```

- [ ] **Step 3: Add explicit TODO-safe scaffolding for future app startup**

Add the following disabled placeholder comments in YAML rather than fake steps:

```yaml
      # Future steps:
      # - apply backend startup configuration for CI Postgres
      # - start backend server
      # - start frontend server or preview server
      # - install Playwright browsers
      # - run Playwright tests
```

This step is complete when the workflow clearly shows intended structure without pretending E2E is implemented.

- [ ] **Step 4: Review trigger and responsibility boundaries**

Check:
- workflow is separate from backend CI
- workflow does not claim to block deploy yet
- Postgres secret naming matches backend CI

Expected: the workflow is intentionally scaffolded for later Playwright implementation, not half-finished.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/e2e-ui.yml
git commit -m "ci: scaffold ui e2e workflow"
```

### Task 3: Adjust backend deploy workflow boundaries

**Files:**
- Modify: `.github/workflows/main_vision-paint-api.yml`
- Reference: `.github/workflows/backend-ci.yml`

- [ ] **Step 1: Review the existing deployment workflow**

Check the current file for:
- trigger conditions
- build/publish/deploy responsibilities
- any embedded test logic

Expected: deploy workflow is publish/deploy focused and currently does not run tests.

- [ ] **Step 2: Make only minimal clarifying edits if needed**

If helpful, update the workflow name or comments so its scope is explicit, for example:

```yaml
name: Build and deploy ASP.Net Core app to Azure Web App - vision-paint-api
```

and keep the job responsibilities limited to build, publish, artifact upload, and deploy.

Do not add Playwright or backend integration test execution here in this task.

- [ ] **Step 3: Verify gating strategy is branch-protection friendly**

Check that the workflow can rely on required checks from:
- `Backend CI`
- `UI E2E`

Expected: no deploy-specific YAML coupling is required for the initial policy.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/main_vision-paint-api.yml
git commit -m "ci: clarify backend deploy workflow scope"
```

### Task 4: Document CI prerequisites

**Files:**
- Modify: `backend/README.md`
- Modify: `docs/superpowers/specs/2026-05-16-ci-workflow-design.md`

- [ ] **Step 1: Add backend CI note to the backend README**

Append or update README text so it includes:

```md
## CI test prerequisites

GitHub Actions backend integration tests expect:

- a PostgreSQL 17 service container
- a `CI_POSTGRES_PASSWORD` GitHub secret
- `VISIONPAINT_TEST_PGADMIN` set from that secret
```

- [ ] **Step 2: Confirm the spec doc matches final secret naming**

Review the spec and ensure it references:

```text
CI_POSTGRES_PASSWORD
```

Expected: spec and README use the same secret name.

- [ ] **Step 3: Commit**

```bash
git add backend/README.md docs/superpowers/specs/2026-05-16-ci-workflow-design.md
git commit -m "docs: add ci workflow prerequisites"
```

### Task 5: Verify workflow syntax and repo alignment

**Files:**
- Verify: `.github/workflows/backend-ci.yml`
- Verify: `.github/workflows/e2e-ui.yml`
- Verify: `.github/workflows/main_vision-paint-api.yml`
- Verify: `backend/README.md`

- [ ] **Step 1: Run a local file review for workflow consistency**

Review all three workflow files and confirm:
- valid YAML indentation
- unique workflow names
- consistent secret naming
- correct repo paths

Expected: no obvious syntax or path issues.

- [ ] **Step 2: Run backend tests locally again**

Run:

```powershell
$env:VISIONPAINT_TEST_PGADMIN='Host=127.0.0.1;Port=5432;Username=postgres;Password=<local-password>;Database=postgres'
dotnet test backend.Tests/backend.Tests.csproj --no-restore
```

Expected: PASS

- [ ] **Step 3: Review README instructions against actual commands**

Check that the README commands match:
- local backend test execution
- CI secret names

Expected: docs match reality and do not mix local and CI variables incorrectly.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/backend-ci.yml .github/workflows/e2e-ui.yml .github/workflows/main_vision-paint-api.yml backend/README.md
git commit -m "ci: finalize workflow split"
```
