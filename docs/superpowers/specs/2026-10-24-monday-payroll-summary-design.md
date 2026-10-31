# Monday Payroll Summary Email Design

**Status:** Approved for implementation planning (2026-10-24)  
**Depends on:** Weekly timesheet minute rules, Brevo email, company timezone  
**Related:** [Reporting and notifications backlog](../../backlog/reporting-and-notifications.md)

## Problem

Owners and admins have to open the app and check each person to see last week's hours. The Monday payroll email sends that summary once the week is over.

## Goal

On Monday, email every active owner and admin the previous Sunday–Saturday week: work time and break time for every active member, with each day listed, using the same numbers as the weekly timesheet.

## Decisions

| Decision | Choice |
|----------|--------|
| Recipients | Active `owner` and `admin` members. Not `manager` or `crew`. |
| Listed people | Every active member, including people with zero hours |
| Week | Sunday–Saturday in the company timezone. The email covers the week that finished before this week's Sunday |
| Day placement | All minutes for a shift sit on the local clock-in day |
| Open shift | Count through the instant the email is sent, same as the weekly timesheet. Label the person still clocked in |
| Send time | First Production run on or after Monday 07:00 company time |
| Missed Monday | Send the next time the API is up, until the following Monday 07:00, when only the newest finished week is eligible |
| Approach | In-process timer plus one database claim per company and week |
| Detail excluded | No per-job lines, no crew copy, no dashboard button |

## What the email contains

One message per recipient. Every recipient for a company gets the same body.

- Subject and heading use the week dates. Same calendar year: `Payroll summary for Oct 18–Oct 24, 2026`. A week that crosses a year: `Payroll summary for Dec 28, 2026–Jan 3, 2027`.
- Company name.
- People sorted by name, then person id. Each person shows work total and break total, then Sunday through Saturday. A day with no time shows `0.0` work and `0.0` break.
- If any of that person's entries in the week have no clock-out, the person line says they are still clocked in.
- A company work total and break total, the sum of the listed people.

Hours are decimal hours to one place, from integer minutes, invariant culture: `90` minutes is `1.5`, `0` minutes is `0.0`. Totals sum minutes first, then format once. HTML is a simple table. A plain-text body carries the same numbers. The sender is the existing Brevo sender.

Someone whose membership is not active at send time is left off, even if they worked that week. Owners and admins who are active members also appear in the hour list.

## When it sends

The scheduler runs only when the host environment is Production. Development, Testing, and E2E do not register it.

It checks once at startup, then every five minutes. For each company:

1. Resolve `company.timezone` with the existing helper (unknown ids fall back to `America/Denver`).
2. Let `thisSunday` be the Sunday of the local calendar date. The target week starts `thisSunday` minus 7 days and ends the following Saturday.
3. Sending opens at 07:00 local on the Monday after that Saturday. Before that instant, skip the company.
4. If a sent claim already exists for that company and week, skip.

A week that never sent is not backfilled once a newer week becomes the target.

## How a send is claimed

Table `payroll_summary_send`:

| Column | |
|--------|--|
| `company_id` | integer, references `company (id)` on delete cascade |
| `week_start` | date, the Sunday |
| `claimed_at` | timestamptz, not null |
| `sent_at` | timestamptz, null until at least one email succeeds |

Primary key `(company_id, week_start)`.

Flow:

1. Insert the claim. If the key already exists and `sent_at` is set, stop.
2. If a claim exists, `sent_at` is null, and `claimed_at` is less than 15 minutes ago, stop. Another run owns it.
3. If that unfinished claim is 15 minutes or older, take it over by setting `claimed_at` to now only when `claimed_at` still equals the value that was read. If the update matches no row, stop.
4. Build the summary and send.
5. If at least one recipient send succeeds, set `sent_at`. Log any recipient that failed. Do not retry those addresses.
6. If every send fails, or there is no recipient address, delete the claim so the next check can try again. Log the failure.

There is no HTTP endpoint for this feature.

## Who is included

**Listed member:** `company_member.status = active` for that company. `person.is_active` is not required. Entries are that person's `time_entry` rows whose job `company_id` is this company and whose local clock-in date falls in the target week. The UTC query window matches the weekly timesheet: from the start of Sunday minus one day through the end of Saturday plus one day, then keep rows whose local clock-in date is inside the week.

**Recipient:** an active member with role `owner` or `admin` (case-insensitive), an active `auth_user`, and a non-blank sign-in email (`auth_user.email`). They are recipients even when their own hours are zero.

## Minute rules

Payroll and the weekly timesheet call the same helper. Do not copy the formula into the mailer.

For each entry, with `now` equal to the send instant:

- Closed entry: break minutes are `time_entry.break_minutes`. Work minutes truncate `(clock_out - clock_in)` toward zero to a whole number of minutes, then subtract those break minutes, then floor at 0.
- Open entry: break minutes are the sum of `time_break` windows, using `now` when `break_end_at` is null. Each window truncates toward zero the same way. Work minutes truncate `(now - clock_in)` toward zero, then subtract those break minutes, then floor at 0.
- Add the entry's work and break to the local clock-in day.

## Components

- `PayrollSummaryScheduler` — `BackgroundService`. Creates a scope per check. Registered only in Production.
- `PayrollSummaryService` — decides the week, claims the row, loads people and entries, sends mail. Takes `TimeProvider` so tests can pin "now".
- Shared minute helper used by `TimeEntryService` and `PayrollSummaryService`.
- `IEmailSender` — unchanged Brevo implementation. Tests use the existing recording sender.

Schema changes: a SQL migration under `database/migrations`, the same table in `database/schema.sql`, and an EF entity on `AppDbContext`. Test reset truncates `payroll_summary_send` with the other app tables.

## Failure behavior

| Case | Result |
|------|--------|
| Brevo rejects one recipient and accepts another | Week is marked sent. The failure is logged |
| Brevo rejects every recipient | Claim is deleted. Next check retries |
| No owner or admin has an email | Claim is deleted. Next check retries. A warning is logged |
| Process dies after the claim and before `sent_at` | Next check takes over after 15 minutes |
| Second instance races the claim | Unique key and the compare-and-swap on `claimed_at` leave a single sender |

Password-reset mail is unchanged. A payroll failure does not surface in the app UI.

## Tests

Call `PayrollSummaryService` with a fixed `TimeProvider`. Do not wait for the timer.

| # | Scenario | Expected |
|---|----------|----------|
| 1 | Local time is Sunday evening, or Monday 06:59 | No email, no claim |
| 2 | Monday 07:00, previous week has a mix of hours, a zero-hour active member, and an open Saturday shift | One email to each active owner and admin. Zero-hour person is listed. Open-shift minutes sit on Saturday and run through 07:00 Monday. Person is marked still clocked in. Manager and crew are not recipients. `sent_at` is set |
| 3 | The service runs again for that week | No second email |
| 4 | Every send throws | Claim is gone. A later run can send |
| 5 | One recipient succeeds and another throws | `sent_at` is set. A later run does not send again |
| 6 | No owner or admin has an email address | No email. Claim is gone |
| 7 | Next Monday 07:00 | The new previous week is the only candidate |

Also assert the shared helper's closed-entry and open-entry results match the weekly timesheet for the same rows.

## Out of scope

- Job-closed-with-gaps email
- Long-break and still-clocked-in-after-hours alerts
- In-app hours-by-job report
- Per-job lines inside this email
- A button that sends the summary on demand
- An external Azure timer or a separate worker
- Backfill of weeks older than the latest finished week
- Pay rates, overtime, or a payroll-file export
