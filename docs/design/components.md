# Vue component inventory

Maps [ui-spec.md](./ui-spec.md) global components and screens to suggested Vue 3 files. Adjust paths to match your Vite layout.

## Layout & shell

| Component | File (suggested) | Used on |
|-----------|------------------|---------|
| `AppShell` | `src/layouts/AppShell.vue` | All authenticated routes |
| `GuestLayout` | `src/layouts/GuestLayout.vue` | SCR-001 |
| `BottomNav` | `src/components/nav/BottomNav.vue` | Crew + mobile manager |
| `SidebarNav` | `src/components/nav/SidebarNav.vue` | Desktop manager |
| `PageHeader` | `src/components/layout/PageHeader.vue` | Most pages |

## Primitives

| Component | File | Notes |
|-----------|------|-------|
| `VpButton` | `src/components/ui/VpButton.vue` | `variant`: primary, secondary, ghost, danger |
| `VpCard` | `src/components/ui/VpCard.vue` | White surface + border |
| `VpInput` | `src/components/ui/VpInput.vue` | Label, error slot |
| `VpSelect` | `src/components/ui/VpSelect.vue` | Priority, status, photo kind |
| `StatusBadge` | `src/components/job/StatusBadge.vue` | Semantic colors per spec |
| `PriorityChip` | `src/components/job/PriorityChip.vue` | high / urgent only |
| `EmptyState` | `src/components/ui/EmptyState.vue` | Icon + title + CTA |
| `LoadingSkeleton` | `src/components/ui/LoadingSkeleton.vue` | Lists, dashboard |
| `ConfirmDialog` | `src/components/ui/ConfirmDialog.vue` | Clock out, archive |
| `ToastHost` | `src/components/ui/ToastHost.vue` | Global via Pinia or plugin |

## Domain

| Component | File | Screen |
|-----------|------|--------|
| `JobCard` | `src/components/job/JobCard.vue` | SCR-004, SCR-005 |
| `JobList` | `src/components/job/JobList.vue` | Lists + filters |
| `JobForm` | `src/components/job/JobForm.vue` | SCR-006, SCR-008 |
| `JobDetailHeader` | `src/components/job/JobDetailHeader.vue` | SCR-007 |
| `JobCrewList` | `src/components/job/JobCrewList.vue` | SCR-007, SCR-009 |
| `JobTimeSummary` | `src/components/time/JobTimeSummary.vue` | SCR-007, J6 |
| `ClockPanel` | `src/components/time/ClockPanel.vue` | SCR-010 |
| `PhotoTimeline` | `src/components/photo/PhotoTimeline.vue` | SCR-011 |
| `PhotoUploadForm` | `src/components/photo/PhotoUploadForm.vue` | SCR-012 |
| `DashboardStats` | `src/components/dashboard/DashboardStats.vue` | SCR-003 |
| `OverdueJobList` | `src/components/dashboard/OverdueJobList.vue` | SCR-003 |

## Views (Vue Router)

| Route | View file | Screen ID |
|-------|-----------|-----------|
| `/login` | `src/views/LoginView.vue` | SCR-001, SCR-002 |
| `/dashboard` | `src/views/DashboardView.vue` | SCR-003 |
| `/jobs` | `src/views/JobsListView.vue` | SCR-004, SCR-005 |
| `/jobs/new` | `src/views/JobCreateView.vue` | SCR-006 |
| `/jobs/:id` | `src/views/JobDetailView.vue` | SCR-007 |
| `/jobs/:id/edit` | `src/views/JobEditView.vue` | SCR-008 |
| `/jobs/:id/crew` | `src/views/JobCrewView.vue` | SCR-009 |
| `/jobs/:id/photos` | `src/views/JobPhotosView.vue` | SCR-011 |
| `/jobs/:id/photos/new` | `src/views/JobPhotoUploadView.vue` | SCR-012 |
| `/clock` | `src/views/ClockView.vue` | SCR-010 |
| `/account` | `src/views/AccountView.vue` | SCR-013 |
| `/forbidden` | `src/views/ForbiddenView.vue` | SCR-014 |

## Pinia stores (cross-page state)

| Store | File | Holds |
|-------|------|-------|
| `useAuthStore` | `src/stores/auth.ts` | Session, user, role |
| `useClockStore` | `src/stores/clock.ts` | Active `time_entry`, timer tick |
| `useJobsStore` | `src/stores/jobs.ts` | Optional cache; prefer fetch per view for MVP |

## Icons

Use [@iconify/vue](https://iconify.design/) with **Material Design Icons** collection via [icones.js.org](https://icones.js.org):

| UI | Icon name (mdi) |
|----|-----------------|
| Jobs nav | `mdi:clipboard-list-outline` |
| Clock nav | `mdi:clock-outline` |
| Account nav | `mdi:account-outline` |
| Dashboard nav | `mdi:view-dashboard-outline` |
| Add job | `mdi:plus` |
| Photo | `mdi:camera-outline` |
| Overdue | `mdi:alert-circle-outline` |

Install: `npm i @iconify/vue` (no subscription).
