# ADR 0172 — An account's last login and last active minute: one row per profile, written at the session grant and on the authenticated request path, erased by the database

**Date:** 2026-10-17
**Status:** Accepted
**Deciders:** Klas Olsson, the controller: the source ("Ja, båda (Rek.)"), the migration GO ("GO (Rek.)") and the hand-over of the `hotspot:i18n` token ("Ja, ta över (Rek.)"),
each by AskUserQuestion on 2026-10-17 · `senior-cto-advisor`, "the CTO" below (decision-maker under CLAUDE.md §9.2, report-only): the form-round ruling of 2026-10-17
(`docs/reviews/2026-10-17-admin-activity-form-cto.md`, local)
**Amends:** nothing. ADR 0150 (its D8 rows for "Senast inloggad" and "Senast aktiv") and ADR 0151 (D1, D3 and D4) are amended by #2161's part 2, in its own PR, not by this ADR.
**Amended:** 2026-10-17 by [#2166](https://github.com/klasolsson81/jobbliggaren/issues/2166) (part 2): D1, D3, D7 and lapse triggers 5 and 7, each by a dated pointer in place (7 is new), and Klas's two
part-2 answers, carried verbatim at the end. Where a pointer differs from the text it follows, the pointer governs and that text stays as the record.
**Related:** [#2161](https://github.com/klasolsson81/jobbliggaren/issues/2161) (part 1 of 2; part 2 is the directory read and the UI) · #1984 (impersonation, not built) · ADR 0013 · 0024 ·
0045 · 0120 · 0139 · 0142 · 0146 · 0150 · 0151 · 0155
**Measured against:** `origin/main` at `911e48268` (#2155's merge), read on 2026-10-17. Nothing here is merged.

> **Provenance.** Written by `adr-keeper` for the driving session from Klas's answers, the CTO's form-round ruling and the session's brief (CLAUDE.md §9.2, §13); it adds
> no decision of its own. What the session settled after the ruling is marked "design pass". The Context was read from `911e48268` on 2026-10-17, and D5's budget and event ids from
> the session's unmerged adapter; nothing was run and no figure is measured. The mandatory agents review the implementation in the PR.

---

## Context

Klas wants to follow the first test users in `/admin/anvandare`; registration has been open since 2026-10-11. ADR 0150 D8 ships "Senast inloggad" and "Senast aktiv" as unknown,
because no login or activity source exists. The per-account counts exist (ADR 0151 D4) and are untouched. Read at `911e48268` on 2026-10-17:

- **No source exists.** A successful login is an `ILogger` line (`AuthAuditLogger.LoginSucceeded`, EventId 1001) and no `audit_log` row (the one `User.InboxProvenByLogin` row is
  written only for the first inbox proof of an unconfirmed address). Redis sessions are volatile, since a logout erases them, and are the session store's internals, not a record.
- **A login has one choke point.** Every sign-in ends in `PasswordlessSessionGrant.GrantAsync` (ADR 0142 D3/D4): a code, a link, OAuth on an existing account, registration
  completion; it takes a `LoginSubject.Active` only. The one other site that issues a session, the change-email confirm, replaces the session of a device that is already signed in
  (ADR 0155 D4) and is not a sign-in.
- **The authenticated path already reads Postgres once per request.** The shipped `SlideThreshold` is 0.0, so every request already rewrites its Redis session, and
  `AccessControlledSessionStore.GetAsync` reads the database through `IAccountAccessReader`: a session is found only while the account `CanAuthenticate`, a live profile that is
  not suspended (ADR 0155 D4).
- **Where a row can live is constrained.** `job_seekers` carries `xmin` as its concurrency token (ADR 0146). A migration in the Identity context needs the manual
  `--prepare-identity` procedure (`docs/runbooks/vps-deploy-stack.md` §3c); an `AppDbContext` migration is applied by the box's `migrate` at the next reconcile.

## Decision

**Two minutes per account, in a new table `account_activity` with one row per profile: the minute a session was last issued and the minute of the last authenticated request.
One Application port writes them, best effort, from the session grant and from a middleware that lets a process make at most one request write per account per minute, and a foreign key makes
the database erase the row with the profile.** D1–D10 are this ADR's numbering.

### D1 — What the two values mean (CTO Q1)

"Senast inloggad" is the minute in which `PasswordlessSessionGrant` last issued a session for the account. "Senast aktiv" is the UTC minute of the last API request authenticated
by any of the account's sessions; the web's background refresh while a tab is visible counts. Recording starts with the release that ships #2161, with no backfill. "Never" is not
knowable, because accounts are older than the release and the writes are best effort (D5), so a missing value means only that nothing is recorded (D7). The instant the box first
applied the release is not known when this ADR is written; part 2 records it here, as a dated pointer, once it is measured.

*Measured 2026-10-17 (#2166, part 2).* This is the pointer that D1 promised. The instant was read on the box the same day, read-only and metadata only (CLAUDE.md §9.2 (a)):

- `jobbliggaren-reconcile.sh --status` showed receipt sequence 1635, release `sha256:20f8cc31ed0d…`, source `013060324` (#2162, part 1's merge) and the verdict consistent.
- The reconcile journal (`journalctl -u jobbliggaren-reconcile`) recorded "applying release 0130603…" at 18:49:52.76Z and "reconcile complete … applied" at 18:50:08.43Z.
- `docker inspect -f '{{.Name}} created={{.Created}} started={{.State.StartedAt}} finished={{.State.FinishedAt}} exit={{.State.ExitCode}}'` showed `jobbliggaren-migrate` started at 18:49:54.59Z and
  exited 0 at 18:49:56.27Z; it created the table. `docker inspect -f '{{.Name}} created={{.Created}} started={{.State.StartedAt}} restarts={{.RestartCount}} image={{.Image}}'` showed `jobbliggaren-api`,
  the first process able to write either value, created at 18:49:53.07Z and started at 2026-10-17T18:49:56.72Z, with RestartCount 0.

Recording on the box starts at that API start. The web holds it as the constant `ACCOUNT_ACTIVITY_RECORDED_FROM = "2026-10-17T18:49:56Z"` in `web/jobbliggaren-web/src/lib/admin/account-activity.ts`, the start
truncated to the second. The admin view shows it as 2026-10-17 20:49 (Europe/Stockholm, CEST): the minute in which that process started, truncated the way the cells are (G, D4), so no stored value falls before
it. The instant is true of the box only. A local stack or a test records from its own start, and the never-deployed admin preview passes its own fixture start.

### D2 — One table, one row per profile, one migration (CTO Q2)

`public.account_activity`: `job_seeker_id uuid` primary key, foreign key to `job_seekers(id)` ON DELETE CASCADE (declared from the dependent side, with no navigation);
`last_login_at timestamptz NULL`; `last_active_at timestamptz NOT NULL`. No index besides the primary key: that keeps the updates HOT-eligible, the later sort rides the sequential
plan that ADR 0151 D8 accepts, and part 2's LEFT JOIN is on the primary key, so it cannot multiply rows. The column is `last_active_at` and never `last_seen_*`: the
`last_seen_*_at` columns on `job_seekers` hold an item's timestamp, not a visit time.

The migration is one `AppDbContext` migration, `AddAccountActivity`. **Klas's migration GO of 2026-10-17 names exactly this table and these columns; any other migration, or a
changed shape, needs a new GO.** Up opens with `SET LOCAL lock_timeout = '3s'` (creating the foreign key takes SHARE ROW EXCLUSIVE on `job_seekers` until commit; the new table is
empty) and writes no row. Down drops the table and every minute in it, destructive by this repository's convention.

### D3 — An Infrastructure-only type behind an Application port (CTO Q3)

The type is configured in `Persistence.Configurations` (the ADR 0139 `CompanyWatchCriterionMember` form), is not a `DbSet` on `IAppDbContext` and is not a Domain aggregate: there is
no invariant to protect, and the upsert never loads it, so its only writer would bypass it and it would exist to satisfy a test's partition. The Application port
`IAccountActivityRecorder` has `RecordLoginAsync(Guid userId, …)` and `RecordRequestAsync(Guid userId, …)`. The Infrastructure adapter `SqlAccountActivityRecorder` runs one
parameterised `INSERT … SELECT js.id FROM job_seekers js WHERE js.user_id = @user ON CONFLICT (job_seeker_id) DO UPDATE` per call, through `Database.ExecuteSqlAsync`, so provider SQL
stays in Infrastructure (AGENTS.md §2.1, axis 3). The upsert is forward-only: it keeps the greater of the stored and the new minute. A missing profile inserts nothing, and there is
no `deleted_at` predicate: the foreign key bounds the write, a session exists only while the account can authenticate, and a second definition of "live" is what ADR 0151 D4
rejected. The values are truncated to the minute before they are written, so the stored precision is the shown precision. An architecture test pins the grant and the request
middleware as the port's only consumers, so the stored values mean what those two writers make them mean. Only `SqlAccountDirectory` reads the table (part 2).

*Pointer, 2026-10-17 (#2166, part 2).* That last sentence is now pinned by `tests/Jobbliggaren.Architecture.Tests/AccountActivityReaderTests.cs`, which scans the IL of the production assemblies. It admits three
methods as loaders of a string that names `account_activity`: `AccountActivityConfiguration.Configure`, `SqlAccountActivityRecorder.WriteAsync` and `SqlAccountDirectory.Command`. EF migrations and model
snapshots are admitted by kind, since every later migration carries the whole model. It admits only `AccountActivityConfiguration` as a referrer of the `AccountActivity` type. Any other consumer fails the test
until it is reviewed. The test cannot see SQL assembled without the table's name, a view or function that a migration creates, or a read made outside the code (CLAUDE.md §9.2 (b)).

### D4 — The throttle: one write per account per UTC minute per process (CTO Q4)

The writer is an Api middleware placed immediately after `UseAuthentication()`, so it sees exactly the principals the session handler accepted. It reads only the authenticated
principal and does nothing for an anonymous request. It is not a Mediator command: it is a per-request cross-cutting concern and not a use case, and it stays out of the logging and
audit pipeline. The adapter holds a process-wide claim per account per UTC minute and claims the minute before it attempts the write, so a process makes at most one request write per
account per minute and a failed write is not retried within that minute. The granularity, G, is one minute because it equals the display precision `YYYY-MM-DD HH:mm`: the shown minute is
exactly the last minute with a recorded request, and a coarser G would show a lower bound. G and the display format change together. The claim is an atomic compare-and-swap on a
concurrent map, pruned once per minute so that it holds roughly the accounts active in the current minute (design pass); this refines the ruling's `IMemoryCache` entry, whose
`GetOrCreate` is not atomic under concurrency. A login writes both columns and primes the claim. The minute comes from `IDateTimeProvider`. The claim holds nothing correctness
depends on (twelve-factor VI): losing it costs one redundant write, and that write converges (D6).

### D5 — A failed write never fails a login or a request (CTO Q5, triage R1 and R3; the event ids and the login write's cancellation are the design pass)

The write is best effort. The adapter owns a typed catch for the provider's exception (`DbException`; a typed catch that logs is not the §5 catch-all, AGENTS.md §3.6) and logs a
`LoggerMessage` Warning, EventId 2701, with the user id, the exception type name and the SQLSTATE and never an address.
A request write runs under a budget of 400 ms (ADR 0045 Beslut 1 class (c)) and a login write under 2 seconds: a lost request minute is overwritten by the
account's next request, while a lost login stays missing until the next sign-in. A write that outruns its budget is abandoned and logged as Warning 2703, while the caller's cancellation is not swallowed. The login write runs once the session exists, beside `audit.LoginSucceeded`, never inside the account-access scope:
inside it would commit a login for a session whose creation can still fail in Redis, a false value, and would hold the lifecycle lock for telemetry. It runs with
`CancellationToken.None`, so a client that hangs up after the session exists does not abandon the record. The residual is named, not hidden: if the last
write fails, an earlier minute remains, and the failure is logged.

### D6 — Concurrency and the hazards checked (design pass)

The foreign-key check takes `FOR KEY SHARE` on the `job_seekers` row, the weakest row lock: it leaves `xmin` unchanged and does not conflict with the `FOR NO KEY UPDATE` that a
JobSeeker save takes, so ADR 0146's hazard, a stale `xmin` failing a save, does not return. The forward-only upsert makes concurrent writes from two sessions of one account converge
on the maximum, so a value never moves backwards. With the foreign key in the schema, a code rollback past #2161 still erases these rows at the database.

### D7 — What part 2 shows (CTO Q8)

A missing value renders the unknown state (an en-dash, read as "Uppgift saknas"), never "Aldrig inloggad", and there is no "registreras sedan" note, which would need a
recording-start date that nothing records. Values show for every status that can hold a row, Active, Suspended and PendingDeletion; ProfileMissing has none by construction. A
timestamp is a past fact that no deletion path alters, unlike the counts ADR 0151 D4 withholds, where the deletion paths leave different rows live and a count would describe the
path and not the account.

*Pointer, 2026-10-17 (#2166, part 2).* One clause of D7 is replaced and the rest stands: "there is no 'registreras sedan' note, which would need a recording-start date that nothing records" gives way to a
?-help. The CTO withdrew the clause in the part-2 form round (`docs/reviews/2026-10-17-admin-activity-pr2-form-cto.md`, local). Its reason, that nothing records the start, stopped holding when D1's instant was
measured, and Klas's instruction for part 2, "Gissa inte; skriv vad kolumnen faktiskt betyder i UI-texten", decides that the UI states what the column means. Klas approved the text and the form on 2026-10-17
("Ja, ?-knapp med den texten (Rek.)", question 1 under "Klas's answers").

- **Form.** One `InfoDialog` in a `.jp-labelhelp` span at the "Senast inloggad" column header explains both columns (DESIGN.md §8 rule 5: explanations live behind the ?-help). The account panel has no trigger.
- **Text.** It states three facts: the times are stored from the start, D1's instant; a missing time means nothing has been stored since then; and "Senast aktiv" also includes automatic updates while a tab with
  the service is open and visible. The start comes from the web constant of D1, passed to the copy as `{start}` and formatted with the cells' own `formatDateTime`, with a no-break space between the date and
  the time. The verb is "sparas" and never "registreras", because the next column is "Registrerad". "Senast aktiv" is not renamed, because two words cannot carry the qualifier.
- **Unchanged.** The empty cell stays `<AdminUnknown />`, an en-dash that a screen reader reads as "Uppgift saknas". There is still no "Aldrig inloggad", and values still show for Active, Suspended and
  PendingDeletion.
- **Rejected in the same round.** No note, which leaves Klas's instruction unmet. A visible line in the lede or under the table, the form the copy skill rejects. A configured date on the wire, which turns an
  observation into a hand-typed setting and needs a new option, a wire field and a box `.env` write for one help sentence on a product with one production (ADR 0154). A note without a date. The date typed
  into the sv and en strings, which gives one instant two hand-typed homes. Renaming the label.

### D8 — Legal position and notice (CTO Q9 and condition 7; the assessment is the design pass)

Purpose: account administration and support, and following how the service is used. Basis: Art. 6(1)(f), with the assessment in short. *Legitimate purpose:* the controller sees how
the first accounts use the service, and support and security questions ("I can't log in", "that wasn't me") need the sign-in event. *Necessity:* only the latest value of each, at
minute precision, no IP address, no user agent, no history (Art. 5(1)(c)). *Balancing:* low intrusion, expected in a signed-in service, visible only to administrators through the
Admin policy, erased with the account. `security-auditor` confirms the basis in the PR review.

Retention (Art. 5(1)(e)): as long as the account exists; each new value replaces the previous one; the row is erased by the database cascade when the profile row is hard-deleted
(`AccountHardDeleter`, ADR 0024); like every table it persists in database backups until they rotate. Transparency (Art. 13(1)) ships in the same PR as the collection: the privacy
policy gets its own item in what is collected and its own retention item, never appended to the "Säkerhets- och händelselogg" item, whose "90 dagar" would make it false. The new text
states what, why, the basis, the retention and who sees it, and nothing more (Klas's directions of 2026-09-05 and 2026-10-11, as the CTO applies them). The policy version moves with
it: `privacy.updated` (sv and en) and `TermsAcceptance.CurrentPrivacyPolicyVersion` change in one edit, pinned by `TermsAcceptanceVersionsMatchPublishedPolicyTests`. Art. 15: the
record is included in a manual extract on request. The processing register gets a field note under "Användarkonto och autentisering", written in the main copy because the register
is gitignored.

### D9 — Erasure is verified by behaviour (CTO Q3)

`AccountHardDeleteCascadeFitnessTests` partitions the Domain's aggregate roots and cannot see this table, the price ADR 0139 named (its Major 5b). A case in
`HardDeleteAccountsJobIntegrationTests` that shows the row present before the hard delete and gone after it is therefore mandatory, and it is mutation-verified by weakening the
foreign key.

### D10 — Impersonation records nothing (CTO condition 3)

A request made under impersonation (#1984, not built) records nothing for the impersonated account.

## Alternatives considered

- **Chosen — both values, in a new table (D1–D4).** *For:* true values at the shown precision, erasure as a database property, one small migration the box applies itself.
  *Against:* an in-process throttle and best-effort writes (Consequences).
- **A — derive "last activity" from the `updated_at` of existing aggregates.** Rejected: it sees writes, not use, since browsing `/jobb` without a query, reading matches,
  `/foretag` and `/cv/granska` leave no row. Under "Senast aktiv" it is a lower bound presented as the last time, which ADR 0120 clause 5 forbids (applied to a time and not a
  count), and it couples the directory to five aggregates' `updated_at` semantics, which change for their own reasons (Martin 2017, ch. 13).
- **B alone — the login minute.** Rejected: a persistent session slides 30 days up to a cap of 180 and rotation keeps `CreatedAt`, so a daily user signs in about twice a year and
  the most engaged users would look inactive.
- **C alone — the request minute.** Rejected: it drops the sign-in event, which support and security questions need and Klas named.
- **Reading the Redis session payloads.** Rejected: they are volatile, and reading them couples the admin view to session internals.
- **Columns on `job_seekers`.** Rejected: every activity write bumps `xmin` (ADR 0146), which fails any JobSeeker save loaded before it, the Worker's five-minute scan or a
  concurrent request from the user's own browser, and it puts an operational observation inside the profile aggregate's consistency boundary (Vernon 2013, ch. 10).
- **A column on `identity."AspNetUsers"`.** Rejected: an Identity migration triggers the manual `--prepare-identity` procedure of §3c, disproportionate for two timestamps.
- **A user-id key with no foreign key.** Rejected: erasure would depend on handler discipline, and an upsert landing after a hard delete would create an orphan row; the foreign key
  makes erasure a database property and a write for a missing profile impossible.
- **A Domain aggregate.** Rejected: there is no invariant to protect (Evans 2003) and its only writer would bypass it, an anemic type (Fowler 2003).
- **A conditional SQL write on every request.** Rejected: it adds a database write to every authenticated request.
- **Reusing the session's previous `SlidAt` as the gate.** Rejected: it ties the write volume to `SlideThreshold` (cap 0.25 × 30 days), so raising the knob makes the stale stamp
  write on every request, and the session store would have two reasons to change (Martin 2017, ch. 7).
- **`IMemoryCache` for the claim.** Rejected: `GetOrCreate` is not atomic under concurrency, and the claim is taken before the write (D4).

## Consequences

### Positive

- Values are true at the precision they are shown at, and "Senast inloggad" answers the support and security question that "Senast aktiv" cannot.
- Erasure is a property of the database, also under a code rollback (D6).
- One small migration that the box applies itself, with no data to fill in and nothing on the Identity side; no new coupling across the Identity and profile boundary, so ADR 0013
  needs no note.
- At most one request write per active account per minute per process.

### Negative and risks

- **The throttle is in-process.** With N replicas it becomes at most N writes per account per minute (lapse trigger 1).
- **The writes are best effort.** If the last write fails, an earlier minute remains (D5), and a missing value never means "never".
- **Gaps at the start.** Nothing is backfilled. An account that has made no request since the release shows both values as unknown; an account whose session predates the release
  shows "Senast aktiv" from its first request but "Senast inloggad" only after its next sign-in, up to 180 days later for a persistent session (derived from D1 and D3, not observed).
- **The table sits outside the aggregate-root cascade gate,** so the behavioural oracle of D9 is mandatory and not a recommendation.
- **One more statement on the authenticated path.** It runs before the handler and outside the Mediator pipeline in which ADR 0045 measures latency (`LoggingBehavior`), so only
  an overrun of its own budget is visible (D5, Warning 2703; lapse trigger 2).

**Lapse triggers** (design pass). Single named home: this ADR. Named reader: Klas, and the session that changes the subject a trigger names. This decision is re-examined when:

1. the API runs more than one replica: a shared claim (for example Redis `SET NX EX`), or an accepted limit of N writes per account per minute (D4);
2. Warning 2701 or 2703 recurs, or a write's latency exceeds its budget: the writes move behind a bounded channel (the `BoundedDispatchChannel<T>` precedent, ADR 0142 D2);
3. #1984 (impersonation) is built: impersonated requests are excluded (D10);
4. the directory reaches ADR 0151 D8's 10 000-account trigger: an index on `last_active_at` is reconsidered, at the cost of HOT updates (D2);
5. a login series is wanted, such as the overview's "Inloggningar" and "Aktiva användare" cards, which stay unavailable until then: it is its own decision, an event table, because
   this record is a last value and not a series;
   - *Pointer, 2026-10-17 (#2166, part 2), correcting this item for "Aktiva användare".* The CTO corrected its own reason in the part-2 form round. That card counts windows that end now (today, the last 7 days
     and the last 30 days), which a last-value record could answer once a window lies after the start of recording (D1's instant). Only "Inloggningar" (today, yesterday, failed attempts) needs a series. Both
     cards stay unavailable because building them is out of scope, not because of the record's form.
6. the display precision changes from `HH:mm`: G changes with it (D4).
7. the box's `account_activity` table is recreated, by a Down or by a restore from a backup older than D1's instant: D1's instant and the web constant `ACCOUNT_ACTIVITY_RECORDED_FROM` are re-taken together
   (added 2026-10-17, #2166).

## Implementation

Part 1 is one PR on `feat/admin-users-activity`, and this ADR ships in it. The ADR sits in the ignored 0100–0199 range and is promoted with `git add -f` (precedent 0168–0170), because
the code comments point at "ADR 0172"; its README row goes in the same PR.

- **Part 1 carries:** the migration `AddAccountActivity`, the configuration, the port, the adapter and its claim, the middleware and its place after `UseAuthentication()`, the call in
  `PasswordlessSessionGrant`, the architecture pin on the port's consumers, the privacy-policy items with the version bump (`content-legal.json`, sv and en), and this ADR.
- **Done when:** `migrate` has applied the migration at reconcile; each sign-in writes both columns; there is at most one request write per account per minute; with a failure
  injected, login and request still succeed; a profileless account gets no row; the hard-delete oracle is green and mutation-verified; a post-deploy read-only count on the box shows
  a row for Klas's account.
- **Tests (CTO conditions 4 to 6):** the claim and the stamps come from `IDateTimeProvider`, and tests drive a fake clock across a minute boundary; sign-in rows are asserted through
  the real grant, never a hand-created session (AGENTS.md §5, Tests); each new SQL branch runs against the migrated schema (ADR 0151 D6).
- **Part 2 carries:** the LEFT JOIN in the directory with the two columns appended, the entry, DTO and wire, the "Senast aktiv" sort, the web, the preview fixtures, the renamed
  wire-pin test, the amendments to ADR 0150 (its D8 rows) and ADR 0151 (D1, D3, D4), and the measured start of recording as a dated pointer in D1 of this ADR.
- **Owed outside the diff:** the register field note in the main copy (D8), and one comment on #1984 that points at D10.
- **Panel for part 1 (CTO):** `db-migration-writer`, `security-auditor`, `dotnet-architect`, `code-reviewer`, `test-writer`.

## Klas's answers, carried verbatim

The CTO's three questions to Klas are quoted in Swedish, as the ruling relays them, without edits (diffed against `docs/reviews/2026-10-17-admin-activity-form-cto.md`, "For Klas").
Klas answered each by AskUserQuestion on 2026-10-17: (1) "Ja, båda (Rek.)", (2) "GO (Rek.)", (3) "Ja, ta över (Rek.)". The second is the migration GO, and its scope is the text
quoted.

> 1. Källa för "Senast inloggad" och "Senast aktiv": vi sparar två tidpunkter per konto från och med driftsättningen. Den första är när kontot senast loggade in (varje lyckad inloggning, även registrering). Den andra är vilken minut kontot senast gjorde ett anrop mot tjänsten (även webbens bakgrundsuppdatering medan en flik är öppen och synlig). Bara senaste värdet sparas, ingen historik, och det raderas när kontot raderas. Integritetspolicyn får en egen mening om det. Konton som inte varit inne sedan driftsättningen visar "–" tills de kommer tillbaka, och inget fylls i i efterhand. Godkänner du det?
> 2. Migrations-GO för exakt `AddAccountActivity` (AppDbContext, körs automatiskt av `migrate` vid nästa reconcile). Den skapar tabellen `account_activity` med kolumnerna `job_seeker_id` (primärnyckel, FK till `job_seekers`, ON DELETE CASCADE), `last_login_at` och `last_active_at`. Inget annat ändras och ingen data fylls i.
> 3. `hotspot:i18n` innehas fortfarande av #1977 (Codex-spåret), trots att dess PR är mergad. PR 1 behöver `content-legal.json` (sv+en) för policymeningen. Får det här spåret ta över den?

**Part 2 (#2166), 2026-10-17.** The CTO's two questions to Klas for part 2 are quoted in Swedish, as the part-2 ruling relays them, without edits (diffed against
`docs/reviews/2026-10-17-admin-activity-pr2-form-cto.md`, "For Klas (relay verbatim)"). Klas answered each by AskUserQuestion on 2026-10-17: (1) "Ja, ?-knapp med den texten (Rek.)", (2) "Ja, ta över (Rek.)".
The first approves the text and the form quoted, which D7's pointer describes. The second lets this track take over `hotspot:i18n` from #1977 for part 2 and release it when the PR merges.

> 1. Förklaringen av Senast inloggad och Senast aktiv i användarlistan: en ?-knapp vid kolumnrubrikerna öppnar en ruta med rubriken ”Senast inloggad och Senast aktiv” och texten ”Tidpunkterna sparas från och med 2026-10-17 20:49. Saknas en tidpunkt har inget sparats sedan dess. I Senast aktiv ingår också automatiska uppdateringar medan en flik med tjänsten är öppen och syns.” Tiden är mätt på lådan i dag: då startade API:t som skriver tidpunkterna. Tomma celler visar strecket som i dag. Godkänner du det?
> 2. Texten kräver fyra nya nycklar i messages/sv/admin.json och messages/en/admin.json. hotspot:i18n innehas fortfarande av #1977 (Codex-spåret, tilldelad dig). Får det här spåret ta över den för #2161 del 2 och släppa den när PR:en är mergad?

## References

- `docs/reviews/2026-10-17-admin-activity-form-cto.md` (the ruling, local)
- Code, at `911e48268`: `src/Jobbliggaren.Application/Auth/LoginChallenges/PasswordlessSessionGrant.cs` · `src/Jobbliggaren.Infrastructure/Auth/Sessions/AccessControlledSessionStore.cs` ·
  `src/Jobbliggaren.Infrastructure/Persistence/Configurations/CompanyWatchCriterionMemberConfiguration.cs` (the form D3 copies) ·
  `tests/Jobbliggaren.Worker.IntegrationTests/Auth/HardDeleteAccountsJobIntegrationTests.cs` (D9's oracle) · `docs/runbooks/vps-deploy-stack.md` §3c
- ADR 0013, 0024, 0045, 0120 (clause 5), 0139 (Major 5a and 5b), 0142 (D2, D3/D4), 0146 (D1), 0150 (D8), 0151 (D1, D3, D4, D6, D8), 0155 (D4) · AGENTS.md §2.1, §3.6, §5 ·
  CLAUDE.md §6.5, §9.2, §9.6
- The CTO record's citations: Martin 2017, ch. 7 and 13 · Evans 2003, "Aggregates" · Vernon 2013, ch. 10 · Fowler 2003, "AnemicDomainModel" · The Twelve-Factor App, VI ·
  GDPR Art. 5(1)(c) and (e), 6(1)(f), 13(1)
- PostgreSQL documentation, "Explicit Locking", row-level locks (`FOR KEY SHARE`, `FOR NO KEY UPDATE`): the lock modes D6 relies on; not re-read for this ADR
