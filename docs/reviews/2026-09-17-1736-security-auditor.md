# security-auditor — #1736 (epic #1732 part 1b): PR #1751

**Date:** 2026-09-17 · **Agent:** security-auditor · **PR:** [#1751](https://github.com/klasolsson81/jobbliggaren/pull/1751)
· **Head reviewed:** `402fe30a` (`41aa7def` landed after the read; it touches a Playwright helper and
a runbook probe only) · **Mode:** report-only, no suite executed · **Verdict:** ✓ Approved, 0 Blocker /
0 Major against this diff; one Major carried forward unchanged from the epic review; 4 Minor; three
escalations to Klas, verbatim below. Report returned in the reply and transcribed verbatim by the session.

---

## Security-audit: terms-acceptance record on `JobSeeker.Register` (PR #1751, epic #1732 part 1b, #1736, closes #1484)

**Status:** ✓ Approved — **0 Blocker / 0 Major against this diff.** One Major is carried
forward unchanged from my epic review (it is not new here and is not closable here); 4 Minor.

**Auktoritet:** GDPR Art. 5(1)(a), 5(2), 6(1)(b), 12(1), 13, 17, 30, 32(1) · ADR 0142 D6 +
`Amendment 2026-09-17` · ADR 0125 Case 2 (plaintext exposure enumeration) · ADR 0049/0066 (assessed,
not engaged) · AGENTS.md §5 (`Comments:`, `Tests:`), §12 · CLAUDE.md §9.6 ·
my own `docs/reviews/2026-09-17-auth-epic-security.md` findings 6 and 8.

**What I ran:** reads, `git diff 88b6ea93..402fe30a`, `git grep`. **No test suite was executed**
(the full Api suite is running in this tree, per the brief). Every claim below is a read of the
tree at `402fe30a`. I wrote nothing.

---

### Blockers
None.

### Major — carried forward (NOT new in this diff, does NOT block #1751)

1. **The live checkbox still says the forbidden phrase, and this PR makes the record durable** — Fil:
   `web/jobbliggaren-web/messages/sv/pages.json:600,615` · `web/jobbliggaren-web/messages/en/pages.json:600,615`
   Nuvarande: `"termsLabel": "Jag godkänner <terms>användarvillkoren</terms> och <privacy>integritetspolicyn</privacy>."`
   and `"termsRequired": "Du måste godkänna användarvillkoren och integritetspolicyn för att skapa konto."`
   (en: "I accept the terms of use and the privacy policy" / "You must accept … and the privacy policy").
   Krävs: ADR 0142 D6, as ratified in `f2961a32`, binds the opposite in its own words — *"the checkbox
   accepts **the terms**; the privacy policy is linked as read in a sibling sentence, never 'godkänner …
   och integritetspolicyn'."* The copy has not moved.
   Motivering: Art. 5(1)(a)/12(1). Asking a user to *accept* an Art. 13 notice invites the reading that
   the processing rests on Art. 6(1)(a) consent with an Art. 7(3) withdrawal right — which is exactly
   the reading `terms_accepted_at`/`privacy_policy_version` were named to avoid. Until today the
   divergence was copy-only; from this PR it has a **persisted artefact**: every new row carries a
   `privacy_policy_version` whose documented meaning is "notice version, never an acceptance fact",
   written at the instant the user ticked a box saying they accept that policy.
   **Grading:** I graded this Major on 2026-09-17 against the epic (finding 8), and the grade is
   unchanged — §9.6 reserves a finding's severity to the agent that reported it, and I am that agent.
   I am **not** converting it into a finding against this diff: the copy is pre-existing, its fix home
   is epic part 2 per D6 (the notice moves to `/logga-in` with the collection point), and it does not
   make this PR's record wrong — the record and the new policy clause are both accurate. **It stays
   open.** Do not let part 2 close without the phrase ban.
   Delegera till: nextjs-ui-engineer (part 2) — or in-block here, see Minor 1; Klas routes.

### Minor

1. **A new docstring asserts a property of the live copy that the live copy contradicts** — Fil:
   `src/Jobbliggaren.Domain/JobSeekers/TermsAcceptance.cs:14-15`
   Nuvarande: *"…never an acceptance fact: the checkbox accepts the terms, and the privacy policy is
   linked as read."* Measured false today against `pages.json:600` in **both** locales.
   Krävs: either delete/narrow the clause to what is true now (a whole-line deletion closes
   mechanically — no added claim), or fix the four copy strings, which makes the sentence true and
   closes the phrase half of the carried Major in the PR that creates the record.
   Motivering: AGENTS.md §5 `Comments:` — a factually wrong comment is a defect and is fixed; §12
   carves `Comments:` out of STOPP, so this is not merge-blocking. The mechanism that matters: this
   sentence reads as though the requirement were already met, which is how part 2 loses it.
   Delegera till: dotnet-architect (docstring) or nextjs-ui-engineer (copy).

2. **A partial-NULL row is an undefined state at every layer** — Fil:
   `src/Jobbliggaren.Infrastructure/Persistence/Configurations/JobSeekerConfiguration.cs:40-50` ·
   `src/Jobbliggaren.Infrastructure/Persistence/Migrations/20260917141433_AddTermsAcceptanceToJobSeeker.cs:14-32`
   Nuvarande: `Navigation(...).IsRequired(false)` reads "no stamp" from **all three columns NULL**. All
   three columns are nullable with no DB-level constraint tying them together, and
   `TermsAcceptanceBackcompatTests` pins only the all-NULL and the all-present shapes.
   Krävs: a check constraint making them all-or-nothing, e.g.
   `CHECK (num_nonnulls(terms_accepted_at, terms_version, privacy_policy_version) IN (0,3))`, so the
   sentinel the mapping depends on is enforced where the raw-SQL paths actually live
   (`docs/runbooks/account-deletion.md` does raw `UPDATE`s on `job_seekers`).
   Motivering: Art. 32(1)(b) availability — a half-stamped row is a read the aggregate has no defined
   behaviour for, on one account's own row. `docs/reviews/2026-09-17-1736-db-migration-writer.md:97`
   argues (correctly) against a NOT NULL constraint; that argument does not reach this one, which
   constrains *consistency*, not presence, and is compatible with the two legacy all-NULL rows.
   Delegera till: db-migration-writer.

3. **API image and web image can disagree about which policy version is published** — Fil:
   `src/Jobbliggaren.Domain/JobSeekers/TermsAcceptance.cs:38` ·
   `web/jobbliggaren-web/messages/{sv,en}/content-legal.json:8`
   Nuvarande: `CurrentPrivacyPolicyVersion = "2026-09-17"` ships in the API image; the published
   `"Senast uppdaterad: 2026-09-17"` ships in the web image. The parity pin is compile/test-time on one
   tree and says nothing about deployment.
   Krävs: deploy the web image **not later than** the API image (web-first is safe, API-first is not),
   or name it as a line in the cutover checklist.
   Motivering: Art. 5(1)(d)/5(2). API-first means a stamp asserting a policy version no live page
   carried — an accountability record that is wrong in the one field it exists to hold.
   Delegera till: the session (PR body / cutover note); no code change.

4. **Re-acceptance has no home, and the current shape destroys the old evidence** — Fil:
   `src/Jobbliggaren.Domain/JobSeekers/JobSeeker.cs:25` · ADR 0142 D6
   Nuvarande: one stamp per seeker, written once in the private constructor, never rewritten. ADR 0142
   D6 is silent on what happens when the terms change and existing holders must accept a new version.
   Krävs: name the shape now — a row per acceptance (append-only), not an in-place overwrite. Deciding
   it at the first terms bump means deciding it under time pressure, and the natural quick fix
   (overwrite the three columns) silently deletes the Art. 5(2) evidence of the earlier acceptance.
   Motivering: Art. 5(2). Nothing to fix today; this is a forward gap that should be written down
   while it is cheap.
   Delegera till: adr-keeper (a line in D6) or a labelled issue; §9.6's filing cap applies — a named
   skip in the PR body is acceptable disposal for Minors 3 and 4.

*Not graded here (not my lane, noted for `code-reviewer`):* the new validator message at
`RegisterCommandValidator.cs:29` is a hardcoded Swedish string that the FE renders verbatim via the
`firstError` fallthrough in `actions.ts` — the same pattern as every other FluentValidation message
in this file, so not new, but it is the §5 "hardcoded UI strings" shape and it is not localized for
`en`.

---

### Answers to the two things you asked me to decide

**The privacy-policy clause is right — necessary, not merely permissible.** `content-legal.json:32`
(both locales) is the Art. 13(1)(c)/13(2) data inventory the policy publishes; a new stored category
was added, so the inventory had to change, and ADR 0142's "copy follows data, never precedes it"
makes *this* PR the correct home. I read it word-for-word against what is stored: three columns =
one instant + two version tokens, purpose "kunna visa att villkoren har godkänts" = Art. 5(2). It
says the user accepted **the terms**, and only that the policy **version applied then** — never that
the policy was accepted. It is one clause inside the existing account-data bullet, not a new separate
notice, which is what Klas-direktiv 2026-09-05/17 requires. Nothing in it is false about the tree.

**Bumping `privacy.updated` on this edit is right, and would have been a defect to omit.** The
published data list changed substantively; republishing a changed notice under the old date misstates
when the notice last changed. One consequence worth knowing rather than fixing: because the same
string is now the machine version token, the constant tracks *every* edit to the privacy copy, not
only material ones. That direction is more precise, not less, so it is fine — but it is what makes
Minor 3 real.

### Verification you asked for — Major 6's "NOT NULL for every row written after 1b" holds by construction

Measured at `402fe30a` (`git grep`, `src/` only): `new JobSeeker(` appears **once**
(`JobSeeker.cs:169`, inside `Register`); `JobSeeker.Register(` appears **once**
(`RegisterCommandHandler.cs:116`); `db.JobSeekers.Add` appears **once**
(`RegisterCommandHandler.cs:124`); no raw `INSERT`/`UPDATE` against `job_seekers` exists in `src/`
(the only raw SQL is a `SELECT` in `RecruiterErasureMatchQuery.cs:948`; `AccountHardDeleter.cs:269`
uses EF `Remove`). `Register` refuses a null acceptance at `JobSeeker.cs:159` *after* the `userId`
guard, the signature was **replaced** rather than overloaded so the acceptance-less path does not
compile, `TermsAcceptance` has a private constructor with three non-nullable properties, and
`JobSeeker.TermsAcceptance` has `private set` with no writer outside the constructor.
⇒ the property holds by construction for every EF path. **Two things it does not cover, and both are
already owned elsewhere:** raw SQL (Minor 2), and the OAuth first-login path, which is part 1c and
still owes the same ordering (my finding 6, unchanged).

### Areas measured and clear
- **Area 1 (PII):** `job_seekers` carries `DeletedAt` + `HasQueryFilter(js => js.DeletedAt == null)`
  (`JobSeekerConfiguration.cs:95,97`); `AccountHardDeleter.cs:269` removes the row, so the stamp goes
  with the account (Art. 17). Retention is covered by the published policy's existing account bullet.
  Plaintext without DEK is correct: two Domain constants and a timestamp, nothing user-authored, and
  `terms_accepted_at` carries no information `created_at` does not already carry.
- **Area 3 (auth):** the `Equal(true)` rule runs in `ValidationBehavior` **before** the handler and so
  before `CreateUserAsync` — no orphan Identity user (#508) — and reads only the bool, so the response
  cannot vary with the submitted address (#714). An omitted field binds to `false` and is refused;
  `RegisterCommand` deliberately carries no default. No authZ surface changed.
- **Area 4/5 (GDPR/transfers):** no new processing purpose, no new recipient, no new sub-processor, no
  transfer, no profiling, no automated decision, no new sensitive category. No DPIA trigger. §5's
  LLM ban is not engaged.
- **Area 6 (logging):** nothing logs the stamp. `LoggingBehavior` logs the message **name** only;
  `ValidationBehavior.cs:23-29` groups failures to `PropertyName → ErrorMessage[]`, so no
  `AttemptedValue` reaches `Program.cs:255`'s `new { errors = ex.Errors }` — the 400 body cannot echo
  a submitted password or address.
- **Area 7 (attack vectors):** no raw SQL in production code, no new URL/redirect/upload surface, no
  new client storage, no serialization of the aggregate past the Application boundary
  (`JobSeekerProfileDto` unchanged).
- **Area 8 (supply chain):** **untouched** — no `package.json`, `pnpm-lock.yaml`, `pnpm-workspace.yaml`
  or `.github/**` path in the 185-file diff. The suppression guard was not run and is not owed.
- **Registers:** `ErasureCascadeRegistry.cs:507-508` classifies the two varchar columns
  `NotRecruiterData` with the written ground at `:789-795`; `terms_accepted_at` is `timestamptz` and
  outside the text sweep, which matches `ModelSweep.IsTextBearingStoreType`'s scope.
  `MappedPlaintextExposureRegistry.cs:121-127` covers them through the `job_seekers` row test, which
  is what `MappedPlaintextExposureRegistryTests` requires for a `PersonGrainedTables` table. Art. 30
  register read in the main copy at `C:/DOTNET-UTB/JobbPilot/docs/runbooks/gdpr-processing-register.md:1376-1390`
  and `:1409-1412` — the Datafält bullet and the Art. 6(1)(b) paragraph both name the stamp
  correctly, including the "not Art. 7 consent, no withdrawal column" characterisation.

### §12
Security-critical change **with** tests. Per §12's Klas-direktiv 2026-07-16 scope clarification, a
security-critical change with tests and a security-auditor APPROVE (0 Blocker / 0 Major against the
final diff) rides normal automerge. The tests: `TermsAcceptanceTests.cs` ·
`TermsAcceptanceVersionsMatchPublishedPolicyTests.cs` · four new cases in `JobSeekerTests.cs`
(null → `JobSeeker.TermsAcceptanceRequired`, guard **order**, stamp carried, event carries no
versions) · `RegisterCommandValidatorTests.Validate_AcceptTermsFalse_FailsOnTheAcceptTermsField` ·
`RegisterCommandHandlerTests.Handle_WithValidCommand_AddsSeekerStampedWithTheCurrentTerms` ·
three cases in `RegisterTests.cs` (false → 400 naming the field, omitted → 400 naming the field,
true → row stamped) · `RegisterConfirmationTests.POST_register_fresh_stamps_the_job_seeker_row_on_the_202_path` ·
`TermsAcceptanceBackcompatTests.cs` (3) · `actions.test.ts`. No other §12 class is engaged (no §5
anti-pattern, no Clean-Architecture boundary crossing — `TermsAcceptance` is Domain and reads no
files — no new library, no design token).

### Praise
- Fail-closed **by construction**, not by convention: no default on `AcceptTerms`, the signature
  replaced rather than overloaded, one measured write path, guard **order** pinned by its own test. ✓
- The premise discipline in `TermsAcceptanceBackcompatTests` — the unreachable state names its actor
  (rows written before the migration) and points at the pin that the current writer cannot produce it. ✓
- Both registries updated with a written ground, and the Domain constants made *checkable* against the
  published copy in both locales rather than declared. ✓

### Sammanfattning
0 Blocker, 0 new Major, 4 Minor, plus one Major carried forward unchanged from
`docs/reviews/2026-09-17-auth-epic-security.md` finding 8 that this PR neither creates nor closes.
The record, the mapping, the migration, the registers, the Art. 30 entry and the published clause are
mutually consistent and accurate; the only thing out of step with them is the checkbox copy, whose fix
home is epic part 2. Re-review efter fix: samma agent, report-only, scopad till fix-deltat
(CLAUDE.md §9.6).

**Eskalering till Klas:** ja — tre saker, och den andra är den som inte får paraphraseras bort.

(1) **Rutans text.** ADR 0142 D6, som du ratificerade i `f2961a32`, binder ordagrant att kryssrutan
godkänner **villkoren** och att integritetspolicyn **länkas som läst, aldrig "godkänner … och
integritetspolicyn"**. Texten i `messages/{sv,en}/pages.json:600,615` säger fortfarande precis den
förbjudna formuleringen, på båda språken, i både etiketten och vägran. Från och med den här PR:en
finns det en **lagrad post** som vilar på den motsatta karaktäriseringen: varje ny rad bär ett
`privacy_policy_version` vars dokumenterade innebörd är "notisversion, aldrig ett acceptansfaktum",
skrivet i samma ögonblick som användaren kryssade i en ruta som säger att hon godkänner policyn.
**Du avgör:** rättas de fyra strängarna in-block i den här PR:en (då stängs också Minor 1 av sig
själv), eller hålls det till del 2 där notisen ändå flyttar till `/logga-in`? Jag blockerar inte
#1751 för det — fyndet är mitt, graderat mot epiken den 2026-09-17, och dess hem är del 2 — men
det står öppet tills texten ändras.

(2) **Jag har INTE mätt om bärarpremissen, och ingenting i den här bedömningen vilar på den.**
ADR/ROPA-premissen "de två kontona, båda Klas egna" är mätt 2026-08-23 och **återges som premiss,
inte som min mätning denna session**. Jag föreslår och signerar **ingen** §9.6 (3)-acceptans här,
och ingen grad ovan är satt på bärarfrånvaro. Om någon längre fram vill åberopa (3) för något i den
här epiken måste mätningen tas om då — en bärarfrånvaro ärvs aldrig från en tidigare rad.

(3) **Utrullningsordningen.** `CurrentPrivacyPolicyVersion = "2026-09-17"` ligger i API-imagen,
den publicerade "Senast uppdaterad: 2026-09-17" i web-imagen. Rullas API:t ut före webben stämplas
en policyversion som ingen levande sida bär — en ansvarighetspost som är fel i det enda fält den
finns för. Webben ska ut senast samtidigt som API:t. Det behövs ingen kodändring, men det behöver
stå någonstans som läses vid cutover.

---

## Scoped re-check (2026-09-17) — fix delta `41aa7def..c2c355d2`, report-only, same agent

**Verdict:** Minors 1–4 CLOSED; no new Blocker/Major; **APPROVE stands against the final diff
`88b6ea93..c2c355d2` (0 Blocker / 0 Major)**; escalations (1)–(3) restated verbatim below. Transcribed
verbatim from the agent's reply.

### Scoped re-check: PR #1751 fix delta (CLAUDE.md §9.6, report-only)

**Scope:** `git diff 41aa7def..c2c355d2` (one commit, `c2c355d2`). **No edits, no commits, no suites run.** HEAD measured at review time: `c2c355d2` (`git rev-parse HEAD`). I additionally read the intervening `402fe30a..41aa7def` so my final-diff statement below is not made against a commit I never saw.

#### Per-Minor verdict

**Minor 1 (docstring vs live checkbox) — CLOSED.** `src/Jobbliggaren.Domain/JobSeekers/TermsAcceptance.cs:13-14` now reads "…for Art. 5(2) accountability, never an acceptance fact (ADR 0142 D6)." The clause describing the checkbox is gone: `git grep -n "linked as read" -- src/` returns **0**. The replacement asserts only the column's meaning, which ADR 0142 D6 defines and which is true of the tree; it makes no claim about the FE copy, which is exactly the property I asked for. The delta adds a line, so this was correctly routed back to me rather than closed mechanically.

**Minor 2 (partial-NULL row) — CLOSED, and closed better than I asked.** `src/Jobbliggaren.Infrastructure/Persistence/Configurations/JobSeekerConfiguration.cs:12-19` carries `ck_job_seekers_terms_all_or_none` with `num_nonnulls(terms_accepted_at, terms_version, privacy_policy_version) IN (0, 3)` — the right predicate: it admits 0 (the legacy shape the two pre-1b rows hold) and 3 (stamped), and refuses 1 and 2. `AddCheckConstraint` runs after the three `AddColumn`s in `Up`; `DropCheckConstraint` runs **first** in `Down` — correct ordering in both directions. Designer and `AppDbContextModelSnapshot` both carry it. Three pins, and they cover different things: `PartiallyNulledRow_IsRefusedByTheAllOrNothingConstraint` asserts `SqlState` 23514 **and** the constraint name (a rename cannot pass it silently); the journey reads `pg_constraint` at head and its **absence** at the previous migration; and the populated-table forward step proves `ADD CONSTRAINT` validated against the all-NULL legacy shape rather than merely being declared. On `job_seekers` the validating scan is two rows, so the `ACCESS EXCLUSIVE` lock `ADD CONSTRAINT` takes is not a deploy-availability concern here.

**Minor 3 (deploy order) — CLOSED.** `docs/runbooks/release-checklist.md:2488-2492` states the direction correctly: web out no later than the API, because API-first stamps a policy version no live page carries; on a split rollout, web first. That is the safe direction, not the reverse.

**Minor 4 (re-acceptance shape) — CLOSED.** ADR 0142 D6's amendment now binds append-only, a row per acceptance, never an overwrite of the three columns, with the reason written out (the overwrite deletes the earlier Art. 5(2) evidence). That was the whole ask — name the shape while it is cheap.

#### New in delta

**No new Blocker and no new Major.** Three things I read closely and clear:

- `tests/Jobbliggaren.Api.IntegrationTests/Auth/RegisterConfirmationTests.cs:216-234` — `POST_register_with_accept_terms_false_is_identical_for_a_fresh_and_a_taken_address` pins identical status **and identical body bytes** for the two branches. This is a genuine strengthening of the #714 anti-enumeration property, not paperwork: it fails the moment the terms refusal is moved into the handler behind the taken-address branch. It belongs in my area 3 and I would have asked for it had it not been written.
- The backcompat helper's raw SQL is parameterized (`@id`, `AddWithValue`) on its own `NpgsqlConnection`; no concatenation, no credential handling, no leak. Area 7 clear.
- The corrected backcompat docblock discloses a **weaker** pin than the original claimed — measured 2026-09-17, EF reads an all-null owned row as `null` with or without `Navigation(...).IsRequired(false)`, so what the call buys is the nullable columns, pinned by the journey's `is_nullable` reads. Reporting a mutation that came back green, instead of quietly keeping the stronger sentence, is the right direction and I am recording it as such.

#### Two ungraded observations for the session (not findings in my scale, not reasons to hold the PR)

1. **The migration was re-scaffolded after its reviewer signed off.** `docs/reviews/2026-09-17-1736-db-migration-review.md:11,17-18` reviews `20260917141433` and states "exakt tre `AddColumn` i `Up` … exakt tre `DropColumn` i `Down`. Inget annat i filen." The shipping migration is `20260917153605` with **four** operations in each direction. The citation itself is dated provenance and does not decay, but the verdict's content no longer describes what ships, and it is the only db-migration-writer sign-off in the PR. Under §9.6's closing rule the constraint went back to nobody: it adds lines, so it is not a mechanical closure. Either db-migration-writer takes a scoped re-check on the constraint + re-scaffold, or that report gains one line recording that the constraint landed after its verdict and who graded it. Routing is senior-cto-advisor's, not mine. (Measured: the old id survives only in `docs/reviews/*`, never in `src/` or `tests/`.)
2. **`tests/Jobbliggaren.Domain.UnitTests/JobSeekers/JobSeekerTests.cs` arm (3) is name-based and partial by construction** — `ReAccept(clock)` or `Renew(clock)` contains no "Terms" and passes it. The delta's own comment says the arm is by name, so nothing is overstated; I note only that the ADR binding, not this arm, is what actually holds Minor 4.

#### Does the APPROVE stand?

**Yes.** Against the **final** diff `88b6ea93..c2c355d2`: **0 Blocker, 0 Major.** `41aa7def` is test and runbook only (`web/jobbliggaren-web/tests/e2e/helpers/auth.ts` and the `docs/runbooks/registration-gate.md` probe body gain `acceptTerms: true`) and is correct for the right reason — the gate probe must pass validation so its answer measures the kill-switch (503) and not the validator. Per §12's Klas-direktiv 2026-07-16 scope clarification, a security-critical change **with** tests and a security-auditor APPROVE issued against the final diff rides the normal automerge flow. My APPROVE is issued against head `c2c355d2`; if content moves again it does not carry over.

**Eskalering till Klas:** ja — oförändrad, och den ska bäras ordagrant tills du svarar.

(1) **Rutans text.** ADR 0142 D6, som du ratificerade i `f2961a32`, binder ordagrant att kryssrutan godkänner **villkoren** och att integritetspolicyn **länkas som läst, aldrig "godkänner … och integritetspolicyn"**. Texten i `messages/{sv,en}/pages.json:600,615` säger fortfarande precis den förbjudna formuleringen, på båda språken, i både etiketten och vägran — mätt på `c2c355d2`, `pages.json` är orörd i fixdeltat. Från och med den här PR:en finns det en **lagrad post** som vilar på den motsatta karaktäriseringen. **Du avgör:** rättas de fyra strängarna in-block, eller hålls det till del 2 där notisen ändå flyttar till `/logga-in`? Jag blockerar inte #1751 för det — fyndet är mitt, graderat mot epiken den 2026-09-17, och dess hem är del 2 — men **det står öppet tills texten ändras**. Att docstringen inte längre påstår motsatsen är rätt förval: det förtiger ingenting och föregriper inte ditt svar. En sak till, som *inte* är ett nytt fynd utan samma öppna: när texten flyttar i del 2 ska D6:s egen presens-mening ("the privacy policy is linked as read in a sibling sentence") svepas med — den beskriver ett tillstånd som ännu inte råder.

(2) **Jag har fortfarande INTE mätt om bärarpremissen, och ingenting i den här bedömningen vilar på den.** ADR/ROPA-premissen "de två kontona, båda Klas egna" är mätt 2026-08-23 och återges som premiss, inte som min mätning. Jag föreslår och signerar **ingen** §9.6 (3)-acceptans här, och ingen grad ovan är satt på bärarfrånvaro. Om någon längre fram vill åberopa (3) för något i den här epiken måste mätningen tas om då — en bärarfrånvaro ärvs aldrig från en tidigare rad.

(3) **Utrullningsordningen** är nu skriven i `docs/runbooks/release-checklist.md:2488-2492` och behöver inget beslut av dig — den ska bara läsas vid cutover.

### Session's note on observation 1

db-migration-writer took the scoped re-check on the constraint + re-scaffold the same hour (appended to
`docs/reviews/2026-09-17-1736-db-migration-review.md`): APPROVE stands for `c2c355d2`, four operations
in each direction reviewed, `Down` order verified. The route the observation asked for was taken.
