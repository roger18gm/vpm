# CI Workflow Design

**Date:** 2026-05-16

**Goal:** Define a GitHub Actions structure that supports backend integration tests, future Playwright end-to-end tests, and safe backend deployment without conflating those responsibilities.

## Context

The repo currently has separate Firebase hosting workflows and one backend Azure deployment workflow in `/.github/workflows/main_vision-paint-api.yml`. Backend integration tests now exist in a separate `backend.Tests` project and require a PostgreSQL admin connection string through `VISIONPAINT_TEST_PGADMIN`.

Future Playwright tests will also need a disposable PostgreSQL instance plus app startup orchestration, but they should not have the same deployment-gating role as the backend integration suite on day one.

## Requirements

- Backend integration tests must run in CI before backend deployment.
- Playwright end-to-end tests should run in CI as a required PR quality signal.
- Backend deploys should be blocked by backend CI, but not initially blocked by Playwright.
- PostgreSQL in CI should be provisioned with a disposable service container.
- The PostgreSQL password should be stored in GitHub Secrets even though the database is CI-only.
- Test data should stay minimal and deterministic.

## Recommended Workflow Structure

Use three workflows with clear responsibilities:

### 1. Backend CI

Create a dedicated backend workflow, for example `.github/workflows/backend-ci.yml`.

Responsibilities:
- check out the repo
- install .NET
- build the solution
- start a PostgreSQL service container
- set `VISIONPAINT_TEST_PGADMIN`
- run `backend.Tests`

Triggers:
- `pull_request`
- `push` to `main`
- optional `workflow_dispatch`

Gate behavior:
- required status check on `main` (`Backend CI / backend-test`)
- required before backend deploy (via branch protection on `main`)

### 2. UI E2E

Create a dedicated E2E workflow, for example `.github/workflows/e2e-ui.yml`.

Responsibilities:
- check out the repo
- start a PostgreSQL service container
- install backend and frontend dependencies
- start backend against the CI Postgres instance
- start the frontend app or preview server
- run Playwright

Triggers:
- `pull_request`
- `push` to `main`
- optional `workflow_dispatch`

Gate behavior:
- required status check on `main` (`UI E2E / playwright`)
- not a separate deploy workflow gate (deploy still relies on backend CI passing on `main`)

### 3. Backend Deploy

Keep the Azure deploy workflow focused on publish and deployment.

Responsibilities:
- build and publish the backend
- deploy to Azure Web App

Triggers:
- `push` to `main`
- optional `workflow_dispatch`

Gate behavior:
- relies on branch protection and required checks so only code that has already passed backend CI is deployed

## PostgreSQL Strategy

Both test workflows should use a GitHub Actions PostgreSQL service container rather than local-machine assumptions or Docker started from test code.

This keeps the test harness portable because:
- `backend.Tests` already connects through `VISIONPAINT_TEST_PGADMIN`
- the harness creates a disposable database per run
- SQL migrations are applied directly by the test project

The workflow should inject:

`VISIONPAINT_TEST_PGADMIN=Host=localhost;Port=5432;Username=postgres;Password=${{ secrets.CI_POSTGRES_PASSWORD }};Database=postgres`

Even though the database is disposable, storing the password in GitHub Secrets is still preferred because it avoids hardcoding credentials into workflow YAML and keeps CI hygiene consistent.

## Data Seeding Strategy

Backend integration tests should remain mostly self-seeding. Each test should create only the records it needs, usually through bootstrap/auth flows and direct API requests.

Playwright should start with minimal deterministic setup:
- use the API or bootstrap flow for auth setup where practical
- add a small targeted seed helper only when richer UI scenarios require pre-existing data

Avoid introducing a large shared demo dataset. It creates brittle coupling and makes failures harder to reason about.

## Tradeoffs Considered

### Single workflow with multiple jobs

Pros:
- fewer files
- simpler initial setup

Cons:
- muddier ownership
- harder to evolve triggers independently
- noisier failure signals

### Separate workflows

Pros:
- clearer test intent
- cleaner failure isolation
- easier future maintenance
- different gating policies per workflow

Cons:
- slightly more YAML to manage

Separate workflows are the better fit for this repo.

## Deployment Policy

Initial deployment policy:
- **Backend CI** and **UI E2E** are required checks for merging into `main` (see [branch-protection.md](../ci/branch-protection.md)).
- Backend deploy runs on push to `main` and should only run after those checks have passed on the merged commit.

Configure protection with `scripts/configure-main-branch-protection.ps1` after `gh auth login`, or via GitHub **Settings → Branches**.

## Implementation Notes

- Add `backend-ci.yml` for backend build and integration tests.
- Add `e2e-ui.yml` for browser tests.
- Update the Azure backend deployment workflow only enough to fit the new gating model.
- Use a shared secret name for the CI Postgres password across workflows.
- Keep seeding logic out of workflow YAML where possible.

## Success Criteria

- Pull requests show separate backend and UI quality signals.
- Backend deployment only occurs after backend CI passes.
- CI can provision Postgres consistently without local assumptions.
- The test suites remain deterministic and maintainable.
