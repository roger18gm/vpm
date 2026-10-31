# Job closed with gaps email design

**Status:** Implemented (2026-10-31)  
**Date:** 2026-10-31  
**Depends on:** Job status updates, job checklists, job photos, Brevo email  
**Related:** [Reporting and notifications backlog](../../backlog/reporting-and-notifications.md)

## Problem

A job can be marked completed while required checklist items are still pending, or while it has no photos. Nobody is told.

## Goal

When a job enters completed and one of those gaps is present, email the person who saved it. The job still completes.

## Decisions

| Decision | Choice |
|----------|--------|
| Recipient | The signed-in person who saved the job (`CurrentUserContext.Email`) |
| Checklist gap | The job has a checklist, and at least one required item is still `pending` |
| Missing checklist | No checklist section and no checklist gap |
| Optional items | A pending optional item does not count |
| Not applicable | A required item marked `not_applicable` does not count |
| Photo gap | The job has zero `job_photo` rows. Any kind counts as a photo |
| Both gaps | One email containing both |
| Repeat | Every later change into `completed` sends again while a gap remains |
| Already completed | Saving a job that is already `completed` sends nothing |
| Cancel | Archiving a job sends nothing |
| Create | Creating a job whose status is `completed` follows the same rules |
| Delivery | Send during that save, after the job row is stored |
| Mail failure | Log it. The API still returns the saved job. That transition is not retried |
| Schema | No new table |
| App UI | No new warning |

## When it sends

`JobsController` calls `JobCompletionGapMailer` only after the job is stored:

- `POST /api/jobs`: after the create transaction commits. The previous status passed in is null.
- `PUT /api/jobs/{id}`: after `SaveChanges`. The previous status is the status loaded before the update.

Archive does not call the mailer.

The mailer returns immediately unless the saved status is `completed` and the previous status is null, whitespace, or anything other than `completed`. The comparison is ordinal ignore case. By the time the mailer runs, `JobUpdateApplier` and create have already stored the normalized status.

## What the email contains

One message. Subject: `Completed with gaps: {job title}`. Carriage returns and line feeds in the title are replaced with a space so the subject stays one line.

The body, in this order:

1. The job title.
2. A link to `{FrontendBaseUrl}/jobs/{id}`, using `AppOptions.FrontendBaseUrl` with one trailing slash removed. The visible link text is that URL. In HTML, the `href` and the link text are both the HTML-encoded URL.
3. When required items are still pending: the heading `Open required checklist items`, then those titles. Order is checklist sort order, then template item id.
4. When the job has zero photos: the sentence `This job has no photos.`

A missing section is omitted, including its heading. HTML and plain text carry the same facts. In HTML, the job title, each item title, and the link text are HTML-encoded. The plain-text body uses the stored title and item titles. Item lines in plain text are `- {title}`.

The sender is the existing `IEmailSender` (Brevo in the app, the recording sender in tests).

## Components

- `JobCompletionGapMailer` — scoped service, registered in `Program.cs`. Method: `NotifyAsync(Job job, string? previousStatus, string? recipientEmail, CancellationToken cancellationToken)`. It reads through the request's `AppDbContext` and does not call `SaveChanges`.
- `JobsController` — calls the mailer at the two points above, with the current user's email.
- `IEmailSender` and `AppOptions` — unchanged.

Checklist query: `job_checklist_item` joined to `checklist_template_item` for this job, `is_required` true and status `pending`. Photo check: any `job_photo` row for this job. No gaps means return without sending.

## Failure behavior

| Case | Result |
|------|--------|
| `SendAsync` throws | Log the exception with the job id. `NotifyAsync` returns. Create still returns 201 and update still returns 200 with the saved job |
| Recipient address is null, empty, or whitespace | Log a warning with the job id. Send nothing |
| Process stops after the save and before the send | That transition is not retried. A later change into `completed` sends only if a gap is still there |
| Send runs after the create transaction has committed | A mail failure cannot roll the job back |

Password-reset mail and the Monday payroll email are unchanged.

## Tests

HTTP cases use the existing API fixture and `RecordingEmailSender`. Each test clears that sender first. The throw case calls `JobCompletionGapMailer` with a sender that throws.

| # | Scenario | Expected |
|---|----------|----------|
| 1 | Update to completed. A required item is still pending. One photo exists. The job title contains `<` and a newline. The item title contains `<`. A second owner exists | One email to the person who saved. Subject is `Completed with gaps: {title}` with the newline replaced by a space. HTML escapes `<`. Plain text keeps `<` and lists the item as `- {title}`. The body has no photo sentence. The other owner receives nothing. The job is completed |
| 2 | Update to completed. No checklist. Zero photos | One email. The body contains `This job has no photos.` and does not contain `Open required checklist items` |
| 3 | Update to completed. A required item is pending and there are zero photos | One email with the item title and `This job has no photos.` |
| 4 | One required item is done, another is not applicable, an optional item is still pending, and one photo exists. Update to completed | No email |
| 5 | No checklist. One photo exists. Update to completed | No email |
| 6 | The job is already completed and a required item is still pending. `PUT` sets status to `completed` again | No email |
| 7 | Complete while a required item is pending and a photo exists. Clear the sender. Move the job back to in progress, then complete it again | One new email |
| 8 | Complete while a required item is pending and a photo exists. Mark that item done. Clear the sender. Move the job back to in progress, then complete it again | No email |
| 9 | Create a job as scheduled, then create another job already completed with no photos | The scheduled create sends nothing. The completed create sends `This job has no photos.` |
| 10 | The sender throws on a direct `NotifyAsync` call for a job that just entered completed | The call returns. The job status stays `completed` |

## Out of scope

- Refusing to complete a job that has gaps
- Emailing anyone other than the person who saved
- Treating a missing checklist as a gap
- Requiring a before photo and an after photo
- Retrying a failed send
- A warning in the app
- A new table
- Changes to payroll mail or password-reset mail
- Manager clock alerts and the in-app hours-by-job report
