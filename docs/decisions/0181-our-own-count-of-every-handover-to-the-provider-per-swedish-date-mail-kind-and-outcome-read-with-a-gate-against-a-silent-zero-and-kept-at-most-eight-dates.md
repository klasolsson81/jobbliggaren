# ADR 0181 — Our own count of every handover to the provider, per Swedish date, mail kind and outcome: counted in the send funnel without ever changing the send, read with a gate against a silent zero, and kept at most eight dates

**Date:** 2026-10-23
**Status:** Accepted
**Decider:** Klas Olsson. His prompt of 2026-10-23 asked for "egna antal per mejltyp (skickade, fel, senaste fel) per period, ur
egna dagliga aggregat utan mottagare". He gave the migration GO for exactly `AddEmailSendDailyCounts` ("Ja, automerge (Rek.)"),
approved the privacy-policy text ("Ja, som den står (Rek.)"), and decided that this PR shares `hotspot:di` and `hotspot:i18n`
with #2198 on files that do not overlap ("Dela nu, disjunkta filer"). `senior-cto-advisor` ruled the plan
(`docs/reviews/2026-10-23-1981-pr3-plan-cto.md`). `security-auditor` set the binding conditions: 5a–f on 2026-10-18, then A–K
and conditions 1–7 on 2026-10-23. `dotnet-architect`, `db-migration-writer` and `design-reviewer` read the plan.
**Amends:** ADR 0150, by its Amendment 2026-10-23 (3), and ADR 0179 in two sentences (D1, D5).
**Related:** [#1981](https://github.com/klasolsson81/jobbliggaren/issues/1981) (PR 3 of 4) · ADR 0045 · 0120 · 0124 · 0150 · 0172 ·
0178 · 0179
**Measured against:** `origin/main` `b7c971c01` for the plan, read 2026-10-23. The branch is cut from `f459e87ba`.

> **Provenance.** Written by the driving session from the rulings above. `docs/runbooks/admin-email.md` is the one home for what
> the figures mean and how they are read on the box.

---

## Context

After #1981 PR 2, Leverans on `/admin/e-post` shows the provider's sent and failed counts per period and the recent failures
(ADR 0179). Its third region, "Utskick per mejltyp", still said "Kommer snart". No provider read can fill it: ADR 0179 D2
reads neither a subject nor a mail kind. So the kind has to be counted where it is known, in our own send path. That is the
fourth item of #1981's plan of record: own daily aggregates, with a migration.

Measured at pickup (2026-10-23):
- `IEmailSender` has seven send methods. Ten call sites in the Api and the Worker use them.
- Three arms exist, each a singleton from `AddEmailSender`. Only the Scaleway arm hands anything to a provider, and it does so
  through one funnel, `ScalewayEmailSender.SendAsync`. Null "succeeds" by dropping the mail.
- Templates render before the funnel.
- The HTTP status is visible only inside the funnel.
- Feedback notices are the only mails re-sent after a refusal, so the honest unit is the attempt, not the mail.
- A decorator registered in `AddEmailSender` would break five registration-by-type pins. `ApiFactory` would drop it, and it
  would wrap Null.
- The handoff's "Senaste fel" showed the provider's reply text, which condition 5a and ADR 0150 D8 bar.

## Decision

### D1 — What is counted, and where

**Every attempt that enters the Scaleway funnel is counted once**, by kind and outcome, into the bucket of the Swedish civil
date. The count runs **after** the funnel's try/catch:
- the outcome is set inside it;
- the tally runs outside it;
- a failure is thrown only after the count.

So no fault in the count can reach the catch and turn an accepted mail into a failure, or replace the failure with a foreign
exception (condition 5d, ADR 0124).

**Not counted, by construction:**
- a template fault, which happens before the funnel and hands nothing over;
- a cancellation the caller asked for, which leaves through the funnel's filter (shutdown only);
- every mail held back before the sender: no recipient, a closed gate, a spent budget (D9);
- the Console and Null arms, which register no tally.

### D2 — The vocabularies

`EmailKind` (in `Application.Common.Abstractions`) has one member per send method, numbered 1–7, and its tokens are the strings
the senders already logged. Every sender takes its token from it. `EmailSendOutcome` is the class the funnel saw:

| Value | Outcome | Seen as | Disposition |
|---|---|---|---|
| 1 | Accepted | 2xx | — |
| 2 | Refused | 4xx other than 429, our own 401/403 included | NotAccepted |
| 3 | RateLimited | 429 | NotAccepted |
| 4 | Unreachable | name resolution, connection, TLS or proxy failed | NotAccepted |
| 5 | ProviderError | 5xx | Unknown |
| 6 | Timeout | the client's own timeout | Unknown |
| 7 | Other | anything else | Unknown |

One pure, total classifier, `OutcomeOf`, sets the outcome, and the disposition the callers act on follows from it unchanged.
No status code, reason phrase, exception type or message is stored, and no label may lay a `Refused` on the recipient's
address (Art. 5(1)(d)).

The numbers are stored, so a member is never renumbered. Kinds are not split: splitting `LoginChallenge` would turn its variants
into daily counts of an account state, which goes back to security-auditor.

### D3 — The table and the recording start

The migration is `AddEmailSendDailyCounts` (AppDbContext), exactly the DDL of Klas's GO.

`email_send_daily_counts`:
- columns `day date`, `kind smallint`, `outcome smallint`, `attempts integer`;
- primary key `(day, kind, outcome)`;
- one positivity check;
- no other index.

`email_send_counting` holds one row, `started_at`, which `Up` writes with the database clock.

**What the rows hold:**
- No recipient, user, time of day or text column. A `smallint` cannot carry text, so the model's text sweeps catch any text
  column added later.
- The day is a date in Europe/Stockholm, never a time of day. It is taken from the server clock inside the tally, never from the
  caller, and the tally's signature carries no time.
- No `updated_at` or `last_*` column exists in either table: a "last written" stamp would be a minute stamp per send.

**The recording start.** No code writes the start row after the migration; an IL pin holds that. The api and the worker wait for
`migrate` to complete (`service_completed_successfully`), so no new-code send precedes the start, and older code never touches
the tables. Its gap of a few seconds is named.

**Rollback residual.** A rollback across this migration runs older code under `MIGRATE_ALLOW_SCHEMA_AHEAD`, and that code
counts nothing. After the roll-forward, the dates in between would read as zeros under a start that predates them. Klas then
re-takes the start with a box write under his GO (lapse trigger 7).

### D4 — The count never changes the send

`EmailSendTally` is a Template Method base. Its only entry, the non-virtual
`ValueTask RecordAsync(EmailKind, EmailSendOutcome)`, cannot throw:
- One catch-all covers everything: creating the scope, resolution, the clock and calendar, the budget, the SQL and the
  disposal.
- Its own timer bounds it at **400 ms** (`WaitAsync`), whether or not the write honours its token.
- An abandoned write is observed and its token source disposed after it.

The 400 ms budget is ADR 0045 class (c). The count sits inside the delivery spine, between the provider's answer and the
caller's bookkeeping.

`SqlEmailSendTally` upserts the day's row:
- `INSERT … ON CONFLICT (day, kind, outcome) DO UPDATE SET attempts = t.attempts + 1`.
- It runs in a scope, and so a connection, of its own, so a failed count cannot abort a transaction the caller holds (25P02).
- The Api and the Worker increment the same row atomically.

A count not confirmed within the budget logs one Warning (event 3009) with:
- the kind token;
- the outcome;
- the exception's type name;
- a SQLSTATE, read only from `PostgresException`, with a fixed placeholder otherwise.

The exception object is never handed to the logger. The Warning means "**not confirmed**": an abandoned write may still have
counted. A synchronous block before the write's first await, and a logging provider that throws, are named residuals.

### D5 — The read

The read reaches every whole Swedish date the rolling period touches, from `FirstDateOf(period) = DayOf(now − length)` through
today. That is a superset, so an attempt inside the period is never missed, and the page names the first date. The page never
states a date count as an equality; on the night daylight saving changes, "24 tim" can touch three dates.

**Per kind**, the region shows:
- the attempts the provider accepted;
- the failed attempts;
- the latest date with a failure, with every class seen that date in a fixed display order. A daily count does not know which
  failure came last, so none is singled out.

**Coverage** is Complete when counting began no later than the first date's start in Stockholm. Otherwise it is Partial, and the
figures are exact from the recording start, which the page names.

**States:** Observed, NotObserved (this deployment hands no mail to a provider) and NotObservable (D6).

**Failure modes.** A database failure is an exception. So is a stored value this release does not know, or a missing start: a
read that counted around either would understate what it shows.

**Registration.** The read is Api-only (`AddEmailSendCountsSource`) and chooses on the sender's provider switch alone. The read
key plays no part, because these counts are ours.

### D6 — The gate against a silent zero

A count that fails to write is contained, so a zero cannot by itself be told from a period without mail. When our total for the
period is zero and the provider's own read of the same period is Observed with at least one sent mail, the region is
**NotObservable** instead of zero.

**Why it is sound:**
- The provider's figure is only a witness. It is never shown, subtracted or summed, which is the line ADR 0178 D1 draws for the
  allowance ("never reconciled with one on the page").
- Our dates cover the provider's whole window.
- The handler asks for that read only on a zero, never for the sentinel, and the read reuses the delivery memo.
- It is evaluated per request and never stored, and no number is logged.

**Named residuals:**
- **(a)** Without a read key the gate is inert. Leverans says so itself.
- **(b)** The provider's window can begin up to about 61 seconds before ours: the memo's 60 s reuse plus rounding to the whole
  second. A send in that span, at a date boundary, gives a false NotObservable, which errs safe.
- **(c)** It catches a total loss, never a partial one.

A test send from the Scaleway console, or the time between a deploy and the first handover after it, can also trigger the
gate. Both err safe.

### D7 — Retention

`EmailSendCountRetentionJob` deletes every count dated before `FirstDateOf(Last7Days)`. That is the rule the read uses, so a
deletion never takes a date a read at the same or a later instant needs.

**The schedule.** It runs daily at **04:40 UTC**, outside the hour in which daylight saving moves the Swedish date. Each scheduled
run therefore keeps exactly today and the seven dates before it. A count lives **at most 8 Swedish dates plus the time to the
next successful scheduled run**, and a failed run leaves the rows for the next one.

**A run triggered by hand.** An administrator can trigger the job at any time, and then it keeps whatever `FirstDateOf` gives at
that instant:
- between 00:00 and 01:00 local time in the spring week, one date more than a scheduled run keeps;
- between 23:00 and 24:00 in the autumn week, one date fewer.

Both are what a read at that instant needs.

The start row is never deleted. Backups follow the processing register's backup entry.

### D8 — Personal data, and where the counts belong

- **No identifiers, but linkable.** In security-auditor's words: "inga identifierare; små celler kan kopplas via våra egna poster
  per person och behandlas som personuppgift under syfte 3". A day's count of one rare kind (a feedback reply, an address
  change) can be linked to records we already hold per person, and each such record already holds the same fact more finely.
  The counts are therefore personal data.
- **Purpose and basis.** They belong to purpose 3, Leveransuppföljning, on Art. 6(1)(f), with the same interest. Art. 11(1)–(2)
  applies to access and erasure: no cell needs to be linked to a person to answer a request, and every cell is gone within
  eight dates.
- **Not a send log.** The counts are never a send log in the sense of CLAUDE.md §9.6 (3)(ii), nor a measurement of "no reached
  bearer" or of ADR 0133's bound: a count is not a set of recipients.
- **Reading on the box.** The table's rows are §9.2 (b). `admin-email.md` lists the few readings that are (a).
- **Privacy policy.** The point "Uppföljning av e-postleverans" says what we count. "Hur länge vi sparar uppgifter" says the counts
  are deleted no later than eight days after the day they concern. Klas approved both texts on 2026-10-23.

### D9 — "No recipient" is not observable from the send chain

Measured 2026-10-23 at `b7c971c01`: every held-back mail returns before the sender, so the send chain cannot count it. The page
keeps the "Utan mottagare" row removed (ADR 0150 D8, 2026-10-19).

| # | Call site | Kind | Host | Held back before the send when |
|---|---|---|---|---|
| 1 | `LoginChallengeIssuer.cs:80` | login-challenge | Api | record-only, the unknown-address cap, or `LoginChallengeAdmission`'s cooldown and budget |
| 2 | `RequestReauthenticationChallengeCommandHandler.cs:82` | login-challenge | Api | the sender cannot deliver |
| 3 | `ChangeEmailCommandHandler.cs:78` | login-challenge | Api | the sender cannot deliver |
| 4 | `RequestAccountEmailChangeCommandHandler.cs:88-91` | account-email-change-requested-notification, then login-challenge | Api | the sender cannot deliver |
| 5 | `ConfirmedAddressSwap.cs:46` | email-changed-notification | Api | — |
| 6 | `SendFeedbackReplyCommandHandler.cs:144` | feedback-reply | Api | the reporter cannot be reached, or the sender cannot deliver |
| 7 | `TopMatchMailDispatcher.cs:191` | match-notification | Worker | the mail gate is closed, or there is no address |
| 8 | `DigestDispatchJob.cs:271` | match-notification | Worker | no address, or nothing to present |
| 9 | `DigestDispatchJob.cs:554` | followed-company-notification | Worker | no address, or nothing to present |
| 10 | `FeedbackNotificationDispatchJob.cs:130` | feedback-received-notification | Worker | `FeedbackGate` is not Open (NoRecipient, Disabled, CannotDeliver) |

## Alternatives considered

- **A decorator over `IEmailSender`.** Rejected. It wraps Null's drop and sees no HTTP status. It breaks five registration pins,
  and `ApiFactory` drops it.
- **Three outcomes (Accepted, NotAccepted, Unknown).** Rejected. "Senaste fel" stands in for the provider's reply text, and three
  values say only "refused" or "unknown". They also lose distinctions that call for different action: wait (429), the key or the
  configuration (4xx), the network (connection).
- **UTC dates.** Rejected. The page would have to write "UTC" beside every date; the Swedish date has the same coarseness (5b).
- **Hourly buckets.** Rejected. At about five mails a day, hour × kind cells are mostly 0 or 1, which is a row per send with an
  hour stamp.
- **A start written by the first counted send.** Rejected. It turns a system time into the minute of one identifiable send.
- **A `RetainedDays` constant beside the read's window.** Rejected. Two formulas for one invariant drift apart around daylight
  saving.
- **No gate, or a canary over our own rows only.** Rejected. The first ships a possible "0 fel" on the page meant to reveal
  failures; the second would need eight days of total loss before it fires.
- **A 2 s budget.** Rejected. A longer stall inside the delivery spine widens the window in which a claimed row strands.

## Consequences

### Positive

- "Utskick per mejltyp" shows real figures, clearly labelled as our own count of handovers, never as the provider's delivery.
- A dead sending key or an unreachable provider now shows per kind, with its class.
- The count can neither fail a send nor change what the caller sees.

### Negative and risks

- A count not confirmed within the budget may be lost. The Warning makes it visible, and the gate catches only a total loss.
- Small cells are personal data (D8); retention and Art. 11 bound that.
- Two figures named "skickade"/"överlämnade" sit in one section. The page keeps them in separate regions with their own
  attribution (ADR 0150 Amendment (3)).

## Lapse triggers

Home: this ADR. Reader: Klas. Each trigger that names a reviewer goes back to that reviewer.

1. A new mail kind, a split kind, or a finer bucket than the Swedish date: security-auditor.
2. A longer period, a changed date function, or a changed retention schedule: security-auditor.
3. A second provider arm that hands mail over.
4. A row per send, or any column holding a time of day: security-auditor.
5. Any use of the provider's figure beyond the ≥ 1 witness of D6: senior-cto-advisor.
6. Warning 3009 recurring: move the write behind a bounded channel (ADR 0172 trigger 2, ADR 0142 D2).
7. A rollback across `AddEmailSendDailyCounts` under `MIGRATE_ALLOW_SCHEMA_AHEAD`: Klas re-takes the start with a box write under
   his GO.

## Implementation

| Area | What |
|---|---|
| Application | `EmailKind` and `EmailSendOutcome` (`Common/Abstractions`) · `EmailSendCountDates`, `IEmailSendCountsSource`, `EmailSendCountsEvaluator`, `EmailSendCountsDto`, `GetEmailSendCountsQuery` (`Admin/Email`) · `IEmailSendCountRetention` and `EmailSendCountRetentionJob` |
| Infrastructure | `Email/Counting/`: the tally base and the SQL tally, the two entities, the read, the not-configured answer, the retention store and `AddEmailSendCountsSource` · the two configurations · the migration · the funnel's one call site, with `ScalewayEmailSender` made internal |
| Api | `GET /api/v1/admin/email/kinds?period=` |
| Worker | `EmailSendCountRetentionWorker`, at `"40 4 * * *"` |
| Web | the "Utskick per mejltyp" section (ADR 0150 Amendment (3)) |
| Docs | the privacy-policy point and retention line; `admin-email.md`; this ADR |
| Tests | the containment matrix; the outcome matrix; the date property over every quarter-hour of 2026; the migration Up/Down/Up; the IL pins on the tables and the start row; the consumer pins; the endpoint and the registrations |
