# dotnet-architect — #1736 (epic #1732 part 1b): PR #1751

**Date:** 2026-09-17 · **Agent:** dotnet-architect (panel review, fresh instance) · **PR:**
[#1751](https://github.com/klasolsson81/jobbliggaren/pull/1751) · **Head reviewed:** `402fe30a` ·
**Mode:** report-only, no edits, no builds · **Verdict:** 0 Kritiskt · 0 Viktigt · 2 Nice-to-have.
Report returned in the reply and transcribed verbatim by the session.

---

## Arkitektur-analys — PR #1751 (#1736, epic #1732 part 1b): the terms-acceptance stamp

**Date:** 2026-09-17 · **Agent:** dotnet-architect · **Mode:** report-only, no edits, no builds run · **Worktree:** `C:/tmp/jbl-1736`, branch `feat/terms-acceptance-1736` · **Range reviewed:** `88b6ea93..402fe30a` (4 commits, 185 files, +3665/−377; the migration Designer skimmed as generated)

---

### Sammanfattning

OK — no Kritiskt, no Viktigt. All five of my pre-code binds (A–E) and the CTO's Form A are executed as written, including the parts most likely to be dropped: the inner `IsRequired()` is absent, `Navigation(...).IsRequired(false)` is present, the factory is total with no dead `Create`, no `tests/Shared/TestTermsAcceptance.cs` was created, and the contact-address pin was actually refactored onto the shared walk-up rather than left to duplicate it. Two **Nice-to-have** findings, neither of which asks for a change to the delivered code.

**One measured deviation from bind B's literal text, and it is correct.** Bind B wrote `CurrentPrivacyPolicyVersion = "2026-08-28"`; delivered is `"2026-09-17"` (`src/Jobbliggaren.Domain/JobSeekers/TermsAcceptance.cs:38`). The bind's operative clause was *"the ISO date the published copy carries as 'Senast uppdaterad'"*, and commit `402fe30a` moves `privacy.updated` to `2026-09-17` in both locales because the policy now names the new data category. The constant follows the copy, which is the rule; `TermsAcceptanceVersionsMatchPublishedPolicyTests` pins it in both directions. Do not read this as drift against the bind.

---

### Bind-by-bind verification (not findings — the record that each was executed)

- **A (form + mapping).** `sealed record` with three non-nullable properties and a private name-matched ctor (`TermsAcceptance.cs:32-49`); `OwnsOne` with explicit `HasColumnName` on all three and `HasMaxLength(20)` on both version columns, **no inner `IsRequired()`**, followed by `builder.Navigation(js => js.TermsAcceptance).IsRequired(false)` (`src/Jobbliggaren.Infrastructure/Persistence/Configurations/JobSeekerConfiguration.cs:40-50`). The all-null-sentinel argument is written into the comment as bound (`:29-38`). Migration is three nullable `AddColumn`s / three `DropColumn`s, types `timestamp with time zone` and `character varying(20)` (`…/Migrations/20260917141433_AddTermsAcceptanceToJobSeeker.cs:14-48`), sequenced immediately after `20260914134635_AddOccupationDivisionProfile` (measured: it is the last migration in the folder). Snapshot marks the two version properties `IsRequired()` — that is EF inferring from the non-nullable CLR members inside the owned type and is consistent with nullable columns under an optional navigation, not a contradiction. ADR action item done: D6's `readonly record struct` sentence corrected in place and an `Amendment 2026-09-17` added (`docs/decisions/0142-…md:266-330`).
- **B (one total factory).** `AcceptCurrent(IDateTimeProvider)` only (`TermsAcceptance.cs:56-57`); no `Create`, no `Result`; `tests/Shared/` gained `ContentLegalMessages.cs` and nothing else. The two clock reads are kept separate and the reason is stated at the call site (`RegisterCommandHandler.cs:113-117`).
- **C (aggregate refusal).** `is null` on the non-nullable parameter, placed after the `Guid.Empty` guard and before `ValidateDisplayName`, code `JobSeeker.TermsAcceptanceRequired`, `DomainError.Validation` (`src/Jobbliggaren.Domain/JobSeekers/JobSeeker.cs:150-158`). Guard **order** is pinned, not just guard presence (`JobSeekerTests.cs` `Register_WithEmptyUserIdAndNullTermsAcceptance_ReportsTheUserIdFailure`). The event is unchanged and its field set is pinned reflectively. The two refusal messages were kept as two sentences, not extracted to one constant, as bound.
- **D (validator).** `RuleFor(c => c.AcceptTerms).Equal(true)` after the `DisplayName` rule (`RegisterCommandValidator.cs:28-29`); `AcceptTerms` sits before `RememberMe = false` and carries **no default** (`RegisterCommand.cs:18-19`). The Swedish `WithMessage` matches the repo's established validator convention (measured against `AddNoteCommandValidator`, `FollowCompanyCommandValidator`). Fail-closed-by-omission is pinned end to end, not only asserted in a comment (`tests/Jobbliggaren.Api.IntegrationTests/Auth/RegisterTests.cs:122-148`).
- **E (two pins, one walk-up).** New pin in Domain.UnitTests reading `terms.updated`/`privacy.updated` **by path** via `JsonDocument`, `EndsWith`, `[Theory]` over `sv`/`en`, plus an ISO-shape arm (`TermsAcceptanceVersionsMatchPublishedPolicyTests.cs:33-66`); walk-up in `tests/Shared/ContentLegalMessages.cs`, linked into both `.csproj`s; **the precedent's private walk-up was actually deleted** (`ContactAddressMatchesPublishedContactTests.cs`, −24/+3) rather than left as the duplicate the bind existed to prevent. No file-wide token sweep.

**Layer and pattern checks, all clean.** Domain's new file imports only `Jobbliggaren.Domain.Common` (`IDateTimeProvider` is measured to live at `src/Jobbliggaren.Domain/Common/IDateTimeProvider.cs` — AGENTS.md §2.1 holds). No EF Core, no provider package anywhere new in Application. No repository layer; the handler keeps using `IAppDbContext`. No `DateTime.UtcNow`, no magic string (both versions are named constants with a checkable source), no primitive obsession (the stamp is a VO, not three loose columns on the aggregate). `TermsAcceptance` is never projected past the Application boundary — measured: no DTO or query references it. The `TermsAcceptance TermsAcceptance` property/type name collision is the delivered `Preferences Preferences` form, not new.

**Sweep integrity, measured.** 324 `JobSeeker.Register(` in `tests` + 1 in `src` (baseline 321+1, +3 from the new tests, 2 of which pass `null!` deliberately). Of the call sites the regex `AcceptCurrent\((X)\), *(Y)\)` can pair, **all 316 pass the same clock to both arguments**; the remaining multi-line sites were read individually and match. The 17 hoists (`tests/Jobbliggaren.Worker.IntegrationTests/Security/CryptoErasureHardDeleteTests.cs:111-114` is representative) hoist a `FixedClock`, so they make two reads identical rather than changing behaviour. **No `TermsAcceptance` instance is shared across two `Register` calls anywhere** — measured across all 330 `AcceptCurrent(` occurrences in `tests` — which matters because a shared owned instance is exactly the failure `CompanyWatchCriterionConfiguration.cs:8-35` documents.

**Erasure/exposure registries.** The two version columns are `NotRecruiterData` with a `job_seekers:NotRecruiterData` ground (`ErasureCascadeRegistry.cs:507-508`, `:789-795`); `terms_accepted_at` is correctly **absent** — measured: the disposition dictionary carries no `*_at` column at all, so it is a text-column registry and a timestamptz needs no entry. `MappedPlaintextExposureRegistry.cs:121-127` extends the existing `job_seekers` ground rather than adding a key, which is right for a table already claimed.

---

### Fynd

**[Nice-to-have]** `BUILD.md:630-635`
**Vad:** §7.1's `job_seekers` sketch does not carry `terms_accepted_at`, `terms_version`, `privacy_policy_version`.
**Varför:** §7.1 is the only place in the tracked spec where a reader looks up what the table holds, and the three columns are the Art. 5(2) accountability record — the kind of thing that block exists to surface. **Pre-existing condition, and the delta only extends it:** the same block already omits at least seven delivered columns (`match_preferences`, `primary_resume_id`, `last_match_scan_at`, `last_seen_matches_at`, `last_seen_jobs_at`, `last_company_watch_scan_at`, `last_seen_followed_ads_at` — regenerate with `awk` over the `JobSeeker` entity in `AppDbContextModelSnapshot.cs` grepping `HasColumnName`). So this is not newly wrong, and it is not a §12 class.
**Föreslagen åtgärd:** three lines in the sketch, or a deliberate skip — I grade it, §9.6 routes it:

    terms_accepted_at (timestamptz null)     -- ADR 0142 D6: contract stamp, null only pre-1b
    terms_version, privacy_policy_version (varchar(20) null)

---

**[Nice-to-have]** `src/Jobbliggaren.Domain/JobSeekers/JobSeeker.cs:75` (read against `docs/decisions/0142-…md:278`)
**Vad:** D6 states *"NOT NULL for every row written after 1b is a property the write paths must hold, not a factory convention."* Today that property holds **by construction** — `Register` is the only factory and the 6-arg private ctor takes a non-nullable `TermsAcceptance`. The residual is `private JobSeeker() { }`, EF's materialization constructor, which is reachable from inside the class; a future in-class static factory (D8's OAuth first sign-in is the next one) could `new JobSeeker()` and set the private setters without a stamp. The delivered reflective guard (`tests/Jobbliggaren.Domain.UnitTests/JobSeekers/JobSeekerTests.cs:435-456`) covers a relaxed **setter** and a public **instance** method taking a `TermsAcceptance` — deliberately and correctly, since those are the mutations 1b can produce — but not a new sibling factory.
**Varför:** AGENTS.md §2.2 — an invariant the aggregate holds by having exactly one factory stops being an invariant the moment a second one appears, and a missing stamp is silent (a nullable column) rather than loud. Nothing in this delta is wrong; the obligation is created here and lands elsewhere.
**Föreslagen åtgärd:** none in this PR. When D8's OAuth first sign-in lands, it routes through `JobSeeker.Register` rather than a parallel factory; if a parallel factory is genuinely needed, the guard grows a third arm over **static** members:

    typeof(JobSeeker).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(m => m.ReturnType == typeof(Result<JobSeeker>))
        .ShouldAllBe(m => m.GetParameters().Any(p => p.ParameterType == typeof(TermsAcceptance)));

---

### Referenser
- AGENTS.md §2.1 (EF dependency rule, three axes — Domain clean, no provider package in Application) · §2.2 (invariants in the aggregate; state written once through an explicit path) · §2.3 (no domain object past the Application boundary) · §2.4 (InMemory harness — the reason `ComplexProperty` was rejected in the pre-code bind) · §3 (`record` VO, `private set`, `IDateTimeProvider`) · §5 (`Backend:` repository/`DateTime.UtcNow`/magic-string/primitive-obsession all clear; `Comments:` — no factually wrong comment found in the delta)
- CLAUDE.md §6.5 (EF migrations, single-owner serial hotspot — this is the first of 1b → 6d → 4a → 4b → 5b and sequences cleanly off `20260914134635`) · §9.6 (severity is mine; routing is the session's)
- `docs/reviews/2026-09-17-1736-architect-form.md` (binds A–E, all executed) · `docs/reviews/2026-09-17-1736-cto-signature.md` (Form A — `string? displayName` retained, verified at `JobSeeker.cs:142-145`) · `docs/reviews/2026-09-17-1736-db-migration-writer.md` (no findings raised, verified)
- ADR 0142 D6 + Amendment 2026-09-17 (`docs/decisions/0142-passwordless-auth-one-page-code-or-link-oauth-ready.md:266-330`)

---

**Verdict: 0 Kritiskt · 0 Viktigt · 2 Nice-to-have.** Nothing here blocks `agents-done` from my side.

---

## Scoped re-check (2026-09-17) — fix delta `41aa7def..c2c355d2`, report-only, same agent

**Verdict:** 0 Kritiskt · 0 Viktigt · 2 Nice-to-have (both new-in-delta, neither owed in this PR); NTH 1
closed; the check constraint bound; the append-only sentence belongs in D6. Transcribed verbatim from
the agent's reply.

### Arkitektur-analys — scoped re-check, PR #1751 (#1736), fix delta `41aa7def..c2c355d2`

**Date:** 2026-09-17 · **Agent:** dotnet-architect · **Mode:** report-only, scoped to the fix delta (CLAUDE.md §9.6). No edits, no builds, no integration suites run. · **Delta:** 1 commit, 19 files, +875/−41 (5 of those files are the round-1 review reports being promoted; not graded).

---

#### Sammanfattning

Delta is clean — **0 Kritiskt · 0 Viktigt · 2 Nice-to-have**, both new-in-delta, neither owed in this PR. **NTH 1 is closed**, with its added prose measured rather than taken on trust. The new check constraint is **bound** as an architecture matter; the append-only sentence **belongs in D6 as written**, with one residual named below. Round 1's Kritiskt/Viktigt count was zero, so nothing else is open from me.

**One thing I have to own before the findings.** The delta corrects a rationale that was **mine**, not the session's. Bind A item 2 and my round-1 verification both said `Navigation(js => js.TermsAcceptance).IsRequired(false)` is what lets EF tell "no stamp" from an all-null instance. The session mutated it and measured the opposite: EF reads an all-null owned row as `null` either way, and the read-side facts stay green (`TermsAcceptanceBackcompatTests.cs:16-23`). What the call actually buys is the **nullable columns in the scaffolded migration** — without it EF Core 10 defaults the owned reference to required and the scaffold emits NOT NULL, which is what would have failed the deploy against a populated table. The call stays; my reason for it was wrong; the docblock now says the measured thing instead of the believed one. CLAUDE.md §9.6: *the premise is false → say so plainly, with the measurement.* Said.

---

#### NTH 1 — closed

`BUILD.md:635-636` now carries:

    terms_accepted_at (timestamptz null)                    -- ADR 0142 D6: avtalsstämpel, null bara före del 1b
    terms_version, privacy_policy_version (varchar(20) null) -- all-or-nothing: ck_job_seekers_terms_all_or_none

This is a fix that **adds** prose, so every sentence it adds is a claim and I measured each against the artefact rather than against the commit message: `timestamptz null` ↔ `type: "timestamp with time zone", nullable: true`; `varchar(20) null` ↔ `type: "character varying(20)", maxLength: 20, nullable: true` on **both** version columns (`…/Migrations/20260917153605_AddTermsAcceptanceToJobSeeker.cs:14-32`); the constraint name is byte-identical to `JobSeekerConfiguration.cs:17-19` and to the migration's `AddCheckConstraint` (`:34-37`); "null bara före del 1b" matches the aggregate's guard. Two columns on one line matches the block's own `created_at, updated_at, deleted_at (soft delete)` precedent. **Closed.** NTH 2 stands as reported and is correctly carried in the PR body as owed by the part that adds a second factory — nothing about it changed here.

---

#### Bind: the check constraint — **one home, not two. Accept it.**

You asked whether a DB check constraint beside an EF optional owned type keeps the sentinel in one home or splits it. **One home**, on three measured grounds:

1. **The EF side never *declares* the sentinel — it derives it.** No literal "0 or 3" exists in the mapping; "all three NULL ⇒ null navigation" falls out of `Navigation(...).IsRequired(false)` plus three non-nullable CLR members. `ck_job_seekers_terms_all_or_none` is therefore the **only** declaration of the rule, not a second copy that could disagree with a first.
2. **The two artefacts govern different writers.** EF's reading governs what the application materializes; the constraint governs what *any* writer may store — including the ones EF never sees. That is not hypothetical here: `docs/runbooks/account-deletion.md` issues direct `UPDATE job_seekers …` statements. AGENTS.md §2.2 ("invariants protected by construction, not by convention") argues *for* the constraint — without it the all-or-nothing property is held only by the CLR type's non-nullability, which no raw-SQL writer is bound by.
3. **The form is the repo's own.** `TaxonomySnapshotMetaConfiguration.cs:14` (`ck_taxonomy_snapshot_meta_singleton`, `id = 1`) is the same `ToTable(name, t => t.HasCheckConstraint(...))` shape, declared in an `IEntityTypeConfiguration<T>` in Infrastructure — not a data annotation, which is the EF rule my charter enforces. Measured: those are the only two `HasCheckConstraint` sites in `src/`.

Mechanics verified: declared **once**, on the owner, despite three entity types table-splitting into `job_seekers` (`AppDbContextModelSnapshot.cs:622-625`); `AddCheckConstraint` ordered after the three `AddColumn`s and `DropCheckConstraint` before the three `DropColumn`s; `num_nonnulls` is core Postgres since 9.6 and the journey runs on `postgres:18`; the pre-migration shape (all three NULL) satisfies it, now proven rather than argued — the journey inserts a real row at `PreviousMigration` and reads its three columns NULL after the forward step (`AddTermsAcceptanceToJobSeekerMigrationTests.cs:127-149`, `:209-215`), which also proves `ADD CONSTRAINT` validated the legacy shape.

**Three things I checked and am explicitly not reporting**, so none reads as missed: (a) `AddCheckConstraint` takes ACCESS EXCLUSIVE and validates every row — on this table's measured size that is nothing, and the `NOT VALID` + `VALIDATE CONSTRAINT` two-step is a form for a large table, not for this one; (b) the constraint's SQL names columns as a string, but Postgres rewrites a check constraint's stored expression on `ALTER TABLE … RENAME COLUMN`, so a rename leaves the deployed constraint correct and only the C# string stale — at which point EF's model diff scaffolds a loud drop/recreate; (c) the watermark and soft-delete `UPDATE`s on `job_seekers` never touch the trio, so `num_nonnulls` stays at 0 or 3 through every delivered write path.

#### Bind: D6's append-only sentence — **it belongs, as written**

Same change-reason (it is what keeps the trio an Art. 5(2) record rather than a mutable field), and it belongs **now** rather than later, because overwrite is exactly what the first terms bump would otherwise reach for. It is not an unmeasured truth-claim about unbuilt code: the operative half — *"never by overwriting these three columns"* — constrains code that exists, and `JobSeekerTests.cs:455-465` arm (3) now enforces it mechanically rather than leaving it as prose. That conversion from sentence to guard is what makes it a bind instead of a wish.

---

#### Fynd (new-in-delta)

**[Nice-to-have]** `src/Jobbliggaren.Infrastructure/Persistence/Configurations/JobSeekerConfiguration.cs:50` (read against `tests/Jobbliggaren.Api.IntegrationTests/Persistence/TermsAcceptanceBackcompatTests.cs:16-23`)
**Vad:** The delta's own mutation measurement establishes that `builder.Navigation(js => js.TermsAcceptance).IsRequired(false)` is **caught by no test**. The read-side facts are green without it, and the migration journey's `is_nullable` reads run against the *committed* migration file, so they cannot notice a configuration line that only changes the *next* scaffold. Measured: `tests/` contains no `HasPendingModelChanges` or `IMigrationsModelDiffer` drift guard, so nothing compares the live model to `AppDbContextModelSnapshot`. The docblock's sentence is literally accurate under its own reading ("what the call buys is the nullable columns … pinned by the `is_nullable` reads" — the reads pin the *columns*), but it sits close enough to "the call is pinned" that a future reader will take the stronger meaning.
**Varför:** Not a defect in the delivered code — the call is correct and must stay. It is a known-unpinned configuration line, and the failure mode is loud rather than silent: removing it makes the *next* scaffolded migration emit `AlterColumn … nullable: false` on all three columns, which fails against rows carrying NULLs or is caught in that migration's review. That loudness is what keeps this off Viktigt.
**Föreslagen åtgärd:** Nothing in this PR — the only thing that would pin it is a generic model-vs-snapshot guard, which is a repo-wide change, not a #1736 one, and the session is under §9.6's filing cap. If it is ever wanted it is one architecture test, not a TermsAcceptance-specific one:

    // EF Core 9+: the live model and the committed snapshot agree, or a migration is owed.
    context.Database.HasPendingModelChanges().ShouldBeFalse();

I grade; §9.6 routes. I note only that this is the delta *discovering* a gap by mutation testing, which is the right outcome, not the delta creating one.

---

**[Nice-to-have]** `docs/decisions/0142-passwordless-auth-one-page-code-or-link-oauth-ready.md:323-326`
**Vad:** The append-only sentence says re-acceptance gets a row per acceptance and that the three columns are never overwritten, but it does not say what the three columns then **mean**: the *registration* acceptance (immutable, and stale the moment a user re-accepts) or the *current* one. Those are different answers to "which terms version is this user on?", and only the first is compatible with "never overwritten".
**Varför:** AGENTS.md §2.2 — the column trio is the aggregate's record of a fact, and a record whose referent is ambiguous is read wrong by whoever arrives second. The ADR is where that referent is fixed; the part that builds the append-only table will otherwise fix it by implementation.
**Föreslagen åtgärd:** One clause, whenever D6 is next edited — not a round of its own:

    …never by overwriting these three columns, which stay the REGISTRATION acceptance;
    the current acceptance is the latest row in that table.

---

#### Delta items verified as correct, not graded

- `JobSeeker.cs:19` "no setter" → "no **public** setter" — a factually wrong comment fixed by the minimal word (AGENTS.md §5 `Comments:`), not rewritten around.
- `docs/decisions/0142-…md:273` drops `RegisterCommandHandler.cs:113`'s line number — right direction; a line number decays within a commit or two.
- `TermsAcceptance.cs:14` drops the sentence describing the live `/registrera` checkbox — a deletion, and D6 still carries the statement, so nothing is lost.
- `TermsAcceptanceBackcompatTests.ExecuteAsync` now opens its own `NpgsqlConnection` from `Database.GetConnectionString()` instead of opening and closing EF's — a real correction, not a style change: the context's connection state is the context's.
- `PartiallyNulledRow_IsRefusedByTheAllOrNothingConstraint` asserts `SqlState` **and** `ConstraintName`, so it cannot pass on some other check violation; the refused `UPDATE` leaves no residue and the base class clears on exit either way.
- `JobSeekerTests` arm (3) is a name-based heuristic and its comment says exactly that ("Caught by name"), so it does not over-claim; a differently-named re-acceptance method escapes it, which is the accepted cost of a cheap third net beside two structural ones.
- `docs/runbooks/release-checklist.md:2488-2492` — web-before-API is the correct direction (API-first would stamp a policy version no live page carries) and the claim that the two dates are pinned in one tree, not at deploy, matches `TermsAcceptanceVersionsMatchPublishedPolicyTests`.

#### Referenser
- AGENTS.md §2.2 (invariants by construction, not convention — the ground for accepting the constraint) · §2.1 / EF rules (constraint declared via `IEntityTypeConfiguration<T>` in Infrastructure, never a data annotation) · §5 `Comments:` (the two comment corrections in this delta are the defect class, handled correctly) · §7 (`total:` is the proof — measurements taken from the session, not re-run here)
- CLAUDE.md §9.6 (report-only, delta-scoped; severity mine, routing the session's; "the premise is false → say so plainly")
- Precedent: `src/Jobbliggaren.Infrastructure/Persistence/Configurations/TaxonomySnapshotMetaConfiguration.cs:14`
- Round 1: `docs/reviews/2026-09-17-1736-dotnet-architect.md` · binds: `docs/reviews/2026-09-17-1736-architect-form.md`

---

**Verdict: 0 Kritiskt · 0 Viktigt · 2 Nice-to-have (both new-in-delta, neither owed in this PR). NTH 1 closed. Nothing from me blocks `agents-done`.**

### Session's disposition of the two new Nice-to-haves

Both are named skips in the PR body: the model-vs-snapshot drift guard is repo-wide and not #1736's;
the D6 referent clause ("the three columns stay the REGISTRATION acceptance; the current acceptance is
the latest row in that table") goes into the next D6 edit, which the epic's part 2 (#1738) makes.
