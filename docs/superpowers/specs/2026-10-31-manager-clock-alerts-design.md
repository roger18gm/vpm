# Manager clock alerts design

**Status:** Implemented (2026-10-31)  
**Date:** 2026-10-31  
**Depends on:** Open time entries, open breaks, job assignments, Brevo email, company timezone, the payroll scheduler pattern  
**Related:** [Reporting and notifications backlog](../../backlog/reporting-and-notifications.md)

## Problem

A lead can miss that someone has been on a break for a long time, or is still clocked in after the workday. The person on the crew is unlikely to read email while they are away from the phone.

## Goal

On a fixed schedule, email the lead or supervisor for that job when a break has run past its threshold, or when someone is still clocked in after 7:00 PM on a weekday. Send one follow-up the next weekday morning if it is still true, then stop for that break or that shift.

## Decisions

| Decision | Choice |
|----------|--------|
| Recipients | Current `lead` or `supervisor` on the job, excluding the person the alert is about. If none remain who can receive mail, the company's active owners and admins |
| Long break | An open break on an open shift. Lunch is due at 45 whole minutes. Rest and other are due at 20 |
| After hours | An open shift at the weekday 7:00 PM pass. Saturday and Sunday have no after-hours mail |
| When it evaluates | 7:00 AM and 7:00 PM company time only. A break that crosses its threshold at 10:00 AM waits for the next unsent slot |
| Catch-up | If the process is down at 7:00 and returns before the next slot, that slot still sends. Once the next slot has started, the missed slot is left behind |
| Repeat | One first email, then one follow-up the next weekday at 7:00 AM if still true. After that, quiet until the break ends or they clock out |
| Shape | One email per recipient per slot, listing every person they cover |
| Approach | Production timer, one slot claim per company, one row per break or shift |
| App UI | None |

## When a pass runs

`ClockAlertScheduler` is a `BackgroundService`. It is registered only when `ClockAlertHost.ShouldRun` is true, which is the Production host environment. Development, Testing, and E2E do not register it. It checks once at startup, then every five minutes. Each check creates a scope and calls `IClockAlertService.SendDueAlertsAsync`. There is no HTTP endpoint.

For each company, resolve `company.timezone` with `TimeZoneHelper.Resolve`. Unknown ids fall back to `America/Denver`.

A slot is identified by the local date of the 7:00 that opened it, and by `morning` or `evening`.

| Local time | Open slot |
|------------|-----------|
| Before 7:00 AM | The previous calendar date, `evening` |
| 7:00 AM inclusive until 7:00 PM | That calendar date, `morning` |
| 7:00 PM inclusive | That calendar date, `evening` |

The morning slot closes when the evening slot opens. The evening slot closes at the next 7:00 AM. Comparison is against company-local time, so a wake at 7:30 still runs an unsent 7:00 slot. The subject always names 7:00 AM or 7:00 PM, including when the pass runs later. Break length uses the actual send instant.

The Monday payroll email is unchanged and can go out on the same morning.

## What a pass includes

Whole minutes truncate toward zero, the same cast `(int)` of total minutes used for open breaks elsewhere. A break is due when those minutes are greater than or equal to its threshold. `lunch` is 45 minutes, compared ordinal-ignore-case. `rest` and `other` are 20. The break counts only while `break_end_at` is null and the parent `time_entry.clock_out_at` is null. An after-hours alert is an open `time_entry` (`clock_out_at` null). One person has at most one open entry.

**Weekday morning**

- First email for a due break that has no `clock_alert_send` row.
- Follow-up for a row whose follow-up instant is this slot's 7:00 AM, when the condition is still true.
- A follow-up whose instant is earlier than this slot's 7:00 AM is marked done and left out of the email. That missed morning stays behind.
- No first after-hours email. A shift that is still open, with no after-hours row yet, waits for the next weekday evening.

**Weekday evening**

- First email for a due break that has no row.
- First email for an open shift that has no after-hours row.
- No follow-up mail, and this pass does not mark follow-ups done.

**Saturday and Sunday**

- The same two clock times run for due breaks that have no row.
- No after-hours lines, and no new after-hours rows.
- No follow-up mail. A missed follow-up whose instant is already earlier than this slot's 7:00 AM is marked done on a weekend morning pass. A follow-up that is still ahead, such as Monday, stays open.

A person who is due for a break and for after-hours in the same pass appears once, with both lines.

## Follow-up instant

Convert `first_sent_at` to company-local time. If that local time is at or after 7:00 AM, start at the next calendar date. If it is before 7:00 AM, start at that same calendar date. Move forward while the date is Saturday or Sunday. The follow-up instant is 7:00 AM on the date that remains.

The follow-up condition is still true when:

- A break row's break is still open, its shift is still open, and the break is still at or past its threshold.
- An after-hours row's shift is still open.

When the follow-up slot arrives and the condition is gone, set `follow_up_sent_at` and leave that person out of the email. A later edit does not revive it. Ending the break or clocking out is what allows a later alert, because a new break or a new clock-in is a new row.

Examples, America/Denver:

| First send | Follow-up |
|------------|-----------|
| Tuesday 10:00 AM | Wednesday 7:00 AM |
| Tuesday 7:05 PM | Wednesday 7:00 AM |
| Friday 9:00 PM | Monday 7:00 AM |
| Saturday 8:00 AM | Monday 7:00 AM |
| Saturday 6:00 AM, still inside Friday's evening slot | Monday 7:00 AM |
| Monday 6:00 AM, still inside Sunday's evening slot | Monday 7:00 AM |

Sunday 7:00 AM does not send a follow-up that is due Monday.

## Who receives it

A recipient has an active `company_member` (`status = active`), an active `auth_user`, and a non-blank `auth_user.email` after trim.

**Job recipient:** a current `job_assignment` (`unassigned_at` null) whose `assignment_role` is `lead` or `supervisor`, ordinal-ignore-case, and whose person is not the person the alert is about.

**Fallback:** when that job has no remaining job recipient, every active owner and admin of the company, using the same email rules as the payroll summary. Company role `manager` is not a fallback. Assignment roles `crew` and `prep` are not job recipients.

One message per recipient person per slot. An owner who is also a lead receives one email containing the jobs they lead and the fallback jobs. A person is listed once. Two leads on the same job receive the same body for that job. Two people who share an email address each get their own message.

Alerts with at least one recipient are mailed to those people. Alerts with none stay unmarked so a later slot can try again.

## What the email contains

Subject, invariant culture: `Clock alerts for Oct 27, 7:00 AM` or `Clock alerts for Oct 27, 7:00 PM`. The date is the slot's local date (`MMM d`). The time is the slot's 7:00, `h:mm tt`.

The body starts with the company name, then the subject. Jobs are ordered by title, ordinal-ignore-case, then job id. People are ordered by name, ordinal-ignore-case, then person id. Under a job, the link is `{FrontendBaseUrl}/jobs/{id}`, using `AppOptions.FrontendBaseUrl.TrimEnd('/')`. The visible link text is that URL.

Each person is one block. Break lines come first, ordered by `break_start_at`, then break id:

- `Lunch break open for 50 minutes`
- `Rest break open for 22 minutes`
- `Other break open for 30 minutes`

The number is whole minutes from `break_start_at` to the send instant. The word is `minutes` for every count. Then, when after-hours is due for that person in this pass: `Clocked in since 4:10 PM`, using the company-local `clock_in_at` in `h:mm tt`.

A line that is not due is omitted. A morning first send of a break omits `Clocked in since`. A 7:00 AM message uses these same lines for a follow-up and for a break that is being emailed for the first time.

Carriage returns and line feeds in a job title or a person name are replaced with a space before either body is built. HTML encodes the company name, the subject, each job title, each person name, and both the `href` and the link text. Plain text keeps `<`. HTML and plain text carry the same facts.

Plain text puts a blank line between the heading and the first job, and between people. HTML uses a paragraph for the company, the subject, and each fact line, an `h2` for the job title, a paragraph for the link, and a `strong` person name.

## How a pass is claimed

Table `clock_alert_slot`:

| Column | |
|--------|--|
| `company_id` | integer, references `company (id)` on delete cascade |
| `slot_on` | date, the local date of the 7:00 that opened the slot |
| `slot_kind` | text, check constraint `morning` or `evening` |
| `claimed_at` | timestamptz, not null |
| `sent_at` | timestamptz, null until the pass finishes |

Primary key `(company_id, slot_on, slot_kind)`.

Table `clock_alert_send`:

| Column | |
|--------|--|
| `id` | integer identity |
| `company_id` | integer, references `company (id)` on delete cascade |
| `time_break_id` | integer, null, references `time_break (id)` on delete cascade |
| `time_entry_id` | integer, null, references `time_entry (id)` on delete cascade |
| `first_sent_at` | timestamptz, not null |
| `follow_up_sent_at` | timestamptz, null until the follow-up is sent or marked done |

Exactly one of `time_break_id` and `time_entry_id` is set. Unique on `time_break_id` where it is not null. Unique on `time_entry_id` where it is not null. A break row is a long-break alert. An entry row is an after-hours alert.

Claim, matching payroll:

1. Load the slot. If `sent_at` is set, stop.
2. If a claim exists and `claimed_at` is less than 15 minutes ago, stop.
3. If no row exists, insert it. If the key already exists, stop.
4. If the claim is 15 minutes or older, set `claimed_at` to now only when it still equals the value that was read. If that update matches no row, stop.
5. Build the pass and send.
6. Insert a `clock_alert_send` row only after at least one recipient for that alert succeeds. Set `follow_up_sent_at` when a follow-up email succeeds, or when this pass marks that follow-up done without email.
7. If at least one recipient send succeeds, or the pass has nothing that must be retried, set `sent_at`.
8. If every attempted send throws, or every due alert has no recipient address, delete the slot row. The next wake inside the same slot can try again.

A pass whose only work is marking missed or cleared follow-ups sets `sent_at` and sends nothing. A pass with nothing due also sets `sent_at`, so a break that crosses its threshold at 7:30 PM waits for the next slot.

An alert included in a successful message is marked sent even when a second recipient throws. An alert whose every recipient throws stays unmarked. If any other recipient in the pass succeeded, the slot still gets `sent_at`, and the unmarked alert waits for a later slot.

A crash after a send and before `sent_at` can send again after 15 minutes. That is the same takeover payroll uses.

## Components

- `ClockAlertHost` — `ShouldRun` is true only for Production.
- `ClockAlertScheduler` — hosted service, five-minute interval, scope per check.
- `IClockAlertService` / `ClockAlertService` — slot, recipients, send, and claim. Takes `TimeProvider`.
- `ClockAlertMail` — subject, HTML, and plain text.
- `IEmailSender` and `AppOptions` — unchanged.
- EF entities on `AppDbContext`.
- Migration `database/migrations/20261031_170000_clock_alert.sql`, and the same tables in `database/schema.sql`.
- `TestDatabaseInitializer` truncates `clock_alert_send` and `clock_alert_slot` with the other app tables, listed before `time_break`.

Password-reset mail, the Monday payroll email, and the job-completion gap email are unchanged.

## Failure behavior

| Case | Result |
|------|--------|
| Brevo rejects every recipient in the pass | Slot row is deleted. Next wake in that slot retries. Alert rows are not inserted |
| Brevo accepts one recipient and rejects another for the same alert | Alert is marked sent. Slot `sent_at` is set. The failure is logged. That address is not retried |
| One job's recipient succeeds and another job's recipient throws | The successful job's alerts are marked sent. The failed job's alerts stay unmarked. Slot `sent_at` is set |
| No eligible address for anyone due | Warning is logged. Slot row is deleted. Next wake retries |
| Process dies after the claim and before `sent_at` | Next wake takes over after 15 minutes |
| Second instance races the claim | Unique key and the compare-and-swap on `claimed_at` leave a single sender |
| Follow-up morning is missed and a later morning pass runs | `follow_up_sent_at` is set. No follow-up email. An evening pass leaves that follow-up unmarked |

## Tests

Call `ClockAlertService` with a fixed `TimeProvider` and timezone `America/Denver`. Do not wait for the timer. HTTP cases are not required. Mail assertions use `RecordingEmailSender`, cleared at the start of each test. Throw cases use a sender that throws.

An extra scheduler test starts `ClockAlertScheduler` with a short interval and records one call to the service. `ClockAlertHost.ShouldRun` is true for Production and false for Development.

| # | Scenario | Expected |
|---|----------|----------|
| 1 | Tuesday 2026-10-27 7:00 AM, with no open shift. Then 10:00 AM the same morning, with a rest break that has been open for 30 minutes | The 7:00 AM pass sends nothing and sets the morning slot `sent_at`. The 10:00 AM wake sends nothing |
| 2 | Tuesday 2026-10-27 10:00 AM. The morning slot has not run. A rest break has been open for 30 minutes. Another lead and a supervisor are assigned. The worker is also a lead. An owner and an unassigned company manager exist | One email each to the other lead and the supervisor. Subject `Clock alerts for Oct 27, 7:00 AM`. Body contains `Rest break open for 30 minutes` and does not contain `Clocked in since`. The worker, owner, and manager receive nothing. `first_sent_at` and the slot `sent_at` are set |
| 3 | Tuesday 2026-10-27 7:00 PM. On a job with a lead and a supervisor: a lunch break open 45 minutes, a rest break open 19, a rest break open 20, and a clocked-out person. On another job, the only lead is the person clocked in with no break, so owners and admins are the fallback. The job title contains `<` and a newline. A person name contains `<` | The lead and the supervisor each receive the first job only. That body has both the lunch line and `Clocked in since` for the lunch person, and both the 20-minute rest line and `Clocked in since` for the 20-minute rest person. The 19-minute person has no rest line, and does have `Clocked in since`, because that shift is still open. The clocked-out person is omitted. Owners and admins receive only the fallback job and its clocked-in line. HTML encodes `<`. Plain text keeps `<`. The newline in the title is a space. Subject `Clock alerts for Oct 27, 7:00 PM` |
| 4 | A Tuesday 7:00 PM first send is already recorded for one due break and for that same open shift. One lead is assigned, and the lead is not the worker. Wednesday 2026-10-28 7:00 AM. The shift is still open and the break is still over its threshold | One email to that lead, with the break line and `Clocked in since`. Both rows have `follow_up_sent_at` set |
| 5 | That follow-up is already recorded. Wednesday 2026-10-28 7:00 PM. The same break and shift are still open | No email |
| 6 | A Tuesday 7:00 PM first send is recorded for a break and for its shift. The break has ended and the shift is clocked out before Wednesday 2026-10-28 7:00 AM | No email. Both rows have `follow_up_sent_at` set |
| 7 | Saturday 2026-10-31 7:00 PM. A long rest break, and another person still clocked in with no open break | The break is included. The clocked-in person is omitted. The body does not contain `Clocked in since` |
| 8 | Saturday 2026-10-31 7:00 PM first send for a long rest break, still open Sunday 2026-11-01 7:00 AM, still open Monday 2026-11-02 7:00 AM | No email Sunday. Monday morning sends the break follow-up and does not contain `Clocked in since` |
| 9 | Friday 2026-10-30 7:00 PM after-hours first send. Still clocked in Monday 2026-11-02 7:00 AM and Monday 7:00 PM | Follow-up Monday morning. No email Monday evening |
| 10 | Friday 2026-10-30 7:00 PM slot not sent yet. The clock is Friday 9:00 PM. A rest break started at 8:00 PM | One email. Subject `Clock alerts for Oct 30, 7:00 PM`. The rest line says 60 minutes |
| 11 | Friday evening never sent. The clock is Saturday 2026-10-31 8:00 AM. The shift is still open and a rest break has been open 30 minutes | No Friday email. The Saturday morning email includes the break and does not contain `Clocked in since`. Subject `Clock alerts for Oct 31, 7:00 AM` |
| 12 | Tuesday 2026-10-27 7:00 PM. Every send throws. A later wake at 8:00 PM the same evening | The 7:00 PM slot row is gone after the throw. The 8:00 PM wake sends |
| 13 | Two leads. One send throws and the other succeeds | The slot stays sent. A later wake in that slot sends nothing |
| 14 | A due alert exists and nobody eligible has an email address | No email. The slot claim is gone |
| 15 | The slot is already sent | No second email |
| 16 | A follow-up is already recorded and that break has ended. A new open shift has a new rest break of 20 minutes. The clock is the next evening slot, Wednesday 2026-10-28 7:00 PM | One new first email for the new break |

## Out of scope

- Emailing the person the alert is about
- A company setting for the threshold or for 7:00
- Mail at the minute a break crosses its threshold
- Further reminders after the one follow-up
- After-hours mail on Saturday or Sunday
- The in-app hours-by-job report
- Changes to payroll mail, password-reset mail, or job-completion mail
- An HTTP endpoint or a screen in the app
- SMS or push
- Alerts for a break that has already ended, or a shift that has already clocked out
