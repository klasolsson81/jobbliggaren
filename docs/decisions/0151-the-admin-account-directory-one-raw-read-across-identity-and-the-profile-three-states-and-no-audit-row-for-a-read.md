# ADR 0151 — The admin account directory: one raw read across Identity and the profile, three states, and no audit row for a read

**Date:** 2026-10-04
**Status:** Accepted
**Deciders:** `senior-cto-advisor` (the routing of the #1974 form round: every verdict marked unambiguous under
CLAUDE.md §9.2, with no escalation to Klas; `docs/reviews/2026-10-04-1974-form-cto.md`, local) · Klas Olsson (product
owner: the epic's scope and its rule that nothing fabricated reads as real, #1972 and ADR 0150) · inputs:
`dotnet-architect`, `security-auditor` and `design-reviewer`
(`docs/reviews/2026-10-04-1974-form-{dotnet-architect,security-auditor,design-reviewer}.md`, local)
**Amends:** ADR 0013 (a dated update retracts point 1 of its 2026-05-06 assessment) · ADR 0150 (four rows in its D8
register)
**Related:** ADR 0013 (the separate Identity context) · ADR 0022 (audit rows come from commands) · ADR 0024 (deletion
and erasure) · ADR 0028 (admin authorization) · ADR 0047 (flow comprehension) · ADR 0048 (a context-crossing read goes
behind a port; query filters are the one definition of live) · ADR 0120 (a rendered count is true, or it is absent) ·
ADR 0142 (passwordless auth; D7: the account has no name) · ADR 0150 (the admin surface; D2, D4, D8) ·
[#1972](https://github.com/klasolsson81/jobbliggaren/issues/1972) (the epic) ·
[#1974](https://github.com/klasolsson81/jobbliggaren/issues/1974) (the issue this ADR ships under) · #1976 (suspension)
· #1977 (scheduled deletion) · #1983 (restore, send login link, mark verified, role) · #1984 (impersonation)
**Measured against:** `origin/main` at `4cedf5e60`, 2026-10-04 (the base of the #1974 branch, whose code this reads).
D7's reading was taken on the dev box at 16:44:38 UTC the same day.

> **Provenance.** Written by `adr-keeper` for the driving session, from its brief and the form-round record (CLAUDE.md
> §9.2, §13). The epic's scope, and its rule that nothing fabricated reads as real, are Klas's (#1972, ADR 0150). The
> decisions are `senior-cto-advisor`'s routing of the #1974 form round over the three agent memos; D7's ground is
> `security-auditor`'s §4. The mandatory agents review them in the PR (CLAUDE.md §9.2).

---

## Context

**The first real data behind the admin surface.** ADR 0150 D4 gives #1974 one entry: listing and inspecting accounts.
ADR 0150 measured at `7c21f8117` that `/api/v1/admin` had no account-administration endpoint, and its D5 shows the
interactions that need rows only over fictional fixtures.

**An account is two records in two contexts.** Identity (schema `identity`, `AppIdentityDbContext`) owns the address,
its normalised form, `email_confirmed` and the roles. The domain (schema `public`, `AppDbContext`) owns whether a profile
exists (`job_seekers`), its `created_at` and `deleted_at`, and every aggregate the account owns. No foreign key joins
them (ADR 0013). An account's status is a function of both, so filtering and counting every account in bounded
statements takes a join across the two.

**ADR 0013 rested a decision on the code never writing that SQL.** Point 1 of its 2026-05-06 assessment of Identity's
PascalCase table names, the reason they were not renamed, is that the tables are reached only through `UserManager` and
`SignInManager`, with no raw SQL against them. `dotnet-architect` measured on 2026-10-04 that this held: no raw SQL
against an Identity table exists in `src/` outside migrations, and the orphan sweep (`AccountHardDeleter`, ADR 0024 D6)
declines the join by materialising both sides and diffing in C#. Every Identity port the code has serves one consumer
and is pinned to it; none lists or filters accounts.

**The constraints.** The issue forbids an unbounded fetch and keeps the search term, which is an address, out of URLs and
logs. An admin read crosses every account by design. And the audit mechanism cannot persist a row for a query:
`AuditBehavior` acts only on commands and `UnitOfWorkBehavior` saves only for `ICommand` (ADR 0022).

## Decision

Eight decisions govern the account directory, each in one place.

### D1 — A port, and one raw read behind it

`IAccountDirectory` (`Application/Admin/Accounts`: `SearchAsync`, `CountByStatusAsync`, `FindAsync`) is implemented by
`SqlAccountDirectory` (`Infrastructure/Admin/Accounts`): raw, parameterised, read-only SQL on `AppDbContext`'s connection,
with its own 30 s command timeout, in the `CompanyRegisterSearchQuery` form. One composer builds the counts, the page
count and the page over one CTE, `identity."AspNetUsers"` left-joined to `public.job_seekers`, so all three read one
predicate. The term is an escaped `LIKE '%…%'` over Identity's normalised address, folded with
`ILookupNormalizer.NormalizeEmail`, and is bound only as a parameter. The Admin role name (`NormalizeName(Roles.Admin)`)
and the status names are bound values too. The directory is registered with Identity, in the Api composition only. ADR
0013 carries a dated update that retracts point 1 of its assessment and points here; its conclusion stands on points 2
and 3.

*Why.* It is the house's tested form for this exact shape: optional predicates, an `ORDER BY` mapped from an enum, and
count-first paging. A read that crosses a bounded-context boundary goes behind a port (ADR 0048 (b); Martin 2017, ch. 11
and 22), which keeps Identity's shape out of Application. One composer gives the count and the page a single predicate
source (Hunt/Thomas 1999). The term is folded with Identity's own normaliser because any other case-fold is effectively a
different rule, and it is a parameter so that it never becomes part of the SQL text (D6). The directory is registered
with Identity because it uses Identity's normaliser.

### D2 — Three states, a separate flag, and the filters that follow

`AccountStatus` is Active, PendingDeletion or ProfileMissing, by the login classifier's rule (`LoginSubjectResolver`): no
profile outranks a soft-deleted profile, which outranks a live one. The directory's SQL `CASE` is a second home for that
rule, and an integration test holds the two together. #1976 adds Suspended just above Active, so a pending deletion
outranks a suspension; there is no Suspended member before then, because a value nothing can produce would present an
unbuilt capability as built (ADR 0150 D2, D4).

`emailConfirmed` is its own boolean, never a status, a filter or a count: it can go with every state, and only the
retired password registration (ADR 0142) ever left it false. The role is one definite `EXISTS` over the Admin role, so
every account is Admin or User and no row shows an unknown role.

Enums cross the wire as their .NET names, one `JsonStringEnumConverter<T>` per enum. The web maps them at one boundary,
and its response schema accepts Suspended before the API sends it: a tolerant reader (Fowler 2011), in the order ADR 0142
used for A0 and B0.

The filters are "Alla", "Aktiva", "Under radering" and "Ofullständiga", a partition that adds up to "Alla" (ADR 0047); a
known zero shows "(0)", and counts that failed to load show no number (ADR 0120, ADR 0150 D2). "Suspenderade" returns
with #1976. The copy is "Ofullständig" for no profile and, as lines under the state's dot, "E-post ej bekräftad" and
"Slutgiltigt tidigast {YYYY-MM-DD}".

*Why.* One rule, not two classifications: the directory reuses the login classifier's, and the parity test keeps the SQL
honest when #1976 or #1977 change what a status means. "Ofullständig" describes the record, not the person, and is true
of both things a missing profile can be: a registration still in flight inside the orphan sweep's grace window, and an
orphan the next sweep removes (ADR 0024 D6). "Ofullständiga" stays even at zero, because without it "Alla" is not the sum
of the visible counts.

### D3 — One source for each date

The registration time is `job_seekers.created_at` and nothing else. An account without a profile has none: the ledger
shows "–", and an unknown time sorts last in both directions. Identity's `created_at`, which older rows hold as a 1970
sentinel, is never read. The earliest permanent deletion is `AccountRestoreWindow.PermanentDeletionEarliest`, called and
never re-derived, and the wire carries it as `YYYY-MM-DD`.

*Why.* One definition beats a field built from two sources, for a state the orphan sweep removes anyway, and a sentinel
must never render as a date or sort as the oldest account. The hard-delete job removes an account at its first run after
the window ends, so a date is all the UI can promise, and a second computation of the window would give retention a
second source of truth.

### D4 — Activity counts: active accounts only, read where "live" is defined

The list shows an application count; the detail shows applications, CVs and saved searches. Only an active account gets
any. A pending-deletion or profileless account gets none: null on the wire, "–" in the ledger, no count rows in the
panel. The handlers read the counts through `IAppDbContext`: one `GroupBy` over the page's profiles for the list, one
projection for the detail, each awaited after the directory and never alongside it, because the raw command and EF share
one connection.

*Why.* EF's query filters are the one definition of a live application, CV or saved search, and a manual `deleted_at`
predicate beside them is the duplicate ADR 0048 (c) forbids. The two deletion paths leave different rows live: the user's
own deletion soft-deletes applications and resumes and leaves saved searches, saved ads and watches live, and the runbook
path writes only `job_seekers`. A count over a pending deletion would describe the path, not the account. A profileless
account has no owner key to count by. A count is true or it is absent (ADR 0120).

### D5 — Two endpoints, one bucket, and a BFF that keeps the term and the id out of the browser's URLs

`POST /api/v1/admin/accounts/search` answers `{accounts, counts}`: two Mediator sends, one response and one rate-limit
token, the counts following the term but not the status filter. `GET /api/v1/admin/accounts/{id:guid}` answers the
details or a plain 404, as a nullable result. Both require the Admin policy and `IAdminRequest` (ADR 0028), set
`Cache-Control: private, no-store` before the query runs so that a failure carries it too, and share the `admin-read`
token bucket per admin: 30 per 10 s in 6 segments with no queue, as starting values. `RateLimitingOptions.AdminRead` owns
the numbers and their derivation from request frequency and per-request cost, and `security-auditor` verifies them. The
search term never enters a URL or a log. An account id is a loggable identifier by house doctrine (`LoggingScopeBehavior`)
and travels in the backend's path, so it may reach server logs, and nothing claims otherwise.

The web reads through two BFF route handlers, `POST /api/admin/konton` and `POST /api/admin/konton/detalj`: same-origin
and JSON only, a 401 without any backend call when there is no session, the backend's 403 as the authority, fixed
response codes, `no-store` on every branch, and the request's signal passed on. From the browser, the term and the
account id travel only in request bodies.

*Why.*

- A settled search costs one round trip and one token, and the two queries stay separate.
- The search is a POST because the term is an address; POST-as-read is the house exception for sensitive terms, not a
  default. The detail is a GET, a safe read of an identified resource (RFC 9110 §9.2.1), because its id is not the term.
- Search, counts and detail are one cost class and share a bucket, apart from `AdminWrite` so that typing never starves
  #1976 or #1977, and apart from the existing admin GETs (the audit log, the background jobs), whose cost class and
  change-reason differ and which a shared bucket would let starve the search. The limiter runs after authorization, so a
  non-admin never spends a token and the limiter never stands in for the policy.
- The `(admin)` layout's gate never runs for a route handler, so each handler checks the session itself. The same-origin
  and JSON checks close timing probes from a sibling origin ("does an account with address X exist?") and force a
  preflight from any other origin.

### D6 — Pins

- **Architecture test.** Every message under `Application.Admin` implements `IAdminRequest`; none implements
  `ICapturesRecentSearch`; and only the three admin account query handlers inject `IAccountDirectory`, checked across
  Application, Infrastructure, Api and Worker. *(Pointer, 2026-10-05, #1975: the consumer list is widened by one handler,
  the request of an address change, which reads the account's role, status and address fresh through the directory by the
  same rule the panel shows; the alternative was a fourth copy of the profile rule (ADR 0153 D5). "The three" above is
  the count on 2026-10-04 and is not edited.)*
- **Integration tests.** The status rule agrees with the login classifier for each state, each produced by a named actor.
  Every composed branch of the SQL runs against the migrated schema, so an Identity upgrade that renames the PascalCase
  tables, now names the code depends on, fails there. The term is in no log record, with the capture reading exceptions,
  state and scopes as well as the message, and is never in the SQL text. Both routes' rate-limit policy is read from the
  built endpoint graph.

*Why.* The directory can list every account's address, so it is the tool that would reopen the account-existence oracle
the login page closes (`security-auditor`, ADR 0142), and the admin gate sits on the message, not on the port: the radius
pin makes a new consumer a reviewed decision. A raw command bypasses EF's command log, so the SQL-text leak path needs its
own pin. An absence assertion that cannot fail is not a test, which is why the log capture was widened first.

### D7 — No audit row for an admin read, until a named trigger

#1974 writes no `Admin.AccountInspected` row. The current mechanism cannot persist one for a query (Context), and the
value of an audit of reads is detecting an admin who is not the controller (`security-auditor`). Measured on 2026-10-04
16:44:38 UTC on the dev box, read-only: one Admin role holder and two accounts, by

```sql
SELECT count(*) FROM identity."AspNetUserRoles" ur JOIN identity."AspNetRoles" r ON r.id = ur.role_id WHERE r.normalized_name = 'ADMIN';
SELECT count(*) FROM identity."AspNetUsers";
```

The one holder is the controller (Klas), so a read audit would record the controller reading on his own authority.

**Lapse trigger, whichever comes first:** an Admin role holder other than the controller, or #1984 (support
impersonation). Such a holder can arise through #1983's role actions, and through the bootstrap's residual (ADR 0028 §4);
the seeder's doc comment, #1983 and #1984 each carry a pointer to this trigger. **Reader: Klas.** The reading selects the
holders' ids, is re-taken at the trigger and is never inherited from this ADR; CLAUDE.md §9.6 holds a bearer-absence reading to the same rule.

**When it fires,** the audit of reads is decided against the new reading and, if kept, built in this form: event
`Admin.AccountInspected`, AggregateType `User`, AggregateId the target's id, `user_id` the acting admin, payload null;
detail reads only, never search or counts. Building it needs an amendment to ADR 0022. Erasure stays consistent:
`AuditTrailEraser` nulls by `user_id`, and the row keeps only an `aggregate_id` (ADR 0024). Once kept, such a log is
Art. 15(1) information (CJEU C-579/21).

**Until then,** no UI copy says that views are logged (ADR 0150 D2).

### D8 — Four trade-offs accepted, each with what reopens it

- **Raw SQL ties the directory to Identity's table and column names**, which ADR 0013 had avoided. D6's tests run every
  composed branch against the migrated schema, so an Identity upgrade that renames them fails there.
- **A profileless account shows "–" for registration time even where Identity holds a real time** (D3): one definition
  over a field from two sources, for a state the orphan sweep removes.
- **Account ids from detail reads reach server logs** (D5), which house doctrine accepts.
- **The substring search is a sequential scan**, because `EmailIndex` cannot serve a substring match. That is accepted at
  the two accounts D7 measured. **Trigger, whichever comes first:** D7's second query counts 10 000 accounts, or
  `LoggingBehavior` records a `SearchAccountsQuery` above ADR 0045's 300 ms p95 for class (a). Then the `admin-read`
  numbers are re-derived and a pg_trgm index is added. **Reader: Klas.**

## Alternatives considered

- **D1, a composite `SqlQuery<T>`.** LINQ composition of the count and the page from one base query is a real gain.
  Rejected: it would be the first use in the repo, how its column mapping behaves under `UseSnakeCaseNamingConvention` is
  unverified here, and it is a second idiom for a problem the house already solves one way (code is optimised for its
  reader, Winters et al. 2020, ch. 8).
- **D1, a keyless `AspNetUsers` mapping in `AppDbContext`.** Rejected: it changes the model snapshot, the migration hotspot
  (CLAUDE.md §6.5), and puts an Identity-shaped type beside `IAppDbContext`, which ADR 0013 kept apart.
- **D1, two EF reads and a diff in C#,** the orphan sweep's form. Rejected: it materialises every account, and the issue
  forbids an unbounded fetch.
- **D1, `count(*) OVER ()` for the total.** Rejected: a page past the end returns no rows and so loses the total.
- **D1, `strpos` for the match.** Rejected: a future pg_trgm index can serve `LIKE` and never `strpos`.
- **D2, `unverified` as a status, and an "Ej verifierade" filter.** Rejected: the flag can go with every state, so one
  status would hide one fact or the other; and an option that overlaps the others breaks the partition, so the counts
  stop adding up to "Alla" and the admin has to guess (ADR 0047). The actions that would use such a filter are #1983's,
  and a separate filter can come with them.
- **D2, a "Suspenderade" filter, shown as "(0)" or disabled.** Rejected: "(0)" is a true number that reads as "nobody is
  suspended" about a state that does not exist yet, and a disabled radio inside a live group has no sanctioned form (ADR
  0150 D2).
- **D2, camel-case wire names by `JsonStringEnumMemberName`.** Rejected: no overrides for one endpoint family; the web
  maps the .NET names at one boundary.
- **D3, falling back to Identity's `created_at` with the 1970 sentinel filtered out.** Rejected: it builds one field from
  two sources and needs a production sentinel constant.
- **D4, the counts in the port's SQL.** Rejected: a second `deleted_at IS NULL` beside the query filters is the drift
  `AccountHardDeleteCascadeFitnessTests` exists to catch.
- **D5, a `Result` with `DomainError.NotFound` for the detail's not-found.** Rejected: the house returns `T?` when
  not-found is the only failure and `Result` when `ErrorKind` has a real choice to make; the endpoint still answers a
  fixed body, throws nothing, and sets `Cache-Control` before the send.
- **D5, a POST for the detail, to keep the id out of logs.** Rejected: the acceptance criteria ban the term from URLs and
  logs, not the id, and POST-as-read stays the house exception for sensitive terms.
- **D5, a separate counts endpoint.** Rejected: two round trips and two tokens for each settled search.
- **D5, the existing admin GETs on the `admin-read` bucket.** Rejected for this PR: a different change-reason and cost
  class.
- **D7, writing the audit row now.** Rejected: the mechanism cannot persist one for a query without an ADR 0022
  amendment, and while the controller is the only Admin it would record the controller reading on his own authority.

## Consequences

### Positive

- The list, the page count and the status counts read one predicate in one place, and the counts statement fails loudly
  on a row no `CASE` arm takes, so an account cannot drop out of every count unseen.
- The search term has no path into a URL, a log record, the SQL text, a cache or the recent-search table, and a test that
  can fail holds each of them.
- Who may read every address is pinned: a new consumer of the directory fails the build until its gate and this ADR have
  been reviewed.
- #1976 and #1977 extend a status rule that already has a parity test, and the web already accepts Suspended.

### Negative and risks

- The status rule has two homes, the login classifier's C# and the directory's `CASE`. The parity test is the only thing
  that holds them together: a change to one fails it until the other follows, as #1976 and #1977 will meet.
- D8's four trade-offs stand, each with what reopens it.

## Implementation

Implemented under #1974, in the same PR as this ADR: the port and the directory (D1), the three queries and their
handlers (D2–D4), the two endpoints, the `admin-read` bucket, the two BFF handlers and the page (D5), and the pins (D6).
The PR also carries ADR 0013's dated update, ADR 0150's four D8 rows, the threat model's sentence on the directory, the
two account endpoint lines in BUILD.md §6.2 and the index row in `docs/decisions/README.md`. Amended later by #1976
(Suspended) and #1977 (scheduled deletion).

## References

- AGENTS.md §2.1, §3.6, §5 · CLAUDE.md §6.5, §9.1, §9.2, §9.6 · BUILD.md §6.2
- ADR 0013, 0022, 0024, 0028, 0047, 0048, 0120, 0142, 0150
- `docs/reviews/2026-10-04-1974-form-cto.md` and the three memos beside it (local)
- The CTO record's citations: Martin 2017, ch. 11 and 22 · Winters et al. 2020, ch. 8 · Fowler, "TolerantReader" (2011) ·
  Hunt/Thomas 1999 (DRY) · RFC 9110 §9.2.1 · the EF Core page "Querying unmapped types",
  `learn.microsoft.com/en-us/ef/core/querying/sql-queries` (read 2026-10-04)
- CJEU C-579/21, cited by `security-auditor`
- `src/Jobbliggaren.Infrastructure/CompanyRegister/CompanyRegisterSearchQuery.cs` (the form D1 copies) ·
  `src/Jobbliggaren.Application/Auth/LoginChallenges/LoginSubject.cs` (the classifier D2 follows) ·
  `src/Jobbliggaren.Infrastructure/Auth/AccountHardDeleter.cs` (the orphan sweep) ·
  `src/Jobbliggaren.Api/RateLimiting/RateLimitingOptions.cs` (`AdminRead`) ·
  `tests/Jobbliggaren.Architecture.Tests/AdminAccountDirectoryTests.cs` (D6's consumer pin)

## Amendment 2026-10-08 — Aggregate registrations and exact directory drill-down (#1978)

`IAccountDirectory.GetOverviewAsync` adds one bounded aggregate read, not an account-list fetch. Infrastructure
reuses its lifecycle CASE in a parameterized command returning totals and at most 90 day rows. The new
`GetAccountOverviewQuery` implements `IAdminRequest`; its endpoint requires Admin and carries private/no-store
on successful, failed and refused responses. No Identity/provider dependency is added to Application.

The retained Identity population is the total and lifecycle denominator. Registration periods require a matching
profile and compare its `created_at`, including soft-deleted/suspended profiles and excluding erased/profileless
accounts. The handler builds Swedish civil-day UTC windows independently through `ISwedishCalendar`, samples
the injected clock once and clamps the current day at that instant. See ADR 0150's dated amendment for period
semantics, observation provenance, projections and refresh behavior.

Search and count contracts accept paired, validated `registeredFrom` / `registeredBefore` UTC instants.
The directory applies the same half-open profile timestamp predicate before address/status filters.
Status counts follow address/date constraints across all lifecycle statuses. Equal bounds mean an empty window;
incomplete or reversed pairs are validation failures. No registration fallback to Identity creation time is added.

Overview totals/statuses/registration links supply exact dates/statuses. The directory preserves the period while
searching, changing status/sort and paginating; it displays the period and a clear action. Only dates/status enter
browser URLs. Address search and account detail IDs retain D5's body-only browser contract.

Migrated PostgreSQL tests exercise profile absence, status precedence, both DST transitions, retained registration
population, permanent erasure, and parity between aggregate windows and directory predicates. Auth tests cover
anonymous, ordinary, revoked-admin and direct query reads, plus private response headers. The consumer radius pin
explicitly admits the overview handler.
The account route's client message provider carries only settings.account.reauth for the shared step-up dialog,
alongside its required shared catalogs. The provider picker accepts literal subtree paths; the import-graph
fitness function checks coverage of full translation paths and rejects unused declarations. The shared admin
catalog stays shared with the outer provider. This bounds document transport without changing the dialog's
copy, command/security contract or the overview layout; the dated performance report records regeneration.