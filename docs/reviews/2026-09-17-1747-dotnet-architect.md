# dotnet-architect — #1747 (epic #1732 part 6d)

- **PR:** #1752 · **Branch:** `chore/drop-auth-provider-1747` · **Base:** `d90414f3`
- **Round 1 against:** `5ec3a1e6` · **Scoped re-check against:** `62c169f0`
- **Scale:** Kritiskt / Viktigt / Nice-to-have (→ Blocker / Major / Minor, CLAUDE.md §9.6)
- **Verdict round 1:** CHANGES REQUESTED — 0 Kritiskt / 1 Viktigt / 1 Nice-to-have
- **Verdict re-check:** round-1 Viktigt CLOSED; 1 new-in-delta Viktigt, closable by measurement
- **Escalations:** none

## Round 1

The drop is correctly layered, complete in code, and leaves no hole against 6a. The Viktigt is a
surviving **prose** residual that an identifier grep cannot see by construction.

### Viktigt — `MappedPlaintextExposureRegistry.cs:107-111`

The written ground for `AspNetUsers` still claimed the table *"Holds email, normalized_email,
user_name and normalized_user_name (…), phone_number, password_hash, **and the OAuth provider's
identifier for her**"*. That is the prose name for `provider_user_id`, which this commit drops.
After 6d the identifier lives only in `AspNetUserLogins.provider_key`, which has its own entry
three lines down. The sentence is false at HEAD.

**Why it survived every gate:** the pin `Every_person_grained_table_carries_a_written_ground`
measures **length only** (`< 60` chars), never content. So the false sentence survives
Architecture 584/0, and the session's grep-based acceptance could not see it — the residue is
prose, not an identifier. `ModelSweep` is model-derived and self-healing, so there is no second
stale entry; this is the only one.

**Closure path:** the false text sits mid-string across two lines, so a repair **adds `+` lines**
→ not a mechanical closure; owes one scoped report-only re-check.

### Nice-to-have — the enum-to-string convention has no canonical home

The removal of `ApplicationUserConfiguration`'s top comment is right, but it surfaces that the
convention has no home: live on ~7 `HasConversion<string>()` sites, written twice as per-site
comments, but absent from BUILD.md §7, CLAUDE.md, every ADR, and every architecture test
(measured: no `IsEnum`/`GetProviderClrType` guard). **The misplacement predates the diff** — a
repo-wide EF convention documented in the Identity context's only configuration was already
invisible to whoever adds an enum in the app model. The removal creates no regression.

→ Filed as [#1753](https://github.com/klasolsson81/jobbliggaren/issues/1753).

### Answers to the session's four questions

1. **Complete and correctly layered — yes.** `AuthProvider` was an Identity/Infrastructure
   concept; Domain and Application never see the type. The Designer file is byte-identical to
   the snapshot in the model body. `ProductVersion` 10.0.9 → 10.0.10 matches the pin and
   `AppDbContextModelSnapshot`. `generated_code = true` in `.editorconfig:27` covers the
   migrations folder, so the inline array CA1861 once forced out of the 2026-05-06 migration
   needs no extraction here. `Down` mirrors `AddAuthProviderToUser`'s `Up` exactly; the round
   trip is **lossless** — but only because the columns are measured unwritten.
2. **No hole against 6a, and the most important guarantee survives the swap.**
   `AspNetUserLogins` exists since `InitialIdentity:125` with `HasKey("LoginProvider","ProviderKey")`.
   **The uniqueness guarantee the dropped filtered index carried — one account per
   (provider, subject) — is carried by the composite primary key instead, and per provider rather
   than globally.** No invariant is lost; it becomes stricter in the right way. The cascade FK
   means the Art. 17 path reaches 6a's `provider_key` automatically.
3. **Both comment calls right, for different reasons.** The move in `ApplicationUser.cs` is right:
   the sentence is a claim about the **class**, not about two properties. Left where it was, it
   would read as applying to `CreatedAt` — true but misleadingly narrow. The deletion in
   `ApplicationUserConfiguration.cs` is right and stronger than "unnecessary": after the drop the
   file contains **no enum at all**, so a comment motivating `HasConversion<string>()` there is
   not redundant but **false-in-direction** — a pointer to nothing.
4. **`Worker.IntegrationTests/Migrations/` is the right home, and in practice the only one.** It
   is the sole test project that links `TestDatabaseProvisioner`, references
   `Jobbliggaren.Migrate.Provisioning`, and already hosts the migration-journey pattern. The
   "Worker" name over a non-Worker test is a pre-existing skew the two sibling tests established.

## Scoped re-check (`5ec3a1e6..62c169f0`, report-only, static)

**Round-1 Viktigt — CLOSED, measured rather than inherited.** A grep for "provider's identifier"
over `src/` + `tests/` returns **one** hit: the `AspNetUserLogins` entry with capitalised
`FOR HER`, correct for that table. The ground now ends `"…phone_number and password_hash."` and
the length stays far above the 60-char floor, so the pin is still non-vacuous.

> The session's separate measurement of the neighbouring sentence holds, and I verified it
> independently rather than inheriting it: `MappedPlaintextExposureRegistryTests.cs:201` names the
> two as `email` and `name`, `ModelSweep.cs:29` says the same. Both survive the drop → the
> sentence stays true. Right to measure it separately: it is precisely the sentence that would
> have fallen with the bind had the two been `email` and `provider_user_id`.

### New-in-delta Viktigt — the rewritten test body has no reported measurement

The delta rewrote the test body substantially, not just the docblock: `column_default` added to
the catalog read, a new exact-string pin `Default.ShouldBe("'Local'::character varying")`, the
journey reordered so the insert happens at **head**, and the INSERT dropping both columns. The
reported measurement was `Architecture total: 584, failed: 0` — **that suite does not run this
test**, and the base's `1/1 green + four red mutants` measured a **different test body**.

**Closure:** run the one test and write the `total:` line into the verdict table. No code change,
no added prose — §9.6 allows that path explicitly, and it owes **no further re-check**.

### What I missed in round 1 — and the delta fixed

The old docblock claimed `TestDatabaseProvisioner` *"lets `AppIdentityDbContext`'s migrations run
as the role production migrates with rather than as the container's superuser"*. **That was false,
and I let it through.** Measured now: `Program.cs:362-363` runs
`BuildIdentityOptions(masterCs)` — **master credentials**. Production does **not** migrate this
context as the app role. The delta's replacement is **true**, and the divergence points the safe
way: the test can fail on a privilege production never meets, never the reverse.

### Graded and passed

- The reorder is a **tightening**, not a rewrite for its own sake: previously `Down` ran against an
  **empty** table, so a `Down` adding `provider NOT NULL` **without** default would have passed.
- Removing the §5 `Tests:` premise paragraph is correct **and necessary** — the insert no longer
  writes those columns, so the paragraph's subject is gone.
- Filing the nice-to-have as an issue rather than a named skip is right, and for the stated
  reason: EF configurations are a surface several lanes touch.
