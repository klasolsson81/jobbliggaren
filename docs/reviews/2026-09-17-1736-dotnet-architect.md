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
