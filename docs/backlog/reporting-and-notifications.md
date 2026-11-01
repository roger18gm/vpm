# Reporting and notifications

**Status:** Item 1 has a design pending review. Item 2 is implemented. Item 3 is implemented. Item 4 is noted only.  
**Date:** 2026-10-24

Email today is password reset only (Brevo). The API has no scheduled job runner yet, so the email items need one before they can send.

## 1. Monday payroll email

Owners and admins receive a summary of the week that just ended: hours and breaks per person, with each day listed.

Design: [2026-10-24-monday-payroll-summary-design.md](../superpowers/specs/2026-10-24-monday-payroll-summary-design.md)

Plan: [2026-10-24-monday-payroll-summary.md](../superpowers/plans/2026-10-24-monday-payroll-summary.md)

## 2. Job closed with gaps

When a job is marked completed, email if required checklist items are still open, or if the job has no photos.

Design: [2026-10-31-job-closed-with-gaps-design.md](../superpowers/specs/2026-10-31-job-closed-with-gaps-design.md)

Plan: [2026-10-31-job-closed-with-gaps.md](../superpowers/plans/2026-10-31-job-closed-with-gaps.md)

## 3. Manager clock alerts

Email the lead or supervisor when a break runs long, or when someone is still clocked in after 7:00 PM on a weekday. The person on the clock is not the recipient.

Design: [2026-10-31-manager-clock-alerts-design.md](../superpowers/specs/2026-10-31-manager-clock-alerts-design.md)

Plan: [2026-10-31-manager-clock-alerts.md](../superpowers/plans/2026-10-31-manager-clock-alerts.md)

## 4. In-app hours-by-job report

A report in the app of hours by job: who worked the job and for how long. Related existing intent: journey J6 in `docs/design/user-journeys.md` (time on a single job). This item is the report, not that job-detail section.
