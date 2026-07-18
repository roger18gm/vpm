# VisionPaint UI Specification

**Canonical design document.** Implement frontend behavior from this file. See also [screen-map.md](./screen-map.md), [user-journeys.md](./user-journeys.md), [wireframes/](./wireframes/).

**Related:** [Auth design](../superpowers/specs/2026-05-07-auth-design.md), [Schema](../../database/schema.md), [Proposal](../../proposal.md).

---

## 1. Design principles

1. **Mobile-first for crew** — thumb reach, large tap targets, minimal typing on site.
2. **One primary action per screen** — e.g. Clock tab = clock in/out, not a form dump.
3. **Job-centric hub** — detail page links time, photos, crew, notes.
4. **Obvious state** — scheduled / in progress / completed visible at list and detail.
5. **Fail clear** — 401 → sign in; 403 → access denied screen; network → retry message.
6. **Painting-contractor simple** — no enterprise chrome; smaller teams, fast paths.

---

## 2. Design tokens

Use Tailwind v4 CSS-first theme variables; wireframes use the same hex values.

### Brand palette (red, grey, white, black)

| Role | Token | Hex |
|------|-------|-----|
| Red | `color-primary` | `#991b1b` |
| Red hover | `color-primary-hover` | `#7f1d1d` |
| Grey (page) | `color-bg` | `#f4f4f5` |
| Grey (muted text) | `color-text-muted` | `#6b7280` |
| Grey (border) | `color-border` | `#e5e7eb` |
| Grey (body text) | `color-text` | `#111827` |
| White | `color-surface` | `#ffffff` |
| Black | `color-black` | `#000000` |

### Brand palette vs semantic colors

**Brand chrome** (nav, primary buttons, headers) uses **only** red, grey, white, and black.

**Semantic badges** (job status, priority) use additional hues so states are scannable in the field. Do not use semantic colors for buttons or navigation.

| Token | Value | Usage |
|-------|-------|--------|
| `color-primary` | `#991b1b` | Primary buttons, brand accent, active nav |
| `color-primary-hover` | `#7f1d1d` | Button hover |
| `color-bg` | `#f4f4f5` | Page background |
| `color-surface` | `#ffffff` | Cards, panels |
| `color-text` | `#111827` | Body text |
| `color-text-muted` | `#6b7280` | Labels, helpers |
| `color-border` | `#e5e7eb` | Borders, dividers |
| `color-error` | `#b91c1c` | Errors |
| `color-black` | `#000000` | Strong headings (sparingly) |

### Typography

- Font: **Inter** (via `@fontsource/inter` or Google Fonts in app).
- Page title: 24–32px, semibold.
- Section title: 18px, semibold.
- Body: 14–15px.
- Label/caption: 12–13px, muted.

### Spacing & layout

- Page padding: `16px` mobile, `24px` desktop.
- Card padding: `16–20px`.
- Grid gap: `12–16px`.
- Max content width (manager desktop): `1120px` centered.
- **Minimum touch target:** `44×44px`.

### Status badges

| Status | Background | Text |
|--------|------------|------|
| `scheduled` | `#f3f4f6` | `#374151` |
| `in_progress` | `#fef3c7` | `#92400e` |
| `completed` | `#dcfce7` | `#166534` |
| `cancelled` | `#fee2e2` | `#991b1b` |

### Priority indicator (list/detail)

| Priority | UI |
|----------|-----|
| `low` | muted dot or no badge |
| `normal` | none |
| `high` | orange “High” chip |
| `urgent` | red “Urgent” chip |

### Icons

Use [@iconify/vue](https://iconify.design/) with **MDI** icons from [icones.js.org](https://icones.js.org) (free). See [components.md](./components.md) for nav/action mapping.

---

## 3. Responsive behavior

| Breakpoint | Width | Behavior |
|------------|-------|----------|
| Mobile | `<768px` | Single column; bottom nav for crew/manager mobile |
| Tablet | `768–1023px` | Single column; optional 2-col on job detail sections |
| Desktop | `≥1024px` | Manager: sidebar + 2-col grids where useful (dashboard, jobs list + create panel) |

**Crew:** optimize for `375px` width (iPhone SE class).  
**Manager dashboard:** usable at mobile width but secondary to desktop review.

---

## 4. Global components

| Component | Description |
|-----------|-------------|
| `AppShell` | Auth gate, layout, nav |
| `BottomNav` | 3–4 items; active state = primary color |
| `SidebarNav` | Desktop manager nav |
| `PageHeader` | Kicker “VisionPaint”, title, optional action button |
| `Card` | White surface, border, rounded-lg |
| `StatusBadge` | Job status chip |
| `PriorityChip` | Optional priority label |
| `EmptyState` | Icon + title + short help + CTA |
| `LoadingSkeleton` | List/detail placeholders |
| `ConfirmDialog` | Clock out, archive job |
| `Toast` | Success/error ephemeral (create job, upload) |

### Bottom nav (crew & mobile manager)

| Icon label | Path | Notes |
|------------|------|-------|
| Jobs | `/jobs` | List icon |
| Clock | `/clock` | Show dot if clocked in |
| Account | `/account` | Person icon |

Managers add **Dashboard** as first tab on mobile, or use sidebar on desktop.

---

## 5. Screen specifications

### SCR-001 / SCR-002 — Sign in & bootstrap

**Routes:** `/login`  
**Wireframe:** [wireframes/auth.html](./wireframes/auth.html)

| | |
|--|--|
| **Roles** | Guest |
| **Layout** | Centered card on `color-bg`, max-width 400px |

**Fields:** email, password; bootstrap adds name.  
**Actions:** Sign in (primary), toggle bootstrap if `canBootstrap`, **Forgot password?** → `/forgot-password`.  
**States:** loading session, error message inline, success redirects to role home (`/dashboard` or `/jobs`); `?reset=1` shows “Password updated…” banner.  
**API:** `GET /api/auth/status`, `POST /api/auth/login`, `POST /api/auth/bootstrap`.

### SCR-001a — Forgot password

**Route:** `/forgot-password`  
**Roles:** Guest  
**Fields:** email.  
**Behavior:** Always show generic confirmation after submit (no email enumeration).  
**API:** `POST /api/auth/forgot-password`.

### SCR-001b — Reset password

**Route:** `/reset-password?token=…`  
**Roles:** Guest  
**Fields:** new password, confirm (min 8).  
**API:** `POST /api/auth/reset-password`. On success → `/login?reset=1`.

---

### SCR-003 — Manager dashboard

**Route:** `/dashboard`  
**Wireframe:** [wireframes/manager-dashboard.html](./wireframes/manager-dashboard.html)  
**Roles:** `owner`, `admin`, `manager`

**Sections (top to bottom):**

1. **Summary cards** (row): Active jobs count | In progress | Completed this week (planned metrics).
2. **Overdue jobs** — list rows: title, due date, status badge; empty: “No overdue jobs.”
3. **Recent activity** (P1) — last status changes or notes.

**Primary action:** FAB or header button “New job” → SCR-006.  
**API (planned):** aggregate from `GET /api/jobs` client-side for MVP; dedicated dashboard endpoint later.

---

### SCR-004 / SCR-005 — Jobs list

**Route:** `/jobs`  
**Wireframe:** [wireframes/crew-jobs.html](./wireframes/crew-jobs.html) (crew); manager uses same + filters.

| Role | Data scope |
|------|------------|
| `crew` | Jobs where user has active `job_assignment` (filter client-side until API exists) |
| `manager+` | All company jobs |

**UI elements:**

- Filter chips: All | Scheduled | In progress | Completed (manager).
- Search (P1): filter by title/address.
- Row: title, status badge, priority chip, address line 1 (truncated), due date if set.
- Tap row → SCR-007.
- Manager: sticky **+ New job** → SCR-006.

**Empty states:**

- Crew, no assignments: “No jobs assigned yet.”
- Manager, no jobs: “Create your first job.” + CTA.

**API:** `GET /api/jobs`.

---

### SCR-006 — Create job

**Route:** `/jobs/new`  
**Wireframe:** [wireframes/job-create.html](./wireframes/job-create.html)  
**Roles:** `owner`, `admin`, `manager`

**Fields (MVP):**

| Field | Required | Notes |
|-------|----------|-------|
| title | yes | |
| priority | no | default `normal` |
| status | no | default `scheduled` |
| description | no | textarea |
| address_line1, city, state_region, postal_code | no | group as “Job site” |
| scheduled_start_at, due_at | no | date inputs |

**Actions:** Save (primary), Cancel → back.  
**Success:** Navigate to SCR-007 with new id.  
**API:** `POST /api/jobs`.

---

### SCR-007 — Job detail

**Route:** `/jobs/:id`  
**Wireframe:** [wireframes/job-detail.html](./wireframes/job-detail.html)  
**Roles:** all with company scope; crew only if assigned (403 otherwise).

**Header:** title, status badge, priority, address (link to maps P1).

**Sections:**

1. **Quick actions** (role-dependent): Clock in/out | Photos | Assign crew (manager).
2. **Schedule** — scheduled start/end, due date.
3. **Crew** — avatars/names (from assignments).
4. **Time on this job** — total hours (J6).
5. **Notes** (P1) — list + add note.
6. **Description** — collapsible if long.

**Crew primary:** Clock in when not active; “View photos”.  
**Manager primary:** Change status dropdown, Edit, Assign crew.

**API:** `GET /api/jobs/:id`; updates `PUT /api/jobs/:id` (manager).

---

### SCR-010 — Clock hub

**Route:** `/clock`  
**Wireframe:** [wireframes/crew-clock.html](./wireframes/crew-clock.html)  
**Roles:** all

**Tabs:** **Clock** (default) | **This week**

**Clock tab — not clocked in:**

- Message: “Select a job to clock in.”
- List of assigned active jobs (same filter as SCR-005) OR deep link from SCR-007 only for MVP.
- Large **Clock in** on selected job.

**Clock tab — clocked in:**

- Job title (large).
- Elapsed timer (live).
- **Start break** / **End break** (toggle).
- **Clock out** (secondary destructive confirm).

**This week tab:**

- Week range label with ← / → navigation (Sunday-start week).
- Seven expandable day rows (Sun–Sat): work hours + break hours collapsed; expanded shows job sessions and break windows.
- Managers+: person picker (`GET /api/people`) to view any worker’s timesheet.
- Crew: own timesheet only.
- **Add/Edit time modal:** job, date, clock in/out, optional notes, and a **break list** (start, end, type: lunch/rest/other) with add/remove — no standalone break-minutes field. Always posts `breaks[]` to `POST/PUT /api/time/entries`.

**API:** `GET /api/time/active`, `POST /api/time/clock-in`, `POST /api/time/clock-out`, `POST /api/time/break/start`, `POST /api/time/break/end`, `GET /api/time/weekly`, `POST/PUT/DELETE /api/time/entries`.

---

### SCR-011 / SCR-012 — Photo timeline & upload

**Routes:** `/jobs/:id/photos`, `/jobs/:id/photos/new`  
**Wireframes:** [job-photos.html](./wireframes/job-photos.html), [job-photos-upload.html](./wireframes/job-photos-upload.html)  
**Roles:** assigned crew + manager+

**Timeline:** vertical list, thumbnail left, caption + timestamp right; kind label (Before / After / Progress).  
**Upload:** file input `accept="image/*" capture="environment"`, caption, kind select, submit.  
**API (planned):** Supabase storage + `job_photo` record.

---

### SCR-013 — Account

**Route:** `/account`  
**Roles:** authenticated

Show: name, email, company role. **Sign out** button.  
**API:** `GET /api/auth/status`, `POST /api/auth/logout`.

---

### SCR-014 — Access denied

**Route:** `/forbidden`  
Show message + link back to home for user role.

---

## 6. Error & session handling

| HTTP | UI behavior |
|------|-------------|
| 401 | Clear session UI; redirect `/login` |
| 403 | SCR-014 or inline “You don’t have access.” |
| 404 | “Job not found” on detail |
| 5xx | “Something went wrong” + retry |

Session: all API calls `credentials: 'include'` per auth spec.

---

## 7. Accessibility (MVP baseline)

- Semantic headings (`h1` once per page).
- Form labels associated with inputs.
- Focus visible on interactive elements.
- Status not conveyed by color alone (text label on badges).
- `aria-current="page"` on active nav item.

---

## 8. Visual references (optional adoption)

When pulling patterns from inspiration sites, note here:

| Pattern | Source | Applied to |
|---------|--------|------------|
| Card list with status pill | Refero (field service apps) | SCR-004, SCR-005 |
| Large single CTA | Call to Inspiration | SCR-010 clock in |
| Dashboard stat cards | Refero | SCR-003 |

---

## 9. Wireframe index

| File | Screens |
|------|---------|
| [wireframes/index.html](./wireframes/index.html) | Hub |
| [wireframes/auth.html](./wireframes/auth.html) | SCR-001 |
| [wireframes/crew-jobs.html](./wireframes/crew-jobs.html) | SCR-005 |
| [wireframes/crew-clock.html](./wireframes/crew-clock.html) | SCR-010 (idle) |
| [wireframes/crew-clock-active.html](./wireframes/crew-clock-active.html) | SCR-010 (active) |
| [wireframes/job-detail.html](./wireframes/job-detail.html) | SCR-007 |
| [wireframes/manager-dashboard.html](./wireframes/manager-dashboard.html) | SCR-003 |
| [wireframes/manager-jobs.html](./wireframes/manager-jobs.html) | SCR-004 |
| [wireframes/job-create.html](./wireframes/job-create.html) | SCR-006 |
| [wireframes/job-photos.html](./wireframes/job-photos.html) | SCR-011 |
| [wireframes/job-photos-upload.html](./wireframes/job-photos-upload.html) | SCR-012 |

Open `wireframes/index.html` in a browser locally.

---

## 10. Agent implementation notes

1. Read this file + [stakeholder-decisions.md](./stakeholder-decisions.md) + target screen section before coding.
2. Match tokens in Tailwind v4 `@theme` / CSS variables (see brand palette above).
3. Vue 3 + Vue Router: views under `src/views/`, shared UI in `src/components/` — see [components.md](./components.md).
4. Use **Pinia** for cross-page state (auth session, active clock entry).
5. Do **not** use Supabase client in frontend for DB.
6. Map each change set to screen IDs (e.g. “Implements SCR-007, SCR-010”).
