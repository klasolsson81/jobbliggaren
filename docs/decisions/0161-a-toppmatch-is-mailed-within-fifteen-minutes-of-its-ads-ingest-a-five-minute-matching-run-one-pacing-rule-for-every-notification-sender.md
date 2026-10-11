# ADR 0161 — A Toppmatch is mailed within fifteen minutes of its ad's ingest: a five-minute matching run, one pacing rule for every notification sender

**Date:** 2026-10-11
**Status:** Accepted
**Decider:** `senior-cto-advisor` under Klas Olsson's delegation. Klas's directive on #1662, 2026-10-11: "Det jag vill ha är
såklart ett mail direkt, vid ny toppmatch, inte bara kl 05." The CTO ruled the design before any code
(`docs/reviews/2026-10-11-2106-form-cto.md`: D1–D13; its conditions are this ADR's) on the reports of `dotnet-architect`
(`docs/reviews/2026-10-11-2106-form-architect.md`) and `security-auditor` (`docs/reviews/2026-10-11-2106-form-security.md`:
Q8, the signed row-7 text, conditions V1–V7). Klas answered the one question the round left him, D12, "GO, denna session (Rek.)".
**Amends:** ADR 0080 (local-only): Beslut 2 (the nightly scan at ~03:20 UTC) and Beslut 4 (a Top mail "dispatched in the same
scan run"), and its Beslut 5 by the reading in "Consent" below. A pointer goes into ADR 0080 in the main copy only. ADR 0144, by
its Amendment of 2026-10-11 (row 7).
**Related:** [#2106](https://github.com/klasolsson81/jobbliggaren/issues/2106) (the issue this ADR ships under) ·
[#1662](https://github.com/klasolsson81/jobbliggaren/issues/1662) (the notification epic) · #1664 and #1665 (the other notification
senders, who read the pacing rule here) · ADR 0087 D5 (rows 7 and 8 are separate purposes) · ADR 0090 D3 · ADR 0124 (no
idempotency key on a send) · ADR 0144 (row 7) · ADR 0146 (consent decided on the loaded row) · ADR 0154 (every merge is a deploy) ·
CLAUDE.md §9.2 (the read rule), §9.6
**Measured against:** the #2106 worktree at `6292ee856`; the form-round reports, read on `b7199bfb8`; the box (`jp-vps`),
read-only, 2026-10-11 (D3).

> **Provenance.** Written by `adr-keeper` for the driving session from the CTO's ruling and the delivered code (CLAUDE.md §9.2,
> §13). The cadence, the watermark, the pacing numbers, the send phase, the age bound and the lock are the CTO's ruling; the
> mandatory agents review them in the PR. This ADR is promoted with `git add -f` (D9): ADR 0080 is local-only, and the sessions on
> #1664 and #1665 must be able to read the pacing rule.

---

## Context

Ads reach `job_ads` every ten minutes (`sync-platsbanken-stream`, `*/10 * * * *`; the daily snapshot is a second writer). Matches
and the Top mail came from one run at 03:20 UTC (`20 3 * * *`), the "kl 05" of Klas's directive. ADR 0080 Beslut 2 chose that
nightly scan and rejected event-on-ingest; Beslut 4 routed a Top match to a direct mail dispatched in the same scan run. A
Toppmatch for an ad ingested in the morning therefore waited until the next night.

**Moving the cron is not the change** (CTO, Regel 4). On its own it makes the permanent-miss race about 144 times more likely (D2)
and keeps one unbounded mail per Top ad (D4). `user_job_ad_matches` has no concurrency token, so a second claimer of Top rows
sends twice (D8). A tick every five minutes also meets the stranded-match reaper (Queued rows older than 48 hours, 04:45 UTC)
every night (D6). Two further senders are planned on the same provider, #1665 (the digest) and #1664 (followed-company and
smart-watch Top), and each needs the same answer to "how fast may notification mail go", so the answer has a home here and not in
one job.

## Decision

**A Toppmatch is mailed within fifteen minutes of its ad's ingest.** `background-matching` runs every five minutes and persists
matches. A send phase of the same run, under the same lock, mails the due Top matches under one pacing rule that every notification
sender uses. D1–D11 keep the CTO's numbers so that his report reads beside this one; his D12 is in Implementation and his D13 in
"Out of scope".

### D1 — Cadence

The job keeps its id `background-matching`. Its cron is `2-59/5 * * * *`, a `public const` on `BackgroundMatchingWorker`, not an
`IOptions` value, because the fifteen-minute bound (D10) is a product promise and a configuration value could break it with no test
failing. The 03:20 run is retired; no nightly run remains. The +2 offset keeps the tick off the :00/:05 cluster (the stream every
ten minutes, landing statistics every five).

The house pattern for a table with more than one writer is a level-triggered tick over committed rows (the criterion sweep, the
feedback dispatch, CTO D1 2026-09-14). ADR 0080 Beslut 2's rejection of event-on-ingest stands: this is not event-on-ingest. The id
is unchanged, so `RecurringJobIds.All` stays at 21, and the admin allowlist and the registrar parity test do not move.

### D2 — The watermark trails the clock by a settle window

`JobAd.Import` stamps `CreatedAt` before the ad commits, so an ad stamped before a run's read can commit after it, and a watermark
advanced to "now" would skip that ad for good. The run takes `now` **before** the global candidate read, advances each user's
`LastMatchScanAt` to `now − IngestSettleWindow` in both branches (the occupation gate and the normal path), and reads
`CreatedAt > since` with **no upper bound**. The window is two minutes. `LastMatchScanAt` keeps its meaning: every ad with
`CreatedAt` at or before the value was visible to a scan. The Domain layer does not change, and the window adds no latency, because
the read has no upper bound. It only re-reads the last two minutes of ads, which the existing-pair check drops.

An ad is missed only if its stamp-to-commit time reaches the window. The condition for the window is that the measured maximum is
at most a tenth of it (12 s); otherwise the window is derived again before merge.

**Measured 2026-10-11**, three runs of 200 new ads (Swedish descriptions of 4 761 bytes, term extraction running) through the real
`UpsertExternalJobAdCommandHandler` against Testcontainers Postgres 18 (Windows 11, Docker Desktop, Debug build), timed from the
clock read in `JobAd.Import` to the handler's return after its commit, an upper bound: warm p95 13–39 ms and maximum 25–114 ms;
the first ad after process start 1.75–1.87 s (JIT, the first skill-index and EF-model load, the first connection). The stream
and the snapshot send the same command per event, so the figures hold for both paths. All are under the 12 s condition. The cold
first call has the least margin: a box five times slower would give about 9.4 s.

**Lapse trigger** (also in the docblock of `BackgroundMatchingJob`): an ingest path starts batching commits, that is, stamping in one
transaction and committing in a later one, or the measured maximum approaches 12 s.

### D3 — Cost per run

- **One global candidate read per run**: Active ads with `CreatedAt` after the oldest due watermark, projected to the ad's id and
  ingest time. Each user's slice is filtered in memory against that user's reloaded, tracked row. A user whose slice is empty is
  skipped entirely: no profile build, no write, no `xmin` bump (security V6). The mail's title and company come from the dispatcher's
  own query (D5).
- **The existing-match check is bounded** by `m.CreatedAt > since − 1 day`. It is a safe superset: `JobAd.CreatedAt` is set only in
  the constructor and nothing reactivates an ad, so a match row is always created after its ad, and the day covers a clock step. A
  missed duplicate would roll back the user's whole batch on every tick.
- **`ScanBudget` is three minutes**: the scan starts no new user after it, and a cut logs a Warning as a capacity signal. Users
  are taken steady-state first, never-scanned last, written as `OrderBy(LastMatchScanAt == null).ThenBy(LastMatchScanAt)` because EF
  InMemory and Npgsql order nulls differently. A cold-start user's Top matches are not "within minutes" by definition.
  `JobAdLifecycleReadRegistry` is updated for both read sites.
- **Measured 2026-10-11 at about 06:00Z on the box**, plan and timing only (CLAUDE.md §9.2 (a)): `job_ads` holds 41 538 Active and
  87 158 Archived rows. `EXPLAIN (ANALYZE, BUFFERS)` of `status = 'Active' AND created_at > now() − 7 days` is a Seq Scan,
  62.3 ms, 11 725 rows. With a ten-minute window it is a Parallel Seq Scan, 42.3 ms, 0 rows. A steady-state tick reads minutes, not
  days, 288 times a day. **No index now.**
- **Lapse trigger:** the candidate read takes over 500 ms, and an index PR follows under #1666's migration token.
- **The send phase's due read** (D5) also runs once per run. Measured the same day: `user_job_ad_matches` held no rows, and the
  plan was a Seq Scan, 0.06 ms; no index leads with grade or status. **Lapse trigger:** the due read takes over 500 ms, and a
  partial index on `created_at` where `grade = 'Top' AND notification_status = 'Pending'` follows under a migration token.

### D4 — One pacing rule for every notification sender

- **Shape.** `MailPacingRule` is a pure sealed record (`MinGapPerRecipient`, `MaxMailsPerRun`) in
  `Application/Common/Notifications`. `MailPacingRun` is built from a rule, one per run and never shared, and counts the mails it
  admits. Its `Decide(lastMailToRecipientAt, now)` answers `MailPacingDecision`: `Admitted`, `RecipientTooSoon` or `RunFull`. A
  refused row stays in the sender's committed queue for the next run, and nothing waits inside a send loop.
- **The rule decides when, never whether.** Each sender keeps its own consent check, because rows 7 and 8 of ADR 0144 are separate
  purposes (ADR 0087 D5; security V4).
- **State is the sender's own committed `SentAt`.** No port, no Redis, no table. A sender reads the time of a recipient's last mail
  from its own rows.
- **`TopMatch` is a gap of zero and 20 mails per run.** Each user's scan is atomic and each recipient gets at most one bundled mail
  per run, so every burst (cold start, re-consent, the first tick after a deploy) lands in one tick and is one mail of up to 20 matches. The
  architect's 60-minute gap would therefore never bind in a burst. It would bind only on a second, separate Top match within the
  hour, which is exactly the case Klas asked to have mailed "direkt". It would falsify the bound in D10 and the signed text of
  row 7, and it would trigger security V4, which requires row 7 to say that mails can be held back. For Top the limit is thus one
  mail per recipient per run, at most 12 an hour. A test pins the zero gap, and its failure message names V4: raising the gap sends
  row 7 back to `security-auditor`. The dispatcher applies the rule it is given and never branches on the rule's numbers.
- **Twenty mails per run (at most 240 an hour)** smooths volume for a young sender domain. The excess is deferred oldest-first by one
  tick and never dropped inside the 24-hour window of D6.
- **Other senders.** #1665 (the digest) and #1664 (follow and smart-watch Top) construct their own rule instance and write their own
  last-mail read. A shared port is added when they need one, not before. A non-zero gap needs no change to the rule.
- **The provider sets no rate limit that this rule defends against.** Scaleway Transactional Email documents no per-minute or
  per-hour limit (the hourly quota was removed 2023-12-01), a default of 10 000 mails a month, and the right to adjust rates (page
  validated 2025-10-29, read 2026-10-11). The pacing is about sender reputation and inbox fatigue, not a provider cap. The monthly
  quota is shared with the sign-in codes (ADR 0142).
- **Lapse triggers** (home: this ADR; reader: Klas):
  1. Monthly sent volume above 50 % of the shared quota: add a global rolling 24-hour budget (precedent:
     `FeedbackNotificationDispatchJob.DailyBudget`).
  2. More than 12 Top mails to one recipient in a rolling 24 hours, measured as a single maximum under §9.2 (a): a non-zero gap,
     decided with Klas.

### D5 — The send phase

The scan only persists rows. `TopMatchMailDispatcher` runs after the scan loop, in the same run and under the same lock, taking the
due rows oldest first.

- **One query for claim and display.** The claim set is read together with each ad's title and company, so the mail's count equals
  the claimed presentable rows by construction (#864: an invariant you construct is a guarantee). Selection also applies
  `NotificationConsent.BackgroundMatch.Criteria`, so a row of a user who is not consenting is never claimed.
- **Per recipient:** (1) claim every due row and commit; (2) check consent again on the live row, the deletion and withdrawal fence
  (security V1; Art. 7(3), Art. 17, #533); (3) read the address, never stored on the row, in a Hangfire argument or in Redis;
  (4) send one mail, outside any transaction; (5) mark the rows Sent and commit. A row whose send did not complete (consent gone, no
  address, a failed send) stays Queued, is never sent again, and the reaper fails it later. The first send failure ends the phase, so
  a provider outage costs one recipient's rows per run. `SendBudget` is one minute.
- **Bundling.** A mail lists at most `MaxItemsPerMail` = 20 matches, a constant on the dispatcher and not a shared option; the rest
  go in the next run's mail. One match gives today's mail with the `/jobb/{id}` link (#2094). More than one gives the plural subject
  "Nya toppmatchningar på Jobbliggaren", and links to `/matchningar`. This changes the Direct
  branch of `EmailTemplates.cs` and its tests; `design-reviewer` grades the copy.

### D6 — Due, and the age bound

A row is due when
`Grade == Top ∧ NotificationStatus == Pending ∧ ad.Status == Active ∧ ad.CreatedAt ≥ now − W ∧ row.CreatedAt ≥ now − W`,
with W = `TopMatchMailDispatcher.DirectMailWindow` = 24 hours.

- **The ad term is security V2.** The clock starts at T = `JobAd.CreatedAt`, our own clock in the Worker's process. A fresh row
  for an old ad (the first run after a deploy, a re-consent after a pause, the cold start's seven days) never mails. `PublishedAt`
  is not used: the supplier sets it and it can be backdated.
- **The row term** makes the set disjoint from the reaper's (Queued rows with `CreatedAt` older than 48 hours) by construction and
  not by derivation. `user_job_ad_matches` has no token, so a `MarkFailed` saved after a `MarkSent` would relabel a sent row.
- A Top match outside W is shown in `/matchningar` only, and `Pending` honestly means "never notified".
- **Pins:** a fresh row for a three-day-old ad, produced by `JobAd.Import` and the real scan, sends 0 mails; W is less than
  `StrandedThresholdHours`, read from both constants.

### D7 — Hangfire: the next tick is the retry

`BackgroundMatchingWorker.RunAsync` carries `[DisableConcurrentExecution(timeoutInSeconds: 120)]` and
`[AutomaticRetry(Attempts = 0)]`. The wait is under half the 300-second interval, so at most one tick waits (wait ÷ interval is the
offered concurrency, as in `CompanyWatchCriterionMaterialisationWorker`; the former 1 800 s would have queued six waiters against
four workers). The watermark and the Pending rows are the queue, so a tick that the lock blocks is a harmless Failed row.

**Named exception, not solved here.** After a hard crash the lock row survives for up to 12 hours
(`HangfireStorageOptionsFactory`, `DistributedLockTimeout`). The mail then arrives late but is neither lost nor doubled: W is 24 hours,
longer than 12, and the next tick bundles. Detection is a Failed `background-matching` job every five minutes in Hangfire. The
remedy is deleting the lock row, a write on the box that needs Klas's GO. A crash-safe lease is a separate change-reason that
affects every job.

### D8 — Never twice

Four layers: `UNIQUE(UserId, JobAdId)`; the state machine (`MarkQueued` only from Pending, `MarkSent` only from Queued); the claim
committed before the send; and **exactly one claimer of Top rows, under one lock**. The fourth carries the weight, because the
table has no concurrency token and the claim is an unconditional update. The digest claims Strong rows only, and a row's grade
cannot change after `Create` (pinned by `DigestDispatchJobTests.RunAsync_OnlyStrongIsDigested_GoodAndTopAreIgnored`).

**Lapse trigger** (also in the dispatcher's docblock): a second claimer of Top rows shares the lock, or claims with a conditional
update (`ExecuteUpdate … WHERE status = 'Pending'`), which bypasses the aggregate and is its own decision.

**Pins:** two consecutive ticks give one mail per (user, ad); a deferral is bundled in the next tick and an empty tick follows; an
ad in the settle overlap is scored again without a new row or mail; a claim committed and a send that throws leaves the row Queued.
A Mono.Cecil test fixes the set of methods that call `UserJobAdMatch.MarkQueued` to the dispatcher's claim method and
`DigestDispatchJob.DispatchUserDigestAsync`, mapping async state machines back to their declaring method. A reflection pin keeps
the lock attribute on `BackgroundMatchingWorker.RunAsync`. No test asserts that two unlocked instances never double-send:
production never produces that state (AGENTS.md §5, Tests).

### D9 — Where this decision lives

ADR 0161 is tracked through `git add -f`, with a README row (precedent 0154 and 0156–0159, inside the ignored 0071–0199 range),
so the #1664 and #1665 sessions can read the pacing rule; a gitignored decision is invisible to them (#1173). ADR 0080 gets a
pointer in the main copy only. ADR 0144 is tracked and gets its row-7 amendment in the same PR, with the security report promoted.
`BUILD.md` and the code comments that describe matching as nightly or at 03:20 are corrected in the same PR, and the session records
the sweep's result in the PR body.

### D10 — The bound

T is `JobAd.CreatedAt`. The declared bound is 15 minutes (`BackgroundMatchingJob.TopMatchMailBound`). It covers the five-minute
interval, `ScanBudget` (three minutes), `SendBudget` (one minute) and Hangfire's scheduling slack; the architect's estimate of the
expected latency is about ten minutes. **It holds only while** a Hangfire worker is free, no lock row is stale (D7), the run that
first reads the ad reaches the user in both phases (no budget, cap or earlier send failure cuts it), and Scaleway accepts it. It does not cover a cold-start user (D3).

Three pins: (1) arithmetic, in its own test file because #2105 changes the registrar parity test: the interval computed from the
cron constant's minute field (Cronos is not referenced, and the field's form is pinned), plus the budgets and a slack, is at most 15 minutes; (2) behaviour: `JobAd.Import` with the clock at T,
then a tick at the next cron occurrence, gives exactly one mail with `SentAt − T ≤ 15 min`; (3) race: `JobAd.Import` (the named
actor, which stamps before the commit) creates an ad before tick k's `now` but commits after tick k's read, and tick k+1 mails it
within the bound.

### D11 — A re-consent scans the last seven days, not everything since the old watermark

`since = max(LastMatchScanAt, now − ColdStartDays)`, and a null watermark gives the floor. `UpdateNotificationConsent` does not
touch `LastMatchScanAt`, so a re-consent after a long pause used the old watermark and scored every Active ad since (up to the whole
Active corpus, 41 538 ads on 2026-10-11), which breaks the three-minute `ScanBudget` and the 120-second lock wait. The architect graded it
Viktigt; it is a defect in delivered code, in a file this change owns, and it shares D3 and D7's change-reason, so it is fixed
here. Test: withdraw, advance the clock more than seven days, re-consent, all through `UpdateNotificationConsent` and the clock; the
scan reads only the seven-day window.

### Consent

- **No fresh consent** (security-auditor, Q8). New consent is needed when the purpose changes or another is added (EDPB Guidelines
  05/2020 v1.1, p. 58 and 90). Matching new ads against the profile and mailing top matches is the same purpose with the same data
  and the same recipient (Scaleway); one consent covers different operations that serve one purpose (p. 57); and timing is not part
  of the minimum information for informed consent (p. 64 (i)–(vi)). Art. 7(1) holds without a version field, since the purposes are
  the same in both texts and ADR 0144's dated amendments show which text applied when. Withdrawal works as before and bites faster
  with the shorter cadence. A forced re-consent would also need a versioned field and so a migration, and switching consent off from
  our side would stamp an Art. 7(3) withdrawal the user never made.
- **Row 7 loses only "varje natt" / "every night"** (ADR 0144 Amendment 2026-10-11). The corrected text ships in the same deploy as
  the new cadence (merge is deploy, ADR 0154): "every night" left standing while mail goes out in the daytime is a false statement
  (Art. 5(1)(a), 12(1)).
- **V3.** ADR 0080 Beslut 5's "Existing `Pending` rows are NOT sent after withdrawal" is read for Top as "not while consent is
  withdrawn", the reading the digest already uses. A re-consent may therefore mail a still-due Top row within W (D6).
- **Conditions carried** (security-auditor; the grade applies if the PR breaks them): V1 the consent re-check after the claim (D5),
  with the `TopDirect` arm of `UserMailDeletionFenceTests` moved to the dispatcher and a new arm for withdrawal after the claim; V2
  the age bound (D6); V4 pacing on existing `SentAt`, no new storage (D4); V5 logs carry counts per run and a per-user line only on
  an event, never an address, and never a `JobAdId` together with a `UserId`; V6 no profile cache between runs and no
  `organization_number` or contact column in a mail or a log (#841); V7 no Hangfire fan-out with a per-user argument, since the
  `hangfire` schema counts as personal data (CLAUDE.md §9.2 (b)).

## Alternatives considered

- **Move the cron and nothing else.** Rejected outright (CTO, Regel 4): D2, D4 and D8 exist because it is wrong.
- **D1.** *Chaining the run after the stream* misses the snapshot's ads, since `job_ads` has two writers, and is event-on-ingest by
  the back door (ADR 0080 Beslut 2). *Nightly plus frequent* keeps two schedules for one fact. *A ten-minute cadence placed after the
  stream* adds about ten minutes of latency before the run time and leaves no headroom under the bound.
- **D2.** *A watermark on commit order or `xmin`* needs provider SQL, has wraparound, and needs a migration. *Overlapping the read
  instead of trailing the watermark* would make `LastMatchScanAt` claim more than was scanned.
- **D4.** *A shared port for a recipient's last mail* is YAGNI and repository-shaped. *Redis*: `IRateBudget` exists only in the Api
  composition, the Worker has no volatile connection, and a Worker-Redis composition is a Redis ACL change that needs Klas (§9.2).
  *An in-memory bucket across runs* resets at every deploy, which is every merge (ADR 0154). *A new table* needs a migration. *The
  architect's 60-minute gap* is overruled above.
- **D5.** *A separate dispatch job* takes the recurring-job count from 21 to 22, which collides with #2105 in the registrar parity
  test, adds a second claimer of Top rows and one more interval of latency. *Sending inline in the scan plus a leftover pass* gives two
  claimers under different locks over a table with no token (the architect's Kritiskt finding).
- **D6.** *`PublishedAt` as the clock* is supplier-controlled. *The row's `CreatedAt` alone* makes a fresh row for an old ad look new
  (security V2, Major).
- **Consent.** *A new consent* is not required, and would need a migration (any version field on `Preferences`, even inside the
  jsonb, does).

## Consequences

### Positive

- A Toppmatch is mailed within fifteen minutes of its ad's ingest, and three tests hold the bound (D10).
- #1664 and #1665 inherit one pacing rule and one place that says what it means, without a new port, store or migration.
- A withdrawal bites within a tick, and the deletion fence runs on the live row at every send.
- One global candidate read per tick replaces one unindexed read per user, and a user with nothing new costs nothing.
- No migration: the pacing state is the committed `SentAt`, and the consent needs no version field.

### Negative

- **A one-tick deferral** when more than 20 mails are due in one run (D4).
- **Up to 12 hours of late mail after a hard crash** (D7), until the lock row is deleted with Klas's GO.
- **Top matches for ads older than 24 hours are never mailed**; they show only in `/matchningar` (D6).
- **The settle overlap re-scores a few ads every tick** (D2).
- The send phase is a second phase under the scan's lock, so a slow send delays the next scan; the budgets (D3, D5) and the 120-second
  wait (D7) bound it.
- The settle window is a measured assumption about every ingest path, and it lapses with the trigger in D2.
- Cold-start users wait behind steady-state users when the scan budget is spent (D3).
- **A user whose scan fails every time** keeps the watermark, is scanned again every tick over a window that grows to seven days,
  and logs a Warning each time; the scan budget protects the other users only while that scan stays under three minutes. Moving a
  failing user last needs state between runs and is its own decision.
- The bound in D10 is conditional on four things the code does not control alone.
- The 10 000-a-month provider quota is shared with the sign-in codes; the first lapse trigger in D4 watches it.

**Lapse triggers.** D2, D3, D4 and D8 each end in a lapse trigger. Their one home is this ADR. The reader of D4's two is Klas. D2's
and D8's are carried in the docblock of the code they guard (`BackgroundMatchingJob`, `TopMatchMailDispatcher`).

## Implementation

One PR for #2106: the cadence, the watermark, the dispatcher and the pacing rule, the Direct branch of `EmailTemplates.cs`, row 7's
copy in `messages/{sv,en}/settings.json` with `pages.matchningar.emptyBody` (not a bound string: `design-reviewer` has the last word),
the comment and documentation sweep (D9), and the tests above. There is no migration. The mandatory agents of CLAUDE.md §9.2 review
it.

- **Log levels** (CTO D13). The per-tick summary logs at Debug when nothing happened and at Information when rows or mails are above
  zero. A second `xmin` conflict logs at Information as "deferred to next tick". Per-user lines only on an event (V5).
- **Release order** (CTO D12, `security-auditor` Blocker 1, whose grade stands). On 2026-10-11 the published privacy policy still called
  the notification mail planned and not in operation. Klas gave GO to a separate PR that corrects it
  ([#2114](https://github.com/klasolsson81/jobbliggaren/pull/2114)), and that PR **merges before #2106**. Without it #2106 carries the `blocked` label. The count of `notification_status` rows on the box is taken by the session
  under §9.2 (a) and recorded with its date in that PR. The policy text is not part of this decision.

## Out of scope

- **The follow digest can name an ad that already went out as a Top mail.** That is two mails by design (ADR 0087 D5). The
  deduplication belongs to #1664 and #1665.
- **The digest reads its claim set and its display set as two queries** (`DigestDispatchJob.cs`, the claim at lines 184–190 and the
  display at 216–239 on `b7199bfb8`). Once `background-matching` commits Strong rows at 06:0x, a commit landing between the two
  reads gives a row that is listed without being claimed, and it is listed again in the next digest. The fix is one query, by
  #864's own principle. The same issue carries the stale comments at lines 22–23 and 182–183. It is filed as
  [#2118](https://github.com/klasolsson81/jobbliggaren/issues/2118) (the matching area label, `P3`, `BE`, no `mvp`). This PR must not touch the file; the first PR
  that opens it after #2106 fixes it as its own commits.

## References

- `docs/reviews/2026-10-11-2106-form-cto.md` · `docs/reviews/2026-10-11-2106-form-architect.md` ·
  `docs/reviews/2026-10-11-2106-form-security.md`
- Code: `src/Jobbliggaren.Application/Common/Notifications/` (`MailPacingRule`, `MailPacingRun`, `MailPacingDecision`) ·
  `src/Jobbliggaren.Application/Matching/Jobs/BackgroundMatching/` (`BackgroundMatchingJob`, `TopMatchMailDispatcher`) ·
  `src/Jobbliggaren.Worker/Hosting/BackgroundMatchingWorker.cs`
- ADR 0080 Beslut 2, 4, 5 (local-only) · ADR 0087 D5 · ADR 0090 D3 · ADR 0124 · ADR 0142 · ADR 0144 (row 7 and its Amendment
  2026-10-11) · ADR 0146 · ADR 0154 · AGENTS.md §5 (Tests, Comments) · CLAUDE.md §6.5, §9.2, §9.6
- EDPB, Guidelines 05/2020 on consent under Regulation 2016/679, v1.1, p. 57, 58, 64, 90 (read 2026-10-11; the link may rot):
  https://www.edpb.europa.eu/system/files/documents/files/file1/edpb_guidelines_202005_consent_en.pdf
- Scaleway, Transactional Email, capabilities and limits (page validated 2025-10-29, read 2026-10-11; the page may change):
  https://www.scaleway.com/en/docs/transactional-email/reference-content/tem-capabilities-and-limits/
- Martin, *Clean Architecture* (2017), ch. 7 and 13 · Fowler, *Patterns of Enterprise Application Architecture* (2002), Optimistic
  Offline Lock · Hunt and Thomas, *The Pragmatic Programmer* (1999), DRY
