# ADR 0179 — Provider delivery is read with a read key of its own: totals per period, the recent failures with a masked recipient, and a canary against a silent zero

**Date:** 2026-10-19
**Status:** Accepted
**Decider:** Klas Olsson. His prompt of 2026-10-19 asked for delivery totals per period on `/admin/e-post`, "utan någon
mottagare". Once the measurement below showed that no provider total exists without a key that reaches recipients, he
asked "Vad krävs för att lösa problemet, istället för att skjuta på det?" and chose "Ja, kör nya PR 2". He gave each
console change and each read probe its own GO.
`senior-cto-advisor` re-cut #1981's plan of record and ruled the PR's scope (`docs/reviews/2026-10-19-1981-pr2-route-cto.md`,
`…-pr2-plan-cto.md`). `security-auditor` ruled the key (`2026-10-19-1981-pr2-statistics-security-auditor.md`: route A2;
`…-pr2-plan-security-auditor.md` with its re-check; `…-pr2-probe9-security-auditor.md`). `dotnet-architect` and
`design-reviewer` read the plan (`…-pr2-plan-architect.md` with its re-check, `…-pr2-plan-design-reviewer.md`).
**Amends:** ADR 0178, by its Amendment of 2026-10-19 (trigger 4 fires; the allowance reads with this key); ADR 0150, by its
Amendment of 2026-10-19 (#1981 PR 2).
**Related:** [#1981](https://github.com/klasolsson81/jobbliggaren/issues/1981) (PR 2 of 4 after the re-cut) · ADR 0178 ·
ADR 0150 D2 · ADR 0131 (Scaleway Transactional Email) · ADR 0133 (the TEM retention question) · ADR 0154
**Measured against:** `origin/main` `c49b4d4f2`; Scaleway's API reference v1.9008.0 and its IAM permission-set page
("Reviewed on March 20, 2026"), read 2026-10-19; the box (`jp-vps`), probes 6–9, each with Klas's GO, 2026-10-19.

> **Provenance.** Written by the driving session from the rulings above. `docs/runbooks/admin-email.md` is the one home for
> what the figures mean and how the key is placed, rotated and re-measured.

---

## Context

PR 1 (ADR 0178) reads the month's count and the plan with the sending key, which holds `TransactionalEmailEmailApiCreate`,
`TransactionalEmailDomainReadOnly` and `TransactionalEmailOfferSubscriptionReadOnly`. PR 2 was to add delivery totals per
period from TEM `statistics`, which answers six counts by mail creation time and names no permission.

**Measured with the sending key (probes 6–8, 2026-10-19):**

| Read | Answer |
|---|---|
| `statistics`: last 7 days, month to date, no window, by `domain_id`, no parameters | HTTP 200 each time, all six counts 0 |
| `project-consumption` (control) | `monthly_emails_count` 146 |
| `domains`, the domain object's own counters | total 163, sent 163, failed 0, canceled 0; no documented window, no period parameter |

`statistics` does not refuse a key it will not count for: it answers 200 with zeros. The reading is that it counts only
the mails the caller may read (`email:read`, the set `TransactionalEmailEmailReadOnly`). That reading was not proven
with the sending key, because proving it needs the very set ADR 0178 D7 bars from that key. A zero from the sending key
is therefore not a measurement.

Klas's prompt said "utan någon mottagare". That describes what the page shows. It cannot describe the key: no provider
total exists without a key that reaches every recipient.

## Decision

### D1 — A read application of its own, justified by the failures read

The IAM application `jobbliggaren-tem-read` holds one policy, `jobbliggaren-tem-read`, with one rule:
`TransactionalEmailEmailReadOnly` and `TransactionalEmailOfferSubscriptionReadOnly`, scoped to the project Jobbliggaren,
without conditions. It has exactly one API key, which expires 2027-10-19 (Klas's reading). The key is injected into the
api only (D7). The console reading was made by Klas on 2026-10-19 and transcribed from his screenshots, names only. A
first attempt selected `TransactionalEmailReadOnly` by mistake; it was removed before the policy was saved.

The key's only justification is the recent-failures read (D5), the purpose "leveransuppföljning". The totals (D3) and the
allowance (ADR 0178, by its amendment) ride a key that exists anyway; neither is ever on its own a reason to keep
`TransactionalEmailEmailReadOnly` (lapse trigger 1). security-auditor does not sign a recipient-reaching key for the
totals alone.

The sending application `Jobbliggaren` (policy `jobbliggaren-tem-send`) keeps `TransactionalEmailEmailApiCreate` and
`TransactionalEmailDomainReadOnly`. `TransactionalEmailOfferSubscriptionReadOnly` is removed from it in the same sitting
as the read key's injection (G5 in the runbook), after which nothing reads with the sending key.

### D2 — What the read carries, and what the key reaches

The page receives counts, and for each recent failure a time, a closed reason and a masked recipient. It never receives a
subject, a provider message, an id or a full address.

The key reaches more. Measured on 2026-10-19 (probe 9): an `Email` object, from the list and from `emails/{id}`, carries
exactly `created_at, flags, id, last_tries, mail_from, mail_rcpt, message_id, project_id, rcpt_to, rcpt_type, status,
status_details, subject, try_count, updated_at`, and a try carries `code, message, rank, tried_at`. No read endpoint in the
API reference returns a body, headers, attachments or MIME. So the read API returns no content field (security-auditor's
4b). That is not a statement that the provider holds no content.

`subject` is reachable. Today every subject is a fixed string or a choice among fixed strings, and none carries a code,
link or token. A test pins that no template whose body carries a code or link carries it in the subject; that test is
this ADR's instrument for 4b on our side.

### D3 — Totals per period from `statistics`

The periods are rolling: the last 24 hours, 72 hours and 168 hours, with `since` computed on the server from the period.
The client chooses a period token (`h24`, `d3`, `d7`) and nothing else. Only `project_id` and `since` are sent. All six
counts are read and must be present as uint32, else the read is Malformed; only `sent` and `failed` cross the port, and
`total` serves the canary (D4).

Measured with the read key (probe 9, 2026-10-19, about 19:00 UTC): the month to date read 149, equal to
`project-consumption`'s `monthly_emails_count` read with the same key; 32 days 162; 7 days 60; 3 days 16; 24 hours 12;
failed 0 in every window. A 20-minute window around a sign-in code Klas had sent within the previous five minutes read
**0**. The control window was not met. Two causes fit and neither is measured: `statistics` lags real time, or `since` is
compared against time buckets so that the current bucket falls outside a short window. A wrong box clock is excluded:
its NTP status read synchronised at 19:15 UTC, to the second of the session's clock. So the page never claims that a
period counts up to the moment it was read, nor that its edges are exact to the minute; the time shown is when we asked.

### D4 — The canary: a silent zero is not shown as zero

A second `statistics` read over the last 32 days is the sentinel. If its total is 0, every delivery figure is
NotObservable, never 0. Totals: a period read that is Observed beside a sentinel total ≥ 1 is shown; beside a sentinel
of 0 it is NotObservable; beside a failed sentinel it takes the sentinel's failure. The recent-failures list is shown as
empty only when the sentinel is ≥ 1 and the same period's statistics are Observed with `failed` 0; a `failed` above 0
beside an empty list is NotObservable.

32 days covers the longest provider month under either candidate reset instant (ADR 0178 D1) with margin. For a key that
reads 0 in every window, which is the threat the canary exists for, this predicate covers everything a
month-against-`monthly_emails_count` comparison would, plus the start of each month, when that count is 0 before the first
send. For arbitrary inputs it is not a superset of that comparison: a key that saw older sends but not newer ones could
pass it. A project-scoped permission does not produce such a key. Its cost is that a project with no mail in 32 days shows
an en-dash instead of 0.

### D5 — The recent failures

One page of at most ten: `statuses=failed`, `since` from the period, `page_size=10`, `order_by=created_at_desc`. Never
`mail_rcpt`, `search`, `mail_from` or `message_id`. The adapter reads, per item, only `status`, `created_at`, `flags` and
`mail_rcpt`:
- a `status` other than `failed`, an unreadable or too-early `created_at`, an order that is not non-increasing, or more
  than ten items makes the **whole** read Malformed, so a provider that ignored a parameter is never shown as failures;
- `mail_rcpt` is masked at once to the first letter or digit of the local part, `•••`, `@` and the whole domain; anything it
  cannot mask safely becomes `•••`. This is pseudonymisation; for a personal domain the masked form can still identify;
- `flags` map to a closed reason (MailboxNotFound, HardBounce, MailboxFull, SoftBounce, Spam, Blocklisted, Other), the most
  severe first in that order;
- `rcpt_to`, `subject`, `last_tries`, `status_details` and the ids never become values.

The body is read into a fresh, non-pooled buffer of at most 64 KiB; a larger answer is Malformed, never a shortened list.
The derivation (2026-10-19): one delivered item with one try is about 657 bytes, so ten are about 6.6 KB. No failed item
was measured, because none existed. A failed item carries up to three tries, each with the receiving server's reply,
which RFC 5321 bounds per line but not per reply; an estimate is 20–25 KB for ten. The margin is therefore about three,
not ten, and an over-cap answer fails closed. The cap is re-derived if a later measurement (lapse trigger 3) finds a
failed item.

Nothing is stored. The masked list is unreferenced by the API 60 s after the provider's answer: its memo entry is evicted
then. That is the property the code holds; runtime buffer pools and garbage collection are not claimed.

### D6 — A missing read key is a state of its own

On a deployment whose `Email:Provider` is Scaleway but which has no read key, every provider read — the allowance's
included — answers ReadKeyMissing: "the key is not installed", with its own line on the page. It is not NotConfigured,
which means "this deployment has no provider to ask" and has no remedy. A blank key, or a read key equal to the sending
key, fails registration and names the configuration key, never a value. There is no fallback to the sending key.

### D7 — The key reaches the api alone

The secret file lives in `/run/jobbliggaren/api-secrets`, a host directory that is bind-mounted read-only into the api
service and no other, not into the shared secrets directory the worker also mounts. The pointer
`Email__ScalewayRead__SecretKey_FILE` sits under the api service only and defaults to empty. A box where the key was
never placed starts the api as before: the directory is created empty and closed (0700 root) by the reconcile script
before any apply, by the tmpfiles rule at boot, and by the injection script, which opens it to the api's group only when
it places the key. The host sampler cannot reach it. The key is injected
over the operator's own terminal (`read -rs`) and never passes through a session. Its expiry is recorded in
`master-key-ops.md` only, not on the alarm surface: a dead read key answers 401, which the page shows as its own closed
line to its only reader.

### D8 — Two endpoints

`GET /api/v1/admin/email/delivery?period=` carries counts; `GET /api/v1/admin/email/failures?period=` carries the masked
list. The overview's attention card reads the 24-hour totals only and never the list, so no masked address reaches a
surface that renders none.

### D9 — How far back the key reaches

Measured 2026-10-19 with the read key (probe 9): the list carried 166 sends, and the oldest was created before 2026-08-17
(the arm was activated 2026-08-16). The processor had thus kept recipient-bearing metadata for more than 63 days. That is
a lower bound. How long the processor keeps the metadata and the content is unmeasured and owed, not accepted; it is
Klas's open question under ADR 0133, in a session of its own.

## Alternatives considered

- **`statistics` with the sending key.** Rejected: it answers 200 with zeros (Context).
- **The read key for the totals alone.** Rejected: a key that reaches every recipient for six counts fails necessity
  (Art. 5(1)(c), 25(2)); security-auditor does not sign it.
- **Cockpit metrics through a Query token.** Rejected: the token also reads logs and traces, the TEM dashboards carry
  per-recipient panels, the metric names are unpublished, and it is a second product.
- **The domain object's counters.** Rejected: no period and no documented window, so any period would be invented.
- **Our own send aggregates as the totals.** Rejected: they count what we handed over, not delivery, and would put a second
  authority under the allowance's "Skickade" (ADR 0178 D1). They remain #1981's next PR, for counts per mail kind.
- **`TransactionalEmailReadOnly`.** Never: it is the union of every read set.

## Consequences

### Positive
- Leverans shows the provider's own sent and failed counts per period, and the failures with a reason, without any figure
  that could be a silent zero.
- The sending key returns to send-only.

### Negative and risks
- **A leaked read key** reaches address, subject, delivery status and delivery tries for sends to all three recipient
  classes, back at least to the arm's activation (D9).
- **The periods' edges are approximate** (D3).
- **The allowance depends on the read key** (ADR 0178 Amendment): a deployment without it shows ReadKeyMissing there too.

## Lapse triggers

Home: this ADR. Reader: Klas.

1. **The recent-failures read is removed.** `TransactionalEmailEmailReadOnly` is withdrawn, or the read application is
   deleted and the allowance moves to an application holding only `TransactionalEmailOfferSubscriptionReadOnly`.
   `statistics` then answers ReadKeyMissing or NotConfigured.
2. **4b changes.** A read endpoint starts returning content, or a template puts a code, link or token in its subject.
3. **The read key is rotated.** Re-read the API reference for content-bearing reads, re-run the schema probe in the
   runbook's form, and re-derive the cap if a failed item is seen.
4. **The provider's retention is decided** (ADR 0133 leg (c), Klas's own session). The residual risk above changes with it.

## Implementation

**Application** (`Admin/Email/`): `EmailDeliveryPeriod` and its tokens, `EmailDeliveryLimits`, `MaskedEmailAddress`,
`EmailFailureReason`, the ports `IEmailDeliveryStatisticsSource` and `IEmailRecentFailuresSource` with their closed reads,
`EmailDeliveryEvaluator`, `GetEmailDeliveryQuery` and `GetEmailRecentFailuresQuery` with validators, and their DTOs; the
`ReadKeyMissing` case on PR 1's reads and status.

**Infrastructure** (`Admin/Email/`): `ScalewayReadOptions` and the read-key selection, `ScalewayReadTransport`,
`ReadMemo` with opt-in eviction and its keyed form, `ScalewayDeliveryStatisticsSource` and `ScalewayRecentFailuresSource`
with their memos, and `NotConfiguredEmailProviderReads` and `ReadKeyMissingEmailProviderReads`, registered in
`AddEmailUsageSource`.

**Api:** `AdminEmailEndpoints` (`/email/delivery`, `/email/failures`).

**Deploy:** the api-only secrets directory, its mount and its three creators (reconcile, tmpfiles, injection), the pointer
under the api service, the inject script's read-key entry and `--check`, `.env.example`, and the host sampler's
`InaccessiblePaths`.

**Tests:** a pin that no template carrying a code or link puts it in the subject (D2).

**Web:** the Leverans section on `/admin/e-post` and the overview's attention line (ADR 0150 Amendment).

## References

- Scaleway, Transactional Email API v1alpha1 (`scaleway.com/en/developers/api/transactional-email/`), v1.9008.0, read
  2026-10-19.
- Scaleway, IAM permission sets (`scaleway.com/en/docs/iam/reference-content/permission-sets/`, "Reviewed on March 20,
  2026"), read 2026-10-19.
- Scaleway, TEM monitoring with Cockpit (validation 2026-01-21) and Cockpit tokens ("Reviewed on July 22, 2025"), read
  2026-10-19.
- RFC 5321 §4.5.3.1.5 (reply line length).
- `docs/runbooks/admin-email.md`.
