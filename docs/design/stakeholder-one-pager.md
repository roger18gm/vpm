# VisionPaint — Stakeholder one-pager

**Purpose:** Field-friendly job, time, and photo tracking for a small painting crew.  
**Review status:** Accepted for MVP (see [stakeholder-decisions.md](./stakeholder-decisions.md)).

## Who uses what

| Role | Main screens | Phone-first? |
|------|--------------|--------------|
| Owner / manager | Dashboard, all jobs, create job | Yes + desktop |
| Crew | My jobs, clock, photos on job | Yes |

## Five things the app must do (demo order)

1. **Create a job** — title, site, due date → assign crew  
2. **Clock in** — one job at a time, on assigned work only  
3. **Add photos** — before / after / progress on the job  
4. **See overdue work** — dashboard highlights past `due_at`  
5. **Clock out** — confirm; show work time vs break time  

Full steps: [user-journeys.md](./user-journeys.md) (J3 → J1 → J4 → J5 → J2).

## Wireframes (open in browser)

Start here: `docs/design/wireframes/index.html`

| Screen | File |
|--------|------|
| Sign in | `wireframes/auth.html` |
| Crew jobs | `wireframes/crew-jobs.html` |
| Clock | `wireframes/crew-clock.html` |
| Create job | `wireframes/job-create.html` |
| Job photos | `wireframes/job-photos.html` |
| Manager dashboard | `wireframes/manager-dashboard.html` |

## Colors

Red, grey, white, black for brand; status badges use extra colors so “In progress” and “Completed” are obvious at a glance.

## Out of scope for first release

- Client portal login  
- Room-by-room areas  
- Prep checklists  
- Push notifications  
- Offline mode  

---

*Technical spec: [ui-spec.md](./ui-spec.md) · Routes: [screen-map.md](./screen-map.md)*
