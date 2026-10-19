# Runbook — `/admin/e-post`: this month's mail against the free allowance

The page's first region answers one question: how this month's sent mail stands against the provider's free monthly
allowance, and what the mail beyond it costs. The mechanism and its decisions are ADR 0178's; this runbook is the one home for
what the figures mean and how they are kept true.

## What the figures are

| Shown | Source | Meaning |
|---|---|---|
| Skickade | Scaleway, `project-consumption` → `monthly_emails_count` | Mail sent this month **in the project**, counted by the provider. Each recipient is one mail. |
| Ingår gratis | `EmailAllowanceOptions.IncludedMonthlyEmails` | The plan's free tier. Shown only when the plan Scaleway names is the configured one. |
| Beräknad kostnad före moms | computed | `max(0, sent − included) × price / 1000`, rounded **up** to the whole cent. An estimate, never the invoice. |
| Sändgränsen | `EmailAllowanceOptions.MonthlySendLimit` | The project's monthly quota, shared with the sign-in codes. Above half of it is ADR 0161 D4 trigger 1. |
| Webhooks | Scaleway, `project-consumption` → `webhooks_count` | None should exist (see the processing register). A line appears only when one does, or when the count could not be read. |

**Counted for the project, free tier per organisation.** The free tier is "300 per organization". The count is the project's.
They are the same while the organisation has one project sending through Transactional Email (measured 2026-10-19, ADR 0178).

**Not known, and never claimed on the page:**
- the instant at which the provider's month resets (UTC or Paris time);
- whether failed or cancelled sends are in the count.

The page names the month only where UTC and Stockholm agree on it; otherwise it reads "Utskick denna månad".

## Re-reading the price list

The terms are code defaults on `src/Jobbliggaren.Application/Admin/Email/EmailAllowanceOptions.cs`, with no appsettings entry.

1. Read <https://www.scaleway.com/en/pricing/managed-services/>, the "Transactional Email" table, for the plan in force. The page
   states prices before tax.
2. If the free tier, the price per 1 000 mails or the plan's name has changed, change the defaults in a PR, and set
   `PriceReadOn` to the day you read it.
3. If nothing changed, nothing is committed. The date on the page then shows how old the reading is.

**When the plan is changed in the console:** change `Plan` (and the terms) in the same PR or before it. Until then the page shows
the mismatch and computes no cost.

## The read permission

The read uses the sending key. Its IAM application must hold exactly these sets, all scoped to the project, and nothing broader:

- `TransactionalEmailEmailApiCreate`: sending (the original rule).
- `TransactionalEmailDomainReadOnly`: the original rule.
- `TransactionalEmailOfferSubscriptionReadOnly`: the read, in a rule of its own. Both `project-consumption` and
  `offer-subscriptions` need `offer_subscription:read` (measured 2026-10-19).

Never `TransactionalEmailReadOnly`, `*FullAccess`, `EmailReadOnly`, `Webhook*` or `Blocklist*`: those read recipients or open
streams the register says do not exist. After any change to the policy, send a sign-in code to your own address and check that
it arrives, because the same policy carries the sign-in codes. The dated console reading is ADR 0178 D7's.

## When the region reports a failure

| Line on the page | Reason | What to do |
|---|---|---|
| "Scaleway nekade läsningen…" | Unauthorized (401) or Forbidden (403) | A 401 means the key itself is refused, and sending is probably failing too: check the key's expiry first (`master-key-ops.md`). A 403 means the read permission is missing: check the sets above. |
| "Scaleway begränsar läsningarna…" | RateLimited (429) | Wait. The read holds the failure for the provider's `Retry-After`, at most 15 minutes. |
| "Scaleway svarade inte…" | Timeout, Unreachable, ProviderError | Transient. A lasting one: check Scaleway's status page. |
| "Scaleways svar kunde inte läsas…" | Malformed | The API's answer changed shape. A code change; the adapter is `ScalewayUsageSource`. |
| "Räknas inte i den här miljön." | NotConfigured | Expected in dev, CI and the harness: `Email:Provider` is not Scaleway. |

The plan's own lines follow the same reasons, and say the cost is not computed.

## Re-measuring from the box

A read with the production key against the provider needs **Klas's GO for each run** (CLAUDE.md §9.2). The form security-auditor
set (ADR 0178 D7):

- The key and project id are read from their files into a curl config on tmpfs (`umask 077`), never into argv or the environment.
- One GET per endpoint, `-sS --max-time 10`. The body goes to a temporary file that is never printed.
- Output on a 200: the HTTP status and the named counts only. On anything else: the error's `type` and its `details`
  resource/action names, whitelisted to `[a-z_]`. Never the body, never `project_id`.

The deployed page performs the same read. Klas reading it live is the measurement that needs no GO.

**Closing the month's reset instant.** Paris midnight comes before UTC midnight: at 23:00 UTC in winter, at 22:00 UTC in summer.
Read the page (or, with a GO, the count) three times around a month's end: shortly before Paris midnight, between Paris midnight
and UTC midnight, and shortly after UTC midnight. The read at which the count falls back names the reset. Record it in ADR 0178,
and drop the unknown from this runbook.
