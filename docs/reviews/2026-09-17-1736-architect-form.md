# dotnet-architect — #1736 (epic #1732 part 1b): the form of `TermsAcceptance`

**Date:** 2026-09-17 · **Agent:** dotnet-architect · **Mode:** plan phase, read-only, report returned in
the reply and transcribed verbatim by the session · **Worktree:** none yet at decision time (main copy
`main` at `f2961a32`, `origin/main` `88b6ea93`) · **Input:** the session's brief — ADR 0142 D6's
`readonly record struct` + `OwnsOne` cannot both hold (`OwnsOne<T>` requires `where T : class`); the
three candidate mappings (a) sealed record + OwnsOne, (b) struct + backing fields, (c) ComplexProperty;
the factory surface; the aggregate's refusal; the API gate; the parity pin's home. The
`string? displayName` question was `senior-cto-advisor`'s in parallel and is not decided here.

---

## Arkitektur-analys — #1736 (epic #1732, part 1b): form binds

### Sammanfattning
Five binds, all resting on measured precedent. One **Kritiskt**: ADR 0142 D6 as written does not compile — `readonly record struct` and `OwnsOne` are mutually exclusive, and D6's own "parity with `Preferences`" argument resolves the conflict against the struct. The remaining four are form binds, not findings. I do not touch `string? displayName`; that is the CTO's.

---

### A — Bind **(a)**: `sealed record` class + `OwnsOne` + `Navigation(...).IsRequired(false)`

**[Kritiskt]** `docs/decisions/0142-…-oauth-ready.md:268-275`
**Vad:** D6 requires both `readonly record struct` and `builder.OwnsOne(js => js.TermsAcceptance).IsRequired(false)`. `EntityTypeBuilder<TEntity>.OwnsOne<TRelatedEntity>` constrains `where TRelatedEntity : class`. The two sentences cannot both hold.
**Varför:** D6's own stated ground picks the winner — *"parity with `Preferences`"*. Measured: `Preferences` is a **`sealed record` class** (`src/Jobbliggaren.Domain/JobSeekers/Preferences.cs:39`), mapped `OwnsOne` at `JobSeekerConfiguration.cs:24-27`. The parity argument is an argument *for* a class. The struct is the half that gives.

Rejecting the two alternatives on measured ground:

- **(b) backing fields** — the `CompanyWatchCriterionConfiguration.cs:8-35` rationale does not transfer. Its two written grounds are (1) a shared VO instance re-keys and throws, and (2) InMemory fails on complex members carrying **value converters**. Neither reaches a per-registration stamp with no converters. And (b) costs what (a) gives for free: the all-three-or-none invariant leaves the type and becomes an aggregate convention, and EF reconstruction bypasses the factory entirely. (a) keeps it in the type.
- **(c) `ComplexProperty`** — measured: **zero** `ComplexProperty` mappings exist in the repo; the only two occurrences are the comments recording its rejection (`CompanyWatchCriterionConfiguration.cs:17`, `ResumeConfiguration.cs:106`). EF Core 10 did add optional complex properties, but with a live constraint (the type needs ≥1 required property) and open defects in exactly that feature ([#37249](https://github.com/dotnet/efcore/issues/37249), [#38043](https://github.com/dotnet/efcore/issues/38043), [#37890](https://github.com/dotnet/efcore/issues/37890)). Whether the **InMemory** provider shapes an optional complex property is unmeasured — and InMemory is the sanctioned handler harness (`tests/Jobbliggaren.Application.UnitTests/Common/TestAppDbContextFactory.cs:16`, AGENTS.md §2.4). Taking a first-in-repo EF feature, on the aggregate every handler test seeds, for zero gain over a twice-delivered pattern, is an unmeasured risk bought with nothing.

**The VO** (`src/Jobbliggaren.Domain/JobSeekers/TermsAcceptance.cs`), the `ManualPosting.cs:13-26` shape verbatim — private ctor whose parameter names match the property names, so EF's constructor binding materializes it (proven by `ManualPosting` being delivered):

    public sealed record TermsAcceptance
    {
        public DateTimeOffset AcceptedAt { get; }
        public string TermsVersion { get; }
        public string PrivacyPolicyVersion { get; }

        private TermsAcceptance(
            DateTimeOffset acceptedAt, string termsVersion, string privacyPolicyVersion) { … }
    }

Property on the aggregate: `public TermsAcceptance? TermsAcceptance { get; private set; }` — absent = `null`, which is the two pre-existing rows.

**The mapping**, placed directly after the `Preferences` block at `JobSeekerConfiguration.cs:27`:

    builder.OwnsOne(js => js.TermsAcceptance, terms =>
    {
        terms.Property(t => t.AcceptedAt).HasColumnName("terms_accepted_at");
        terms.Property(t => t.TermsVersion)
            .HasColumnName("terms_version").HasMaxLength(20);
        terms.Property(t => t.PrivacyPolicyVersion)
            .HasColumnName("privacy_policy_version").HasMaxLength(20);
    });
    builder.Navigation(js => js.TermsAcceptance).IsRequired(false);

Four things are load-bearing and each has a measured reason:

1. **Explicit `HasColumnName` on every property** — the global `UseSnakeCaseNamingConvention` (`DependencyInjection.cs:1162`) would otherwise prefix the navigation name → `terms_acceptance_*`. Same reason as `ApplicationConfiguration.cs:51-52`.
2. **`Navigation(...).IsRequired(false)` is obligatory** — EF Core 10 defaults an owned reference to required; without it EF cannot tell "no acceptance" from an all-null instance (`ApplicationConfiguration.cs:53-55`, `:77-80`).
3. **No `.IsRequired()` inside the lambda.** The optional `OwnsOne`s (`ApplicationConfiguration.cs:56-70`, `:84-122`) omit it; only the *required* navigation at `ResumeConfiguration.cs:111-117` uses it. An optional navigation nullifies every column regardless, and an inner `IsRequired()` fights that.
4. **The all-null sentinel is unambiguous by construction** — all three VO properties are non-nullable inside the type, so a present acceptance never writes an all-null row. This is the property `ApplicationConfiguration.cs:110-112` has to argue for `AdSnapshot` via `capturedAt`; here it is free. Say so in the comment.

**Columns:** `terms_accepted_at timestamp with time zone NULL`, `terms_version character varying(20) NULL`, `privacy_policy_version character varying(20) NULL`.

**On the cap: literal `20`, not a Domain constant.** The value is an ISO date (10 chars); 20 is headroom for a suffix without being de-facto unbounded `TEXT`. A `TermsAcceptance.MaxVersionLength` const would have **no Domain consumer** under bind B — the factory is total and validates nothing — and a member with no consumer is the dead axis `ManualPosting.cs:9-11` names as §5.1. `ManualPosting`'s own 300/200 are literals on both sides. Revisit when `Create(...)` arrives and the number acquires a Domain reader.

**Action item beyond the code:** D6's sentence is now wrong in a tracked, Accepted ADR. Amend `0142-…:268-269` in the 1b PR — `readonly record struct` → `sealed record` — and add the one-clause reason (`OwnsOne` requires a reference type). A factually wrong spec line is a defect, not phrasing (AGENTS.md §5 `Comments:`).

---

### B — Bind **(i)**: one total factory, `AcceptCurrent`. No `Create(...)`, no `tests/Shared/` helper.

    public const string CurrentTermsVersion = "2026-06-28";
    public const string CurrentPrivacyPolicyVersion = "2026-08-28";

    public static TermsAcceptance AcceptCurrent(IDateTimeProvider clock) =>
        new(clock.UtcNow, CurrentTermsVersion, CurrentPrivacyPolicyVersion);

**Varför (ii) is rejected:** your own measurement is decisive and I confirm it — no part in the epic carries a client version. `RegisterCommand` gains `bool AcceptTerms`; D3's `/auth/challenge/complete` takes `acceptTerms` as a bool; D8's OAuth first sign-in routes through the consent step. So `Create(termsVersion, privacyPolicyVersion, acceptedAt) → Result<TermsAcceptance>` would ship with **one caller passing the two constants and a failure branch no caller can reach**. That is a dead axis in the exact sense `ManualPosting.cs:9-11` cites §5.1 for, and "the known set" would have nothing to refuse. Defer the validating factory to the part that first carries a version the user actually saw; name that deferral in D6 so it reads as scheduling, not as an omission.

**Varför the factory is total, not `Result`-returning:** with versions as constants, both of D6's version predicates are true by construction. The third — `AcceptedAt ≠ default` — would be a check `Register` does not perform on its **own** `CreatedAt` (`JobSeeker.cs:141` reads `clock.UtcNow` unguarded). A clock returning `default` is a test defect, not a domain refusal; adding the guard here and not there asserts a distinction that does not exist.

**Varför no `tests/Shared/TestTermsAcceptance.cs`:** the shared-helper convention (`TestIds.cs`, `TestFacets.cs`, linked at e.g. `Jobbliggaren.Domain.UnitTests.csproj:30-32`) exists to hide ceremony. `AcceptCurrent` has none — every call site already holds the clock it passes to `Register`, so each becomes `JobSeeker.Register(userId, "Name", TermsAcceptance.AcceptCurrent(Clock), Clock)`. A helper would add a link line to five `.csproj` files to save nothing. Note the converse: under **(ii)** the helper *would* be needed, to hide a `Result` unwrap that can never fail — which is itself evidence (ii) is the wrong factory.

**Measured churn, and it is larger than the prompt states.** `grep -ro "JobSeeker\.Register(" --include=*.cs src tests` → **321** occurrences across 142 files: **1 in `src/`** (`RegisterCommandHandler.cs:113`) and **320 across 141 test files** (Application.UnitTests 81 files, Api.IntegrationTests 32, Worker.IntegrationTests 21, Domain.UnitTests 6, QA.Corpus 1). The prompt's "251" does not match my scope; I state mine rather than adjust yours. The edit is mechanical either way, but it is ~320 lines, not ~250, which is worth knowing before the diff is reviewed.

**On the two clock reads:** the handler reads `clock.UtcNow` for the VO and `Register` reads it again for `CreatedAt` (`JobSeeker.cs:141`). Two facts, two reads, microseconds apart. This is correct, not drift — do not couple `terms_accepted_at` to `created_at` to save a read.

---

### C — The refusal is `is null` on a **non-nullable** parameter; the event gains nothing

**Signature:** `TermsAcceptance acceptance`, not `TermsAcceptance?`. The `string? displayName` idiom exists because the caller forwards a nullable DTO field (`command.DisplayName`, `RegisterCommandHandler.cs:113`); nothing in the acceptance path has a nullable source, so a nullable parameter would advertise a legal `null` the aggregate then refuses.

**The check runs anyway** — NRT is not a runtime guarantee, and this is the house form already: `Register` runs `userId == Guid.Empty` on a non-nullable `Guid` (`JobSeeker.cs:132-134`). Place it immediately after that guard, before `ValidateDisplayName`:

    if (acceptance is null)
        return Result.Failure<JobSeeker>(DomainError.Validation(
            "JobSeeker.TermsAcceptanceRequired",
            "Ett konto kan inte skapas utan godkända användarvillkor."));

`DomainError.Validation` → `ErrorKind.Validation` → 400 through the central mapper (`DomainError.cs:26-27`). Code in `<Aggregate>.<Condition>` form, matching `JobSeeker.UserIdRequired` / `JobSeeker.DisplayNameRequired`.

**Deliberately NOT shared with the validator's message (bind D).** They are two propositions — "this request did not express acceptance" (reaches a user) and "a seeker cannot exist without a stamp" (fail-closed, unreachable from the API path). One constant would assert they are one statement. Keep two sentences; do not extract.

**The event carries no versions.** Measured: `JobSeekerRegisteredDomainEvent` has **zero consumers** — `grep` finds the raise site (`JobSeeker.cs:144-145`) and two Domain.UnitTests assertions (`JobSeekerTests.cs:34`, `:279`), nothing else in `src/`. `OccurredAt` already carries the instant. A field no consumer reads is the same dead axis as B. DoD 7 is met by documenting the event honestly: **the accountability record is the row, not the event** — the event announces that a registration happened; `job_seekers.terms_*` is the Art. 5(2) evidence. Write that sentence into D6 and the XML doc, because it is the thing a future reader will otherwise get wrong.

---

### D — Bind the **validator**, not a handler-level `DomainError`

    RuleFor(c => c.AcceptTerms).Equal(true)
        .WithMessage("Du måste godkänna användarvillkoren för att skapa ett konto.");

in `RegisterCommandValidator.cs`, after the `DisplayName` rule at `:20`. Four grounds, in order of weight:

1. **Ordering — it clears the #714/#508 hazard by construction.** `ValidationBehavior` throws before invoking the handler (`ValidationBehavior.cs:28-31`), i.e. before `CreateUserAsync` at `RegisterCommandHandler.cs:56`. No Identity user, no `DeleteUserAsync` compensation, no orphan for the #508 sweep. This is the same ordering property the handler argues for the kill-switch and the #1117 refusal (`RegisterCommandHandler.cs:27-31`, `:39-49`) — and here it is free rather than reasoned into place.
2. **The rule reads only `command.AcceptTerms` and never `command.Email`**, so the response cannot vary with the submitted address. The anti-enumeration property holds structurally, exactly as `RegisterCommandHandler.cs:30-31` states it for the kill-switch.
3. **It reuses the delivered 400.** `ValidationException` → the `errors` dictionary at `Program.cs:252`. A handler-level `DomainError` would require a new `AuthErrorCodes` constant + message for a rule about **request shape**, not domain state — and `AuthErrorCodes`' own docstring says it exists to keep *auth control-flow discriminants* in one place. A missing checkbox is not one.
4. **It is not a second home.** The aggregate refuses independently (bind C). The two rules state different propositions, so this is ordering, not duplication — the #1117 argument verbatim.

**Two mechanics worth binding while you are in the file:**

- `RegisterCommand` (`RegisterCommand.cs:10-14`): `AcceptTerms` goes **before** `RememberMe = false` and takes **no default** — C# forbids a non-defaulted parameter after a defaulted one, and a defaulted `AcceptTerms` invites someone to write `= true`.
- A JSON body omitting `acceptTerms` binds to `default(bool)` = `false` → 400. **Fail-closed by construction.** Say it in a comment; it is the property a reader will otherwise assume is an oversight.
- Measured: **no `.Equal(true)` or `Must(x => x)` exists in any validator in `src/`.** This is a new form in the repo, not a departure from one — flagging it so a reviewer does not read it as ad-hoc.

---

### E — Two pins, two homes, one shared walk-up

**The existing test cannot move.** `ContactAddressMatchesPublishedContactTests.cs:2` is `using Jobbliggaren.Infrastructure.Email;` — its subject is `EmailTemplates.ContactAddress`, an Infrastructure constant. `Jobbliggaren.Domain.UnitTests.csproj:22-24` references **only** `Jobbliggaren.Domain`. So the question is not "which project" but "how many walk-ups".

**Bind:** new pin in **Domain.UnitTests** (its subject is a Domain constant, and Domain.UnitTests reaches it with no new reference); existing pin stays in Application.UnitTests; the walk-up moves to `tests/Shared/ContentLegalMessages.cs`, linked into both `.csproj`s beside the existing `TestIds.cs` / `TestFacets.cs` lines. Cost: two link lines and one ~17-line deletion at `ContactAddressMatchesPublishedContactTests.cs:107-127`. The alternative is a second private walk-up, i.e. knowingly creating the duplicate `tests/Shared/` exists to prevent.

**⚠ Copy the precedent's structural half only — its token-sweep does NOT transfer.** The precedent asserts *no other address survives anywhere in the file* (`:76-84`). The analogue — no other date anywhere — is **false**: `"updated"` appears at five indices per locale (lines 8, 231, 316, 395, 543), carrying `2026-08-28`, `2026-06-28`, `2026-07-05`, `2026-06-30`, `2026-07-16`. A file-wide sweep fails against three unrelated documents. Address the keys **structurally** via `JsonDocument`, never by line number or token scan.

Verified by parsing both files: `privacy.updated` = `"Senast uppdaterad: 2026-08-28"` / `"Last updated: 2026-08-28"`; `terms.updated` = `"Senast uppdaterad: 2026-06-28"` / `"Last updated: 2026-06-28"`. So `EndsWith` (not equality — the prefix is localized prose), `[Theory]` over `sv` and `en`, both locales pinned as the precedent does.

---

### Referenser
- AGENTS.md §2.1 (EF dependency rule, three axes) · §2.2 (invariants in the aggregate, not handlers) · §2.3 (pipeline order) · §2.4 (InMemory harness) · §5 (primitive obsession, dead axis, `Comments:`)
- CLAUDE.md §9.5 (external fact — EF Core 10 optional complex properties, web-checked 2026-09-17) · §13 (ADR/spec correction rides the same PR)
- ADR 0142 D6 (`docs/decisions/0142-…-oauth-ready.md:266-294`) — **needs the `readonly record struct` → `sealed record` amendment**
- `docs/reviews/2026-09-17-auth-epic-architect.md:104-108` — replacement signature stands; its `readonly record struct` clause carries the same defect as D6 and is corrected here by this report.

Sources for the EF Core 10 check: [efcore#37249](https://github.com/dotnet/efcore/issues/37249), [efcore#38043](https://github.com/dotnet/efcore/issues/38043), [efcore#37890](https://github.com/dotnet/efcore/issues/37890), [Complex Types in EF Core 10 — NikolaTech](https://www.nikolatech.net/blogs/complex-types-ef-core-10)

---

## Session's note on the architect's count (2026-09-17)

The architect counted 320 test call sites in 141 files; the session's `git grep` on `origin/main`
`88b6ea93` gives 321 in 142 (the CTO's number). The one-file difference is
`tests/Jobbliggaren.Api.IntegrationTests/Helpers/AuthTestHelpers.cs`, whose single `Register` call
the architect's `grep -ro` over the main copy at `f2961a32` saw in its pre-#1750 form. The sweep
rewrote 321 and the solution compiles; the count is a PR-body fact with its command, never frozen here.
