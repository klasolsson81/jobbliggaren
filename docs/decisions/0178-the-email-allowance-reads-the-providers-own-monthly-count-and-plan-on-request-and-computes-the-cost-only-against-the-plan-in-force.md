# ADR 0178 — The email allowance reads the provider's own monthly count and plan on request, and computes the cost only against the plan in force

**Date:** 2026-10-19
**Status:** Accepted
**Decider:** Klas Olsson. His question of 2026-10-18 on #1981: "framförallt veta hur jag ligger till med antalet gratismail per
månad, vs kostnad". He also gave each console change and each read probe below its own GO.
`senior-cto-advisor` ruled the design before any code (`docs/reviews/2026-10-18-1981-plan-cto.md` and its addendum A–J,
`…-plan-cto-addendum.md`). `security-auditor` ruled the read key and its permission (`…-plan-security-auditor.md`, the re-check
`2026-10-19-1981-plan-security-auditor-recheck.md`, and the 403 ruling `2026-10-19-1981-probe-403-security-auditor.md`).
`dotnet-architect` and `design-reviewer` read the plan (`…-plan-architect.md`, `…-plan-design-reviewer.md`).
**Amends:** ADR 0150, by its Amendment of 2026-10-19 (D4's #1981 row is flipped in part, there is a `notConfigured` state, and
D8 gains two rows); ADR 0161 D4 trigger 1, by a pointer to its instrument.
**Related:** [#1981](https://github.com/klasolsson81/jobbliggaren/issues/1981) (the issue; this is its PR 1 of 5) ·
[#1972](https://github.com/klasolsson81/jobbliggaren/issues/1972) (the epic) · ADR 0131 (Scaleway Transactional Email replaces SES) ·
ADR 0150 (D2: honest unavailability) · ADR 0157–0159 (the admin observation conventions this follows, and departs from in D5) ·
ADR 0161 D4 (the shared monthly quota) · ADR 0154 (every merge is a deploy; what needs Klas's GO) · CLAUDE.md §9.2 (the read rule)
**Measured against:** `origin/main` at `1f9535b68`; Scaleway's public pages and API reference, read 2026-10-18 and 2026-10-19; the
box (`jp-vps`), with one read probe per GO, 2026-10-19.

> **Provenance.** Written by the driving session from the rulings above. `docs/runbooks/admin-email.md` is the one home for what
> the figure means, how the price is re-read and how the read is re-measured. This ADR does not repeat its steps.

---

## Context

The app records nothing per sent mail. The Scaleway sender never reads the response, no table holds a send, and the only line per
send is the log line with the mail's kind. The indirect traces (`user_job_ad_matches.SentAt`, the feedback tables, `audit_log`)
cannot be summed into a count: one mail covers many rows, an empty drain marks rows sent without a mail, and sign-in codes leave
no row at all. A count of our own would also start on its deployment day, in the middle of a month.

Scaleway Transactional Email, read 2026-10-18:

- **The price list** (`scaleway.com/en/pricing/managed-services/`) gives the Essential plan "300 per organization (Free tier)",
  then 0.25 EUR per 1 000 mails, before VAT. Each recipient counts as one mail.
- **The API** (`transactional-email/v1alpha1`, `fr-par`) has `project-consumption`, whose `monthly_emails_count` is "emails sent
  during the current month in the project", and `offer-subscriptions`, which names the project's plan.
- **Neither endpoint states the permission it needs.** No permission set is named for consumption.

## Decision

### D1 — The provider's count is the only source

The figure is Scaleway's own `monthly_emails_count`. It is never our own count, never shown next to one, and never reconciled with
one on the page: the provider computes the allowance and the invoice from its own number, and a model of it could only drift. The
page says the count is made by Scaleway for the project, never that it is billed.

Two facts stay unmeasured, and the page claims neither:
- the instant at which the provider's month resets (UTC or Paris time);
- whether failed or cancelled sends are in the count.

### D2 — The plan's terms are configuration with a source and a date

`EmailAllowanceOptions` holds the Essential plan's terms as **code defaults, with no appsettings entry**: the plan, 300 included
mails, 0.25 per 1 000 extra, the day the price list was read (2026-10-18), and the monthly send limit (10 000, Scaleway's
documented default). A test pins that the defaults pass validation, so a bad default fails CI rather than a production boot.

The currency (EUR) and "before VAT" are constants, because each has exactly one legal value. The price list's URL lives in the
runbook, and no URL reaches the browser.

### D3 — The cost is computed, rounded up, and labelled as an estimate

`max(0, sent − included) × price / 1000`, in `decimal`, rounded **up** to the whole cent and sent as integer cents. One extra mail
costs a fortieth of a cent, so rounding to nearest would show a cost of nothing for the first twenty. A computed 0 is a value and is
shown. An unknown cost is an en-dash, never 0 (ADR 0150 D2).

The Billing API is rejected. It needs organisation-wide `BillingReadOnly`, which is the cost of every product, and its figures are
not real-time.

### D4 — A cost only against the plan in force

`offer-subscriptions` is read for one field, `offer_name`, which is mapped onto a closed `EmailPlan`. A name this code does not
know, or anything other than exactly one subscription, is `Unknown`. "Ingår gratis" and the cost are computed only when the plan in
force is the configured one. Otherwise they are an en-dash with the cause, while the count still shows. An `Unknown` plan never
matches. A plan changed in the console is thereby visible on the page, not a cost quietly computed against the wrong terms.

`included_monthly_emails` is not consumed: the free tier is per organisation, while the field is per project. Its value was 300 on
2026-10-19.

### D5 — The read: in the Api, on request, remembered per read

This departs deliberately from ADR 0159's "the request never touches a source". There the observed state is local. Here the
provider holds it, and a sampler would call Scaleway with the key around the clock for a page that is rarely open.

- **Each read is remembered separately**, so one failing never blanks the other.
  - A success is reused for 60 s from its own `observedAt`, and never after that.
  - A failure is reused for 60 s, or for the provider's `Retry-After` when that is longer, capped at 15 min. The send path has
    no retry, so this read must not add load to a provider that is already refusing (Art. 32(1)(b): the sign-in codes share the
    quota).
  - One read is in flight at a time. It runs on `CancellationToken.None`, and each caller waits on its own token.
- **The read client is a named client of its own:**
  - a 5 s timeout, strictly shorter than the web's 10 s per-source deadline;
  - no retry and no resilience handler;
  - **no loggers**. Measured: without `RemoveAllLoggers`, the client factory's default loggers write the transport exception's
    message.
  - The send client is untouched.
- **What the adapter reads:**
  - it never reads the body of a refusal;
  - it reads a 2xx body only up to 64 KiB, and from it only the named fields; `project_id` and `organization_id` are skipped and
    never become a value;
  - a missing or negative count is `Malformed`, never 0;
  - its own log line names the read, the reason and the status, never the URI, the body or a message.

### D6 — Time and month

`X-Admin-Sampled-At` is the API's read instant, as for every admin source. When the provider answered is `observedAt` in the body,
and a reused answer keeps its own instant.

The DTO names the month only when UTC and Stockholm agree on it. Stockholm keeps Paris's offset all year, so the name is never
wrong under either candidate reset instant. For the first hour or two of each month there is no name, and the page reads "Utskick
denna månad".

### D7 — The key and its one added permission

The read uses the existing sending key (security-auditor's D4 (a)): the read carries no personal data, and a leaked sending key's
worst case is unchanged. It can still send as the domain.

The key must never read mail objects. A later read of mail objects (#1981 PR 3) gets a read application of its own, injected into
the Api only, and this permission moves to it.

**The console reading** was made by Klas on 2026-10-19 and transcribed by the driving session from his screenshots. Only names are
recorded here, never an id.

- **Before.** The IAM application `Jobbliggaren` had no groups and one policy, `jobbliggaren-tem-send`, with one rule:
  `TransactionalEmailEmailApiCreate` and `TransactionalEmailDomainReadOnly`, scoped to the project `Jobbliggaren`. The organisation
  has that one project.
- **Probe 1** (Klas's GO; one GET per endpoint from the box, printing only status and named fields): `project-consumption` 403,
  `offer-subscriptions` 403.
- **Rule #2 added** with `TransactionalEmailProjectSettingsReadOnly`, scoped to the project, followed by a delivery check to Klas's
  own address. Probe 3 still answered 403 on both.
- **Probe 4, diagnostic.** On a non-200 it printed only the error's `type` and its `details` resource and action, whitelisted to
  `[a-z_]`. Both endpoints answered `permissions_denied`, resource `offer_subscription`, action `read`.
- **Rule #2 swapped** to `TransactionalEmailOfferSubscriptionReadOnly`, followed by a delivery check.
- **Probe 5:** both endpoints answered 200.
- **After.** `TransactionalEmailEmailApiCreate`, `TransactionalEmailDomainReadOnly` and
  `TransactionalEmailOfferSubscriptionReadOnly`, all scoped to the project `Jobbliggaren`. `ProjectSettingsReadOnly` is gone.

Probe 2 measured nothing new: it ran before rule #2 had been saved.

### D8 — Webhooks: counted, not built

`project-consumption` also carries `webhooks_count`, at no extra cost. The page shows a deviation line when it is above 0, and a
quiet line when it could not be read: the register says no webhook exists, and a provider-side registration changes no line of
our code. `custom_blocklists_count` is not read.

Webhooks themselves are not built. They arrive only through Topics and Events, which would need public ingress to the API, and every
payload carries `email_to`.

### D9 — The send limit, and ADR 0161's trigger 1

The DTO carries the configured monthly send limit. When the month's count is above half of it, the page says so in words, with no
percentage. That gives ADR 0161 D4 trigger 1 ("monthly sent volume above 50 % of the shared quota") an instrument. The half is the
calculator's, and its docblock points at the trigger.

### D10 — Composed in the Api only

- `AddInfrastructure`, which only the Api calls, registers the source. The Worker calls `AddEmailSender` alone and never reads the
  provider; an architecture test pins that.
- Where `Email:Provider` is not Scaleway, the source answers "not configured", a state rather than a failure.
- `EmailProviderSelection` is the one reading of `Email:Provider`, shared with the sender, and a parity test pins it.

## Alternatives considered

- **Our own send log as the source.** Rejected for this question. It starts mid-month, needs a migration, sees one send path, and
  would disagree with the invoice. It returns in PR 4 for counts per mail kind, never as the allowance figure.
- **The Billing API for the cost.** Rejected in D3.
- **`TransactionalEmailReadOnly` for the read.** Never: it carries `EmailReadOnly`, which reads every recipient.
  security-auditor does not sign it.
- **A hosted sampler.** Rejected in D5.
- **Committed appsettings for the terms.** Rejected in D2: it gives the terms a second home, and a typo would fail a production boot.

## Consequences

### Positive

- The page answers Klas's question from the number the invoice is computed from, and says when and why it cannot.
- The key reads two counts and a plan name, and nothing personal.
- A plan changed in the console shows on the page instead of quietly invalidating a cost.

### Negative and risks

- **The free tier is per organisation, while the count is per project.** This is exact while the organisation has one project
  that sends (measured 2026-10-19). Lapse trigger 3 below.
- **The price is a dated reading of a web page.** It does not update itself. Lapse trigger 2 below.
- **The month's reset instant and the count's treatment of failed sends are unmeasured** (D1). They are closed by reading the
  count around the first month boundary after deployment (runbook).

## Lapse triggers

Home: this ADR. Reader: Klas.

1. **The plan is changed in the console.** Change `EmailAllowanceOptions` with it. The page shows the mismatch meanwhile (D4).
2. **Scaleway's price list changes** (price, free tier or plan names). Re-read it and update the terms and `PriceReadOn`.
3. **A second project in the organisation sends through Transactional Email.** The count no longer covers the free tier's whole
   population.
4. **A PR needs to read mail objects.** That read gets its own application (D7), and `TransactionalEmailOfferSubscriptionReadOnly`
   moves with it.

## Implementation

**Application** (`Admin/Email/`):
- `EmailAllowanceOptions`, `EmailAllowanceTerms`, `EmailAllowanceLimits`, `EmailAllowanceCalculator` and `EmailAllowanceEvaluator`;
- the port `IEmailProviderUsageSource`, with the closed reads `EmailProviderUsageRead` and `EmailProviderPlanRead`;
- `GetEmailAllowanceQuery` (`IAdminRequest`) and `EmailAllowanceDto`.

**Infrastructure:**
- `Admin/Email/`: `ScalewayUsageSource`, `MemoizedEmailProviderUsageSource`, `NotConfiguredEmailProviderUsageSource` and
  `EmailUsageSourceCollectionExtensions`;
- `Email/`: `EmailProviderSelection`, and `ScalewayClientRegistration.AddScalewayUsageClient`.

**Api:** `AdminEmailEndpoints` (`GET /api/v1/admin/email/allowance`). The admin no-store middleware now also covers
`/api/v1/admin/email`.

**Web:** the allowance region on `/admin/e-post` and the overview's e-post card (ADR 0150 Amendment 2026-10-19).

## References

- Scaleway, Transactional Email pricing (`scaleway.com/en/pricing/managed-services/`), read 2026-10-18.
- Scaleway, Transactional Email API v1alpha1 (`scaleway.com/en/developers/api/transactional-email/`), read 2026-10-18 and 2026-10-19.
- Scaleway, IAM permission sets (`scaleway.com/en/docs/iam/reference-content/permission-sets/`, "Reviewed on March 20, 2026"),
  read 2026-10-19.
- Scaleway, TEM capabilities and limits (reviewed 2025-10-29), read 2026-10-11 and 2026-10-18.
- `docs/runbooks/admin-email.md`.
