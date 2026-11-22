# Gusto employee onboarding

**Date:** 2026-11-21

An owner or admin can open Gusto's hosted employee-onboarding page from Users. Gusto collects the Social Security number, tax details, bank account, and pay. VisionPaint does not. When the admin comes back, the existing Gusto column lists anyone Gusto now marks onboarded and active, and the admin saves the link as they do today.

## Decision

Use Gusto's hosted `add_employees` flow. Do not build onboarding forms in VisionPaint.

`employee_self_management` was rejected because it requires VisionPaint to create the employee first, including a pay amount and a work address, and then to deliver a link that Gusto will not email. `add_employees` is the company-level flow Gusto documents for onboarding employees, and its page collects the sensitive fields.

`company_onboarding` is the second flow. It is offered only after `add_employees` is rejected because the demo company is not ready.

## Who and where

Owners and admins only, on `/users`, the same gate as the directory. One **Onboard in Gusto** button sits in the page header beside **New user**. It is not a per-row action. The flow belongs to the Gusto company, not to one VisionPaint person.

After Gusto onboarding, the admin still chooses the employee and compensation job in the Gusto column and clicks **Save**. Matching by email, automatic linking, and unlink stay out of scope. **Send week to Gusto** is unchanged.

## API

`POST /api/users/gusto-onboarding`

```json
{ "flowType": "add_employees" }
```

`flowType` may also be `company_onboarding`. Any other value is **400** `Choose a Gusto onboarding flow.`

Success is **200**:

```json
{ "url": "https://flows.gusto-demo.com/flows/example" }
```

The server calls `POST /v1/companies/{company_uuid}/flows` with `{ "flow_type": "<flowType>" }` and no entity fields. It accepts the returned `url` only when the scheme is `https` and the host is exactly `flows.gusto-demo.com`. The flow URL is not stored, logged, or written to the database.

| Condition | Status | Message |
| --- | --- | --- |
| Caller is not owner or admin | 403 | Only owners and admins can manage users. |
| `flowType` is missing or not one of the two allowed values | 400 | Choose a Gusto onboarding flow. |
| `add_employees` returns HTTP 422 | 422 | The Gusto company is not ready for employees. |
| Demo is not configured, the token fails, the call fails, the URL host is wrong, or any other non-2xx remains after one 401 refresh | 503 | The Gusto demo is not configured or could not be reached. |

A 401 refreshes the company token once and retries that call. A second 401 is **503**. The same demo-only rules as the directory apply: base URL exactly `https://api.gusto-demo.com`, header `X-Gusto-API-Version: 2026-06-15`, 30-second timeout, tokens kept in memory.

## Users page

**Onboard in Gusto** calls the endpoint with `add_employees` and opens the returned URL in a new tab. If the browser blocks the tab, the page says **Allow pop-ups to open Gusto.** The URL is not shown as text.

When `add_employees` returns **422**, the page shows that message and a **Set up the Gusto company** action. That action calls the same endpoint with `company_onboarding` and opens its URL the same way.

When the admin returns to the Users tab, the page reloads the Gusto directory. The user list is left as it is. A directory error still appears above the table, and the Gusto column still shows an em dash while that error lasts.

## Out of scope

VisionPaint forms for Social Security number, bank account, pay rate, home address, or tax. Creating an employee with `POST /v1/companies/{company_uuid}/employees`. Emailing an onboarding link. Per-person self-onboarding. Iframe embedding and `postMessage`. Storing the flow URL. Changing who can be linked, the unique employee rule, or send-week behavior.
