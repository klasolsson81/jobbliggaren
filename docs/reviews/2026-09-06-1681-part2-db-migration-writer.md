# db-migration-writer review — #1681 part 2 (ADR 0139), `criteria_fingerprint`

**Scope reviewed:** `20260906182044_AddCriterionMaterialisationFingerprint.cs` + `.Designer.cs`,
`AppDbContextModelSnapshot.cs`, `CompanyWatchCriterionMaterialisationConfiguration.cs`,
`CompanyWatchCriterionMaterialisation.cs`, `CriteriaFingerprint.cs`, plus the write path
(`CompanyWatchCriterionMemberStore.ReplaceAsync`/`AnalyzeAsync`) and the read path
(`CompanyWatchBrowseQuery.cs`) that consumes the column. Branch `feat/1681-read-path`, base
`c2d782e8`, worktree `C:\tmp\jbl-1681c` (no changes made — read-only review, nothing committed,
nothing applied to any database).

**Verdict: 1 Major, 0 Blocker.** The migration itself (schema, backfill, DROP DEFAULT, type,
index absence, ANALYZE, write path) is sound on all six points asked. The Major is a read-path
defect one layer up (`CompanyWatchBrowseQuery.cs`) that silently defeats the column's stated
purpose for one of its two states — reported here because it is exactly the failure mode item 1
asked me to rule out, and because it directly undercuts the migration's own docblock claim.

---

## 1. The backfill (`defaultValue: ""`)

Reasoning holds **for the `Materialised` branch**, and is unqualified as written for the
`TooBroad` branch — see Finding A below, which is a read-side defect, not a migration defect.

- `CriteriaFingerprint.Of` always emits `Convert.ToHexStringLower(SHA256.HashData(...))` — exactly
  64 lower-case hex characters, never empty, confirmed by reading
  `Jobbliggaren.Application/CompanyWatches/Abstractions/CriteriaFingerprint.cs:69-91`. `''` is
  therefore unreachable as a genuine value and safe as a sentinel.
- For a backfilled row with `state = Materialised`, the read (`ReadAdCountAsync` /
  `ListActiveAdIdsAsync` in `CompanyWatchBrowseQuery.cs`) compares `storedFingerprint` (`''`)
  against the live `fingerprint.Value` (64 hex) with `string.Equals(..., Ordinal)`, which is always
  `false`, and correctly degrades to `NotMaterialised`. Confirmed by reading the comparison at
  `CompanyWatchBrowseQuery.cs:432-436` and `:472-473`.
- For a backfilled row with `state = TooBroad`, the fingerprint comparison is **never reached** —
  see Finding A. The row is read as `TooBroad` (an answer), not as unknown, for as long as it
  survives. In practice this window is bounded: `CompanyWatchCriterionMaterialiser.MaterialiseAsync`
  walks *every* saved criterion on *every* completed run (`CompanyWatchCriterionMaterialiser.cs:83-90`,
  paginated `OrderBy(c => c.Id)` over the whole table, no filtering) and unconditionally
  `ReplaceAsync`s each one, so the backfilled `''` is overwritten with a real fingerprint at the very
  next scheduled run regardless of state. The migration's "next run rewrites it and self-heals"
  claim is therefore true in the end, but the docblock's stated mechanism (equality-check failure)
  is not what makes it true for `TooBroad` rows — silence from the short-circuit is what makes it
  temporarily wrong instead.

No action needed in the migration for this point; the backfill choice (`''`, drop the default) is
correct. The docblock's claim ("every pre-existing row fails the read's equality check") is
one word too strong — "every Materialised row" would be accurate; TooBroad rows survive on the
short-circuit, not the equality check. Minor wording point, not gating.

## 2. `DROP DEFAULT`

Correct, and raw SQL is the right mechanism — there is no Fluent API surface for "add a column
with a migration-only backfill default, then remove the default from the model going forward"; the
default here was never expressed in `CompanyWatchCriterionMaterialisationConfiguration.cs` (no
`.HasDefaultValue(...)` call), so it exists **only** as an `AddColumn(..., defaultValue: "")`
argument — a migration-time constant, not a persisted model default. Confirmed the snapshot carries
no default annotation for `CriteriaFingerprint` (`AppDbContextModelSnapshot.cs` diff — `IsRequired()`
+ `HasMaxLength(64)` + `HasColumnType` + `HasColumnName`, nothing else), so migration and model agree:
nothing will re-propose a default on the next `migrations add`.

Generated SQL (`dotnet ef migrations script 20260906125527_AddCompanyWatchCriterionMembers
20260906182044_AddCriterionMaterialisationFingerprint`), reproduced in full:

```sql
START TRANSACTION;
ALTER TABLE company_watch_criterion_materialisations ADD criteria_fingerprint character varying(64) NOT NULL DEFAULT '';

ALTER TABLE company_watch_criterion_materialisations ALTER COLUMN criteria_fingerprint DROP DEFAULT;

INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20260906182044_AddCriterionMaterialisationFingerprint', '10.0.10');

COMMIT;
```

Both statements run inside the same transaction, no `CONCURRENTLY`, no `suppressTransaction` —
correctly so, since this is a plain `ALTER TABLE`, not an index build (the `CONCURRENTLY` protocol
in the charter's own worked case does not apply here). `ADD COLUMN ... DEFAULT ''` on PG 11+ is a
catalog-only operation for a constant default (no table rewrite, `atthasmissing`/`attmissingval`);
`DROP DEFAULT` afterward only removes the column's *current* default expression and does not touch
that captured missing-value metadata, so old rows keep reading `''` virtually until an actual write
touches them — exactly the intended backfill behaviour, and cheap regardless of table size (moot
here: one row per criterion).

**`Down()`** is correct and needs no symmetry fix: it `DropColumn`s the whole column, which removes
the NOT NULL constraint and whatever default state existed together with the column — there is
nothing left to restore.

## 3. Column type and width

`character varying(64)` / `HasMaxLength(Application.CompanyWatches.Abstractions.
CriteriaFingerprint.Length)` in the configuration, `character varying(64)` / `maxLength: 64`
hard-coded in the generated migration and Designer snapshot. Single-sourcing the *configuration*
from `CriteriaFingerprint.Length` is sound practice and the migration hard-coding `64` is not a
drift risk in the way it would be for an arbitrary business constant: `CriteriaFingerprint.Length`
is pinned to the output width of SHA-256 hex encoding (`Convert.ToHexStringLower(SHA256.HashData(...))`
is always 64 chars), so the only way the two numbers could disagree is a future digest algorithm
change — which is itself a schema change requiring a new migration to be generated (standard EF
workflow: migrations are point-in-time snapshots, not live references to the constant). No open
drift today. One nice-to-have, not required: a `CHECK (length(criteria_fingerprint) = 64)`
constraint would catch a future programmer error (a write path other than `ReplaceAsync` writing a
short string) at the database rather than at the next silent read-side degrade — optional
hardening, not gating, since the write surface for this column is already closed by construction
(only `CompanyWatchCriterionMemberStore.ReplaceAsync`, bound from `CriteriaFingerprint.Of`, per
`ErasureCascadeRegistry.cs:441-451`).

## 4. Index

Agreed: no index is owed. Every consumer (`MaterialisedAdCountSql`, `MaterialisedAdIdSetSql`) is
driven `FROM company_watch_criterion_materialisations m ... WHERE m.criterion_id = @criterion_id`,
and `criterion_id` is the table's primary key (`builder.HasKey(m => m.CriterionId)` in the
configuration) — the PK lookup already selects at most one row before `state`/`criteria_fingerprint`
are ever evaluated (as a `CASE WHEN` guard in the count statement, or as a post-read C# comparison
in both). Filtering a singleton row costs nothing extra regardless of column cardinality. Nothing in
the codebase queries this table by `state` or `criteria_fingerprint` alone. Confirmed by reading
`CompanyWatchBrowseQuery.cs:285-345` and `CompanyWatchCriterionMaterialisationConfiguration.cs:81-84`
(whose comment reaches the identical conclusion). No index recommended.

## 5. ANALYZE discipline (AGENTS.md §3.6)

No change needed. `CompanyWatchCriterionMemberStore.AnalyzeAsync` (`CompanyWatchCriterionMemberStore.cs:267-276`)
runs plain, unqualified `ANALYZE public.company_watch_criterion_materialisations;` — `ANALYZE` with
no column list refreshes statistics for every column of the table, the new `criteria_fingerprint`
column included automatically. Nothing to add or change in that method for this migration. The
three §3.6 conditions (one periodic writer, read-only between runs, a column reaching a `WHERE`/join)
continue to hold for the table as a whole; the new column does not need to reach a predicate itself
to be covered by the existing unqualified call.

## 6. The write path (`ReplaceAsync`)

Correct on both counts checked:

- **`ON CONFLICT (criterion_id) DO UPDATE SET`** includes
  `criteria_fingerprint = EXCLUDED.criteria_fingerprint` (`CompanyWatchCriterionMemberStore.cs:227`),
  alongside every other column. Confirmed this is not merely present in the INSERT column list —
  it is also in the conflict-update list, which is the one EF migrations can't check for you and the
  one place a copy-paste of the INSERT while missing the UPDATE arm would go unnoticed (exactly the
  bug class the sibling integration test `Materialise_RestampsTheFingerprint_WhenThePredicateChanges`
  exists to catch, and it does exercise the UPDATE arm, not just INSERT).
- **Parameter binding**: `stateCmd.Parameters.AddWithValue("@criteria_fingerprint", NpgsqlDbType.Text,
  criteriaFingerprint.Value)` (`:241`). Binding a `varchar(64)` column via `NpgsqlDbType.Text` is the
  established convention already used one column over for `state` (`character varying(20)`, bound the
  same way at `:230`) — consistent, not a new pattern, and a safe one: Npgsql sends the parameter as
  text and Postgres applies the implicit assignment cast into the `varchar(64)` target, enforcing the
  length constraint at the database regardless of the client-side binding type.
- Confirmed the `TooBroad` guard clause (lines 170-176) still fires correctly and is unrelated to
  the fingerprint addition — a `TooBroad` materialisation with a non-empty member list still throws
  before reaching the fingerprint write.

---

## Finding A — Major: the read path never checks the fingerprint for `TooBroad` rows, defeating the column's stated purpose for that state

**Where:** `src/Jobbliggaren.Infrastructure/CompanyRegister/CompanyWatchBrowseQuery.cs`,
`ReadAdCountAsync` (~429-436) and `ListActiveAdIdsAsync` (~469-473). Not a file in
db-migration-writer's edit scope (Persistence/Migrations, Persistence/Configurations only) — reported,
not fixed here.

**What:** Both methods read `state` and `storedFingerprint` off the single PK-selected row, then:

```csharp
if (state == MaterialisationState.TooBroad.ToString())
    return MaterialisedAdCount.TooBroad;   // <-- returns BEFORE the fingerprint is ever compared

if (!string.Equals(storedFingerprint, fingerprint.Value, StringComparison.Ordinal))
    return MaterialisedAdCount.NotMaterialised;
```

The `state == TooBroad` branch returns unconditionally, before the fingerprint equality check that
follows it. The fingerprint is compared **only** on the path that falls through to a `Materialised`
row. This means: once a criterion's materialisation is refused as `TooBroad`, *any subsequent edit
to that criterion's predicate* (including narrowing it below the breadth gate) is invisible to every
read until the next completed materialisation run — the surfaces keep saying "för bred" for a
predicate that may no longer be too broad at all.

**Why this is the exact failure the column exists to prevent, not a tangential gap:** three
independent pieces of this same PR say, in nearly the same words, that this must not happen:

- `CompanyWatchCriterionMaterialisation.cs:86-90` (the entity docblock): *"Written on EVERY path,
  including TooBroad. A refused criterion has no members, but it still has a predicate — and the
  refusal itself must stop applying once that predicate changes, or a user who narrowed a too-broad
  watch would keep being told it is too broad until the next daily run."*
- `CompanyWatchCriterionMemberStore.cs:237-239` (the write-site comment): *"Written on EVERY path,
  TooBroad included: a refusal is about a PREDICATE, and it must stop applying the moment that
  predicate changes."*
- `CompanyWatchCriterionMaterialisationTests.cs:676-701`
  (`Materialise_StampsThePredicatesFingerprint_OnTheTooBroadPathToo`), whose own comment says:
  *"Without the stamp here, a user who narrows a too-broad watch keeps being told it is too broad
  until the next nightly run — the refusal would outlive the predicate that earned it."*

The write side does exactly what all three say (verified: the fingerprint is stamped on the
`TooBroad` path, and the integration test proves it lands in the column). The read side then never
uses that stamp for this state, so the very outcome all three passages describe as the reason the
column exists is exactly what happens today: a narrowed watch keeps reading `TooBroad` for up to
~24h (`CompanyWatchCriterionMaterialisationWorker.cs:24-31` establishes this is a nightly job, kept
on Hangfire's default retry specifically because "a transient DB blip must not turn into a full day
of stale membership" — the same day-long window this ordering bug reintroduces for a different
reason).

**No test catches it.** I checked every test that touches `TooBroad` plus a fingerprint:

- `CompanyWatchBrowseQueryPlanTests.AdQueries_ReportTooBroad_WhenTheBreadthGateRefusedTheCriterion`
  uses `fingerprint = CriteriaFingerprint.Of(broadSpec)` — the *matching* fingerprint. It proves
  `TooBroad` is reported correctly when the fingerprint agrees, not what happens when it doesn't.
- `CompanyWatchBrowseQueryPlanTests.AdQueries_ReportNotMaterialised_AfterThePredicateIsEdited_NeverTheOldNumber`
  exercises the edited-predicate scenario, but only starting from a `Materialised` row
  (`SeededContextWithAdsAsync`), never from a `TooBroad` one.
- Every other place `TooBroad` and a fingerprint appear together
  (`CriterionMatchingAdSetResolverTests`, `GetCriterionAdMagnitudeQueryHandlerTests`,
  `GetMyMatchingAdCountForCriterionQueryHandlerTests`) stubs `ICompanyWatchBrowseQuery` directly with
  NSubstitute, so none of them can see this ordering bug — it lives entirely inside the concrete
  `CompanyWatchBrowseQuery` implementation those tests replace.

There is no test — unit, integration, or plan — for "a `TooBroad` row whose stored fingerprint no
longer matches the criterion's current predicate." That is precisely the gap that let the write-side
correctness (which *is* tested) mask the read-side regression.

**Why Major, not Blocker:** the failure direction is conservative, not exposing — no wrong number is
ever rendered, no PII leak, no accuracy defect in the sense of asserting a false magnitude (Art.
5(1)(d) as invoked by the design's own reasoning is about false *numbers*, and this bug never
produces one). What it produces is a wrong *refusal reason* for up to one materialisation cycle: a
user who did exactly the right thing (narrowed an over-broad watch) is told nothing has changed. That
is a real, reproducible, user-facing correctness defect in the feature this migration exists to
support, confirmed by reading the code and by a checked absence of test coverage — not a suspicion.

**Suggested fix** (for whoever owns `CompanyWatchBrowseQuery.cs` — outside my edit scope): move the
fingerprint comparison ahead of the `TooBroad` check, e.g.:

```csharp
if (!string.Equals(storedFingerprint, fingerprint.Value, StringComparison.Ordinal))
    return MaterialisedAdCount.NotMaterialised;

if (state == MaterialisationState.TooBroad.ToString())
    return MaterialisedAdCount.TooBroad;
```

in both `ReadAdCountAsync` and `ListActiveAdIdsAsync`, plus a `CompanyWatchBrowseQueryPlanTests` case
seeding a `TooBroad` row and then asserting `NotMaterialised` against an edited-predicate fingerprint
(mirroring `AdQueries_ReportNotMaterialised_AfterThePredicateIsEdited_NeverTheOldNumber`, starting
from `AdQueries_ReportTooBroad_WhenTheBreadthGateRefusedTheCriterion`'s seed instead). Routing this
finding is not mine to decide (§9.6) — recorded here per the review's instructions; the reordering
touches only `CompanyWatchBrowseQuery.cs` and its test file, neither of which is in
db-migration-writer's Write/Edit allow-list.

---

## Summary

| # | Item | Verdict |
|---|---|---|
| 1 | Backfill `defaultValue: ""` | Sound for `Materialised`; docblock overstates for `TooBroad` (see Finding A, which is a read-path bug, not a migration bug) |
| 2 | `DROP DEFAULT` via raw SQL | Correct mechanism, correct placement, `Down()` symmetric |
| 3 | Type/width single-sourcing | Sound; effectively drift-proof (SHA-256 fixed width); optional `CHECK` constraint as nice-to-have |
| 4 | Index | Correctly omitted — PK lookup already selects the single row |
| 5 | ANALYZE | No change needed — unqualified `ANALYZE` already covers the new column |
| 6 | Write path (`ReplaceAsync`) | Correct — upsert includes the column on both INSERT and conflict-UPDATE, binding matches existing `state`-column convention |
| A | Read path `TooBroad` short-circuit | **Major** — bypasses the fingerprint check the column exists to provide, for up to one materialisation cycle (~24h), on every criterion that transitions out of `TooBroad`. Not in migration-writer scope to fix. |

**GDPR-kontroller** (per charter): N/A in the strict PII-column sense — `criteria_fingerprint` is not
personal data itself (closed-domain digest, `ErasureCascadeRegistry.cs:441-451`,
`NotRecruiterData`), carries no soft-delete/audit-column obligation of its own (it lives on a
derived-state row already covered by the parent criterion's cascade), and needs no encryption. The
migration adds no new PII surface. Finding A has an Art. 5(1)(d)-adjacent flavour by the design's own
framing (a stale answer), but in the conservative direction (a refusal outliving its cause), not the
exposing one — recorded as a correctness Major, not a GDPR Blocker.

**Recommendation:** the migration itself may proceed (additive, non-destructive, no user approval
needed under the destructive-migration protocol). Finding A should go to whoever owns
`CompanyWatchBrowseQuery.cs` under this PR — `dotnet-architect` or `code-reviewer` per the mandatory
panel — before `agents-done`, since it is a Major against code this same PR introduces.
