# ADR 0146 — Optimistic concurrency on job_seekers: an xmin token, and a replay for every writer of the row

**Date:** 2026-09-27
**Status:** Accepted
**Decider:** Klas Olsson (queue approved via the #1891 plan). Design: dotnet-architect. Signed with
conditions: security-auditor, 2026-09-27. Routing: senior-cto-advisor, 2026-09-27.
**Related:** #1891, #1903 (consent split, merged `26a11893`), #1907 (unmarked-command conflicts still
500, filed), #1819 (free-text `Language`, filed), #751 (the per-user child-scope precedent this ADR
extends), ADR 0008 (pipeline order — unchanged), ADR 0009 (port — amended by this ADR; see its
Amendment 2026-09-27), ADR 0022 (audit behavior innermost), ADR 0024 (hard delete), ADR 0080 / ADR
0087 (the two consents and the two scans this token protects)

## Context

`job_seekers.preferences` is an owned `OwnsOne(...).ToJson()` document, and every writer of it
replaces the whole document. Before this ADR the row carried no concurrency token, so a write that
loaded the row before a later commit rewrote the document over it: a consent withdrawal (Art. 7(3))
committed between a cadence, language or consent save's read and its `UPDATE` was silently
re-granted. security-auditor's design-round Major 1 (#1891).

Four handlers write `Preferences` directly — `ChangeLanguage` (via `UpdateMyProfileCommand`),
`UpdateNotificationConsent`, `SetDigestCadence`, `UpdateFollowedCompanyNotificationConsent` — and
every other writer of the `job_seekers` row shares the same row-level race: the three seen
watermarks, match preferences, the primary-resume flag, soft delete, the dev reset, the two nightly
scans (`BackgroundMatchingJob`, `CompanyWatchScanJob`), and the hard-delete job.

security-auditor's probe (2026-09-27; real `AppDbContext` + `JobSeeker`, PostgreSQL 16.13, EF Core
10.0.12 / Npgsql EF 10.0.3, against the design-round build) measured all three shapes. With the
token alone, each stale write emitted `UPDATE job_seekers SET … WHERE id = @p AND xmin = @p RETURNING
xmin` and threw `DbUpdateConcurrencyException`: the withdrawal stood, and the failed attempt's audit
row rolled back with it. A replay **without** a tracker clear re-read the same stale tracked instance
and conflicted again. A replay **with** `ChangeTracker.Clear()` re-read the withdrawal and succeeded,
with exactly one audit row. The EF-docs "client wins" resolution
(`OriginalValues.SetValues(GetDatabaseValues())` then save) re-created the lost update outright:
stored flag `true`, withdrawal stamp `null`.

## Decision

**D1 — an xmin shadow concurrency token on `JobSeeker`.**
`builder.Property<uint>("xmin").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken()`
(`JobSeekerConfiguration.cs:100-105`), parity `ResumeConfiguration.cs:157-160`,
`ApplicationConfiguration.cs:151-154`, `ParsedResumeConfiguration.cs:155-158`. Migration
`AddJobSeekerXminConcurrencyToken` emits zero DDL: `xmin` is a PostgreSQL system column, so both `Up`
and `Down` are comment-only (db-migration-writer verified `dotnet ef migrations script` writes only
the `__EFMigrationsHistory` row against a throwaway PostgreSQL 18); the model diff lands in
`AppDbContextModelSnapshot.cs:606-610`.

**D2 — a replay in `UnitOfWorkBehavior`.** A new marker interface,
`IReplayOnConcurrencyConflict` (`Application/Common/Abstractions/IReplayOnConcurrencyConflict.cs:15`),
opts a command in. On `DbUpdateConcurrencyException` from the unit of work's own
`SaveChangesAsync`, the behavior calls `IAppDbContext.ClearTracking()` and re-runs the rest of the
pipeline on a fresh read, `MaxAttempts = 3` attempts in all (`UnitOfWorkBehavior.cs:17,31-48`); on
exhaustion the tracker is cleared once more and `ConcurrencyConflictException` is thrown
(`:44-46`), which the Api maps centrally to a 409 `ProblemDetails` with the fixed title
`Concurrency.Conflict` and no exception text or entity value (`Program.cs:271-278`). Clear-and-re-run
is the only resolution — never refreshing original values, never re-saving the stale entity. One log
line per replay carries only the command's type name (`:47`). Unmarked commands are unchanged (plain
`SaveChangesAsync`, no catch, `:24-29`), and only `DbUpdateConcurrencyException` is caught, so both an
unmarked command's conflict and any other `DbUpdateException` still propagate uncaught. Pipeline
order is unchanged (ADR 0008): `ReauthenticationBehavior` and `FieldEncryptionKeyPrefetchBehavior` sit
outside `UnitOfWorkBehavior`, so a replay spends no re-auth grant and prefetches no key twice;
`AuditBehavior` sits innermost and re-runs with the rest of the pipeline (ADR 0022), so a committed
attempt writes exactly one audit row (`MediatorPipelineBehaviors.cs:29-54`).

**D3 — every API writer of the row carries the marker.** Twelve command types carry
`IReplayOnConcurrencyConflict`: `UpdateMyProfileCommand` (language), `UpdateNotificationConsentCommand`, `SetDigestCadenceCommand`,
`UpdateFollowedCompanyNotificationConsentCommand`, `MarkJobsSeenCommand`, `MarkMatchesSeenCommand`,
`SetLastSeenFollowedAdsCommand`, `SetMatchPreferencesCommand`, `SetPrimaryResumeCommand`,
`DeleteResumeCommand`, `DeleteAccountCommand`, `ResetMyDataCommand` (dev). Contract of the marker:
the handler re-derives everything from what it reads and has no side effect before commit —
`DeleteAccountCommand`'s session teardown stays in the endpoint, after the mediator call returns
(`MeEndpoints.cs:129` commits; `:152-153` tears the session down). Withdrawal and grant share one
marker and one cap.

**D4 — ADR 0009 amendment.** `IAppDbContext` gains `ClearTracking()` (`IAppDbContext.cs:59-66`,
beside `Detach` at `:49-57`), a narrow member that maps to `ChangeTracker.Clear()`. `ChangeTracker`
itself stays off the port. Recorded as an amendment to ADR 0009, not a rewrite of it — see that ADR's
Amendment 2026-09-27.

**D5 — Worker.** `BackgroundMatchingJob` re-checks the consent on the row each attempt loads, before
any `Add` or watermark advance (`BackgroundMatchingJob.cs:164-173`: a withdrawn or disabled row
returns `0` with the watermark untouched), and retries once in a fresh child scope on
`DbUpdateConcurrencyException` (`:104-112`); the atomic commit is one `SaveChangesAsync` (`:279`), and
the Top-match email dispatches only after it (`:285-286`). `CompanyWatchScanJob` moves to a child
scope per user (parity #751, `CompanyWatchScanJob.cs:129-131`) and retries once the same way
(`:100-110`). `AccountHardDeleter` clears its tracker when one account's commit fails —
`db.ChangeTracker.Clear()` directly, since it holds the concrete `AppDbContext`, not the port
(`AccountHardDeleter.cs:315-320`): its context serves the whole run, and without the clear the next
account's save would write the failed account's still-tracked deletes.

**D6 — a withdrawal is refused only by exhaustion.** Reaching the cap needs a commit to the same row
inside each of the three attempts' read-to-save windows; every handler that loads a `JobSeeker` is
scoped to the caller's own `UserId`, and a scan writes a given row at most twice a night even with its
own retry, so only the data subject's own sustained writes can exhaust it. The refusal is visible and
truthful — 409 → the web maps it to an error state → the toggle reverts — and writes nothing; a grant
is refused the same way. This is security-auditor's Art. 7(3) ruling, not a CLAUDE.md §9.6 (3)
accepted-risk ADR: the cap closes the finding, it does not accept a residual one.

## Alternatives considered

- **Field-scoped `jsonb_set` behind a port.** Duplicates the Art. 7(1)/7(3) stamping invariants in
  SQL, or re-invents compare-and-set, and runs outside the unit of work's save, so the audit row
  would not be atomic with the mutation.
- **A version column for `Preferences` only.** The same writers are checked regardless — EF puts
  every concurrency token of an entity type in every `UPDATE`/`DELETE` of it — and it still needs
  `ADD COLUMN` plus a bump mechanism that `xmin` gets for free.
- **`Preferences` in its own table.** Real scoping, but a data migration out of `jsonb` and a rewrite
  of every reader, both scans included.
- **`SELECT … FOR UPDATE` / `SERIALIZABLE`.** Opens the transaction before the handler runs, which
  ADR 0008 avoids; the SQL is provider-specific and would sit behind a port; the change would reach
  every command, not this one row.
- **ETag/If-Match** (named in BUILD.md §6.1 until this PR deleted the line; no code read it). Solves
  a stale client view, not the in-request race, and makes an absolute withdrawal refusable.
- **"Client wins" resolution** (`OriginalValues.SetValues(GetDatabaseValues())` then save). Measured
  to re-create the lost update outright (Context).
- **An unrefusable withdrawal via a field-scoped write.** Would put a second copy of the Art. 7
  evidence rules beside the aggregate, and two sources of consent evidence can drift (Art. 5(1)(d),
  5(2)).
- **Same-value no-ops on the consent/language writers** (dotnet-architect's Q5). Not adopted: the
  replay already absorbs the conflicts a no-op would avoid, and a no-op consent grant read against a
  stale "on" state would skip the very conflict that re-derives it correctly. Revisit only if the
  per-replay log line shows a rate worth acting on.

## Consequences

### Positive

- An unrelated concurrent write to the row — another tab's `MarkJobsSeen`, a nightly scan — costs a
  replay, never a refusal.
- `DeleteAccount`: a conflict that used to surface as a 500 after the one-time re-auth grant was
  already spent is now either absorbed by the replay or, on exhaustion, a truthful 409 with nothing
  committed.

### Negative / out of scope

- Unmarked commands on the other tokened aggregates (`Resume`, `DomainApplication`, `ParsedResume`)
  still answer 500 on a conflict — filed as #1907, not fixed here.
- **`DigestDispatchJob` decides each pass's consent once**, at its due-set query
  (`DigestDispatchJob.cs:95-99`, `:139-143`), and does not re-check it at the per-user claim
  (`:247-249`, `:509-511`). A withdrawal or soft delete committed after the due-set query still gets
  that pass's email. This ADR does not close that gap, and nothing here claims a digest pass honours
  a mid-pass withdrawal (security-auditor's condition E8).
  *(The bullet's first three sentences above are superseded — Amendment 2026-09-27, #1891.)*
  The fix is security-auditor's Major 3,
  routed by senior-cto-advisor to its own follow-up PR immediately after this one.
- Free-text `Language` (`JobSeeker.cs:117-121`) stays an open Minor, already filed as #1819 — this ADR
  closes the concurrency race on it (D1) but not the missing domain invariant.

## Implementation

- db-migration-writer's script check: prior tip → new head is `START TRANSACTION; INSERT INTO
  "__EFMigrationsHistory" ('20260927121429_AddJobSeekerXminConcurrencyToken','10.0.12'); COMMIT`;
  `Down` is only the matching `DELETE`.
- BUILD.md §6.1 loses "ETag + If-Match för optimistic concurrency på aggregate-updates" in the same
  PR (senior-cto-advisor, in-block), so the rejected mechanism has no second home.

## Verification

Tests added with this ADR:
`tests/Jobbliggaren.Api.IntegrationTests/Me/JobSeekerWriteRaceTests.cs` (a cadence save held at
`SaveChanges` while a real withdrawal commits — both requests succeed, the withdrawal stands, exactly
one `DigestCadenceUpdated` and one `NotificationConsentUpdated`; the mirror with language vs.
followed-company; exhaustion on a withdrawal → 409, nothing written) with its
`Infrastructure/JobSeekerSaveRace.cs` hook;
`tests/Jobbliggaren.Api.IntegrationTests/Persistence/JobSeekerConcurrencyTokenTests.cs` (the
two-context mechanism pin over the four `Preferences` writers plus the scan watermark);
`tests/Jobbliggaren.Application.UnitTests/Common/Behaviors/UnitOfWorkBehaviorTests.cs` (clear-then-
re-run, the cap, unmarked commands untouched); `tests/Jobbliggaren.Worker.IntegrationTests/Matching/
BackgroundMatchingJobConsentRecheckTests.cs` and `.../CompanyWatches/
CompanyWatchScanJobIntegrationTests.cs` (the re-check, the retry, the child scope, via the new
`Common/OverridingScopeFactory.cs`); `tests/Jobbliggaren.Worker.IntegrationTests/Auth/
HardDeleteAccountsJobIntegrationTests.cs` (the tracker clear); and
`web/jobbliggaren-web/src/lib/api/me.test.ts` (409 on either consent route maps to `{kind:"error"}`).

## References

- security-auditor, design-round signature 2026-09-27 (E1–E9, T1–T7); dotnet-architect, design round
  2026-09-27 (Q1–Q5); senior-cto-advisor, routing 2026-09-27; db-migration-writer, 2026-09-27.
- ADR 0008 (pipeline order), ADR 0009 (this ADR's amendment), ADR 0022 (audit innermost), ADR 0024
  (hard delete), ADR 0080 / ADR 0087 (the consents and the scans this token protects).
- #1891, #1903, #1907, #1819, #751.

## Amendment 2026-09-27 (#1891) — DigestDispatchJob reads each consent again before it claims

**Scope.** Supersedes the Negative / out of scope bullet's first three sentences above, marked inline
(security-auditor's conditions F7 and F8): `DigestDispatchJob` deciding each pass's consent once at the
due-set query and not re-checking it at the per-user claim, and the disclaimer that nothing here
claims a digest pass honours a mid-pass withdrawal (condition E8). D1–D6 stand. D5's citations into
`BackgroundMatchingJob.cs` are corrected in place above, to where this PR's own change to that file
moved the same lines; its `CompanyWatchScanJob.cs` and `AccountHardDeleter.cs` citations did not move.

**The change.** `NotificationConsent.BackgroundMatch` and `.FollowedCompany`
(`Domain/JobSeekers/NotificationConsent.cs`) are the one definition of each consent —
a `Specification<JobSeeker>` (`Domain/Common/Specification.cs`): `Criteria` for a query, which EF
translates to SQL, and `IsSatisfiedBy` for a row already held in memory, both compiled from the one
expression. Every decision site reads through one of the two: the three due sets
(`DigestDispatchJob.cs:95`, `:144`; `BackgroundMatchingJob.cs:86`) and the scan's per-attempt check
(`BackgroundMatchingJob.cs:172`, D5) — so no two of them can drift apart (GDPR Art. 5(1)(d)).
`DigestDispatchJob` also reads the pass's consent again, through `ConsentStillGrantedAsync`
(`:329-334` — an `EXISTS` through the `JobSeeker` query filter, so a soft-deleted account reads as not
consenting and nothing is tracked), before it claims a user's rows (match pass `:252`, follow pass
`:531`), and, in the follow pass, again before it builds the CV-derived profile for an `OnlyMatched`
watch (`:426`, before `BuildFullForUserIdAsync` at `:429`). A consent that has ended by either read
claims nothing, sends nothing and logs nothing; the rows stay Pending. F6 required deleting, not
rewording, nine comments — `IEmailSender.cs`, both consent command handlers, `BackgroundMatchingJob.cs`,
`DigestDispatchJob.cs`, `JobSeeker.cs`, `BackgroundMatchingJobTests.cs` — that stated when a withdrawal
takes effect or that the Worker's filter honours it on its next run; none is reworded (`e31df2f2`).

**R1 — security-auditor's ruling.** "A withdrawal committed after that user's last check still gets
that pass's email." This is her Art. 7(3) ruling that the fix closes Major 3 — the same form as D6 —
and not a CLAUDE.md §9.6 (3) accepted-risk ADR.

**F8.** E8's second sentence lapses when this PR merges with U1–U4 green in CI; R1 and the F6
deletions above replace it.

**The tracker clear.** D5's remedy — clearing the tracker — also
applies to `DigestDispatchJob`: one `IAppDbContext` serves the whole run, so each pass's per-user loop
clears it in a `finally` (`:119-124` match pass, `:166-170` follow pass). Before this, a
claim whose `SaveChangesAsync` threw left that user's rows Modified in the shared context, and the
next user's save committed them Queued with no email ever sent for them.

**Verification.** `tests/Jobbliggaren.Domain.UnitTests/JobSeekers/NotificationConsentTests.cs` and
`.../Common/SpecificationTests.cs` (the two specifications over the aggregate's own consent states;
neither is satisfied by the other consent). `tests/Jobbliggaren.Worker.IntegrationTests/Matching/
DigestDispatchJobConsentRecheckTests.cs`, against real Postgres: U1 the match pass and U2 the follow
pass, each a theory over {withdrawal, soft delete} ending the second of two seekers' consent after the
due set is read — that user is neither claimed nor emailed, in either pass; U3, the soft-delete arm,
reads its precondition past the `JobSeeker` query filter; U4, the follow pass, never builds the
withdrawn user's profile. `tests/Jobbliggaren.Worker.IntegrationTests/Matching/
DigestDispatchJobPoisonIsolationTests.cs`: a one-shot trigger fails the first claim of a pair in each
pass — its rows stay Pending, the other user is sent.

**References.**

- security-auditor, signature 2026-09-27 (F1–F8, U1–U5); dotnet-architect, design round 2026-09-27;
  senior-cto-advisor, routing 2026-09-27; #1891.
