# db-migration-writer — 5b (#1857) form round, report-only, against `41a49394`

Transcribed verbatim from the agent's SubagentHandback payload (extracted from its transcript), 2026-09-25.
Brief: `docs/sessions/2026-09-25-1857-form-brief.md`.

## db-migration-writer — form round for 5b (#1857, ADR 0142 Amendment (11)), Q1–Q4

Report-only. Nothing edited, no migration scaffolded, no `dotnet ef` run against a file or DB. All citations re-measured at HEAD `41a49394` in the worktree.

### Facts re-measured (all hold)

- **Fact 1 (schema).** `AppIdentityDbContextModelSnapshot.cs:75-77,87-89` — `PasswordHash`/`SecurityStamp` are both `text`, no `.IsRequired()`, no `HasMaxLength` → nullable in Postgres. `ConcurrencyStamp` (`:37-40`) carries `.IsConcurrencyToken()`. Confirmed.
- **Fact 2 (no C# throw in Down; the four precedents).** Read all four in full:
  - `20260704013134_NullResumeVersionLegacyContent.cs` — `internal const string PreconditionGuardSql`/`NullOutSql`; `Down` is comment-only, explicitly "NON-RESTORING BY DESIGN" (`:66-74`).
  - `20260717232352_PurgeStaleGradeGateMatches.cs` — `internal const string PurgeSql`, a `DO $$ … GET DIAGNOSTICS deleted_count = ROW_COUNT; RAISE NOTICE …` block (`:89-125`); `Down` is a documented no-op.
  - `20260920232535_DisplayNameNullable.cs` — `Down` is a *conditional* SQL `RAISE EXCEPTION` inside `DO $$…$$` (`:33-45`), only when a violating row exists, then proceeds to actually revert the column.
  - `20260703074121_AddApplicationUserCreatedAt.cs:33-34` — the only prior UPDATE against `identity."AspNetUsers"`, with an explicit lock note.
  None throws a bare C# exception. Confirmed — this repo has no precedent for that specific form, which is itself a data point for Q2.
- **Fact 3 (the journey-test break).** `DropAuthProviderColumnsMigrationTests.cs:188` (`db.Database.MigrateAsync(ct)` → assembly **head**), `:200` (`IMigrator.MigrateAsync(PreviousMigration, ct)` → **DropRefreshTokens**), `:225` (head again). Once `NullPasswordHashes` exists, "head" at `:188`/`:225` becomes `NullPasswordHashes`, and the rollback at `:200` must revert **both** `NullPasswordHashes` and `DropAuthProviderColumns` in one call. A throwing `Down` on `NullPasswordHashes` aborts that call at the first (newest) migration in the chain — confirmed as a real break, not a hypothetical.
- **Fact 4 (no CommandTimeout on Identity options; bootstrap has no schema-ahead gate).** `MigrationsOptionsFactory.cs:53-61` — `BuildIdentityOptions` sets `MigrationsAssembly` + `MigrationsHistoryTable`, no `.CommandTimeout(...)` (contrast `BuildAppOptions:35`, which sets 600s). `Migrate/Program.cs:365-380` (`RunBootstrapAsync`) reads `GetPendingMigrationsAsync` and calls `MigrateAsync` directly under master creds — no schema-ahead-gate call comparable to Phase E's `EvaluateSchemaAheadGate`. Confirmed.
- **`vps-deploy-stack.md:453-537` (§3c), read in full.** This section today is written **only for 6d** (`DropAuthProviderColumns`) and explicitly **excludes 5b**: precondition 5 (`:490-494`) says *"A migration without one (5b's, which throws) is not run by this procedure until its own PR adds a backup decision `security-auditor` has signed"* and *"No dump is taken … The `Down` script is the backup."* The rollback section (`:529-534`) generates a `Down` script for the "after a successful apply" branch — this literally cannot be produced for 5b once `Down` throws at operation-generation time (see Q2). This is the brief's fact 9, and it's exactly right — the whole precondition-5/rollback text needs 5b-specific additions once security-auditor signs the backup decision (her Q5).
- **`NoPasswordSymbolTests.cs` read in full.** `AuthSurfaceAssemblies()` (`:30-36`) is currently Domain/Application/Api/Worker only; the docblock (`:16-18`) explains Infrastructure is out of scope today because of `RemovePasswordAsync`. Confirmed this is the CTO-F3-flagged predicate that Q13 (dotnet-architect) needs to redesign before Infrastructure can be added — my SQL text will trip the literal-string arm the moment that happens (see Finding 2 below). Not something I can or should design around.
- **`Jobbliggaren.Infrastructure.csproj:222`** — `InternalsVisibleTo` already grants `Jobbliggaren.Worker.IntegrationTests` access to `internal` migration constants, matching the brief's placement of the journey test there (sibling to `DropAuthProviderColumnsMigrationTests`/`UnmapJobSeekerDisplayNameMigrationTests`).
- **Const-SQL test idiom, confirmed by reading the actual usage**, not just the doc comments: `PurgeStaleGradeGateMatchesMigrationTests.cs:186,203` and `ResumeContentEncryptionTests.cs:659` both call `db.Database.ExecuteSqlRawAsync(<Migration>.<Const>, ct)` directly — idempotency is proved by re-invoking the const and re-reading downstream state, never by parsing `RAISE NOTICE` output. This is the exact mechanism Q15's "re-run the const SQL → 0 rows" step will use.
- **`gen_random_uuid()` as core in PG18** — not independently re-verified against Postgres docs this round (no DB, no web access exercised), but this matches both the brief's own claim and my own system prompt's UUIDv7 section ("`gen_random_uuid()` (PostgreSQL 18 default, produces UUIDv4)"), so I'm treating it as given rather than re-deriving it.
- **One brief claim I could not confirm from EF Core source, so I checked it directly**: whether `IrreversibleMigrationException` (one of Q2's three named options) is a real EF Core type. Built a throwaway console app referencing `Microsoft.EntityFrameworkCore.Relational 10.0.10` and reflected over `typeof(Migration).Assembly.GetExportedTypes()` for anything matching `*Irreversible*` or `*Migration*Exception*`. **Zero matches.** That type does not exist in EF Core 10.0.10 — it is off the table for Q2, not a real third option.

### Q1 — the Up

```csharp
// SPOT: the exact statement this migration applies, exposed so the Testcontainers journey test
// (Worker.IntegrationTests, InternalsVisibleTo) re-runs the REAL predicate for the idempotency
// assertion instead of a hand-copied twin (repo precedent: NullResumeVersionLegacyContent.NullOutSql /
// PurgeStaleGradeGateMatches.PurgeSql).
internal const string NullPasswordHashesSql =
    """
    DO $$
    DECLARE
        nulled_count integer;
    BEGIN
        UPDATE identity."AspNetUsers"
        SET password_hash = NULL,
            security_stamp = gen_random_uuid()::text
        WHERE password_hash IS NOT NULL;

        GET DIAGNOSTICS nulled_count = ROW_COUNT;
        RAISE NOTICE '#1857 crypto-erasure (NullPasswordHashes): nulled password_hash and rotated security_stamp for % row(s) in identity."AspNetUsers" (ADR 0142 Amendment (11)).', nulled_count;
    END $$;
    """;

protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(NullPasswordHashesSql);
}
```

Point-by-point:
- **The random expression**: `gen_random_uuid()::text`. This produces the same lowercase-hyphenated shape as ASP.NET Identity's own `NewSecurityStamp()` (`Guid.NewGuid().ToString()`, "D" format), so the stamp stays "an opaque string to Identity" (brief's own phrase) — nothing downstream can distinguish a migration-rotated stamp from an app-rotated one by shape.
- **`concurrency_stamp` does NOT rotate in the same statement.** ADR 0142 (`:1285-1286`) and #1857's acceptance both name exactly two columns — `password_hash` nulled, `security_stamp` rotated — never a third. `concurrency_stamp` is EF's own optimistic-concurrency token (`.IsConcurrencyToken()`), checked only by a writer that already holds the row in memory (`UserStore.UpdateAsync`'s `WHERE concurrency_stamp = @original`); it is not an invalidation mechanism the way `security_stamp` is (fact 6 is entirely about `security_stamp`). Rotating it would be scope creep past the bound decision.
  - I want to flag, not silently resolve, the narrow race the brief's own ⚠ points at: a request that `FindByEmailAsync`'d the row *before* the migration runs and calls `UserManager.UpdateAsync` (via `IdentityInboxProofRecorder`) *after* it commits would write with `WHERE concurrency_stamp = <unchanged value>` — which still matches, since we never touched it — and could silently put a (stale, in-memory) password hash back. Rotating `concurrency_stamp` too would turn that into a loud `DbUpdateConcurrencyException` instead of a silent clobber. I did **not** add it, because (a) it's outside the bound decision's literal scope, and (b) §3c's read-back (`count(*) where password_hash is not null`, expected `0`) runs inside the *same locked shell sequence* immediately after apply, so this failure mode is caught before the operator releases the lock, even without touching `concurrency_stamp`. I'm routing this to security-auditor's Q11 (which is exactly about the recorder's write path once no password exists) rather than deciding it myself.
- **`DO $$` wrapper reporting the row count**: yes, `GET DIAGNOSTICS … RAISE NOTICE`, matching `PurgeStaleGradeGateMatches` exactly — this is what makes §3c's "Read the output in the foreground" (`:502`) contract meaningful for 5b too, without ever `SELECT`ing a hash value into a log.
- **Internal const form**: `internal const string NullPasswordHashesSql` on the migration class, visible to `Jobbliggaren.Worker.IntegrationTests` via the existing `InternalsVisibleTo` (`Jobbliggaren.Infrastructure.csproj:222`) — no new project wiring needed.
- **Row-lock notes**: this is a plain DML `UPDATE`, not `ALTER TABLE`/`ADD COLUMN` — no `ACCESS EXCLUSIVE` lock (contrast `AddApplicationUserCreatedAt`'s documented lock, `:14-20`). It takes `ROW EXCLUSIVE` on the table and row-level locks on the touched rows only, held for the duration of this one statement inside the migration's transaction, released at commit. With "the box holds 2 rows" (brief fact 4), this completes in well under a millisecond of actual lock time; the Npgsql default 30 s `CommandTimeout` (fact 4 — no override on `BuildIdentityOptions`) is not a real constraint here, unlike the 600 s override `BuildAppOptions` needed for the trigram-index build.

### Q2 — the Down

```csharp
/// Not a data-dependent refusal (contrast DisplayNameNullable's RAISE EXCEPTION guard, which only
/// fires when a violating row exists) — this is unconditional. The hash material Up() destroys has
/// no backup (security-auditor's Q5 / §3c precondition 5: a dump would be a plaintext copy of the
/// very thing this migration exists to destroy), so there is nothing to restore, ever.
protected override void Down(MigrationBuilder migrationBuilder) =>
    throw new NotSupportedException(
        "NullPasswordHashes cannot be reversed: password_hash is Art. 17 crypto-erased, not backed up " +
        "(ADR 0142 Amendment (11); security-auditor's Q5 / §3c precondition 5 — a dump would be a " +
        "plaintext copy of the hashes this migration destroys). There is no value to restore.");
```

- **Which form, and why not the other two.** `IrreversibleMigrationException` is not a real EF Core type (verified above — off the table). Between a bare C# `throw` and a SQL `RAISE EXCEPTION` via `migrationBuilder.Sql(...)` (the `DisplayNameNullable` idiom): I chose the **C# throw**, unconditionally, because our refusal has no data-dependent condition to check (contrast `DisplayNameNullable`, which only refuses *if* a nameless row exists and otherwise proceeds) — there is nothing to `SELECT` before deciding. A bare `throw` also fires at **operation-generation time**, before `migrationBuilder`'s operations are even converted to SQL and before any transaction opens — which means `dotnet ef migrations script <head> <previous> --context AppIdentityDbContext` **fails to produce a script at all**, not merely to apply one. A SQL-based refusal would let script generation succeed (producing an artefact that only fails when actually run), which is a weaker form of "cannot be reversed" than what ADR 0142's plain-English "an explicit throw" (`:1804-1805`) reads as.
- **What surfaces to `IMigrator.MigrateAsync(previous)`**: the `NotSupportedException` propagates unwrapped out of the `Down()` call during operation generation — this happens for the *newest* migration in the down-chain first (matching how EF reverts in reverse-chronological order), so in `DropAuthProviderColumnsMigrationTests`'s post-5b scenario, `NullPasswordHashes.Down()` throws before `DropAuthProviderColumns.Down()` is even invoked. No SQL for either migration is generated or sent to the database. I'm stating this with high confidence from documented EF Core mechanics (script/operation generation is a pure in-memory step, run before `MigrationCommandExecutor` touches the connection) but flagging that I have not executed it this round — no migration exists yet and DB access is out of scope for this brief. This is precisely what the journey test (Q15) needs to pin empirically once the migration exists.
- **What the history holds after the refusal**: unchanged — `identity."__EFMigrationsHistory"` still lists `NullPasswordHashes` (and everything after it, if any) as applied, because the DELETE-from-history statement that a successful `Down` would emit is never generated, let alone executed.
- **§3c's rollback text (`:529-534`) cannot apply to 5b** — confirmed exactly as the brief states (fact 9). The "after a successful apply: generate the `Down` script … apply it … read back in reverse" branch has no meaning here; only "a failed apply: the migration ran in a transaction and rolled back. Read back and stop." (`:531`) is reachable for 5b's `Up`, since `Up` has no unconditional guard of its own to trip.

### Q3 — the scaffold

- `dotnet ef migrations add NullPasswordHashes --context AppIdentityDbContext -o Identity/Migrations`, run from `src/Jobbliggaren.Infrastructure` — no `--startup-project` needed, since `DesignTimeIdentityDbContextFactory.cs` (`:11-22`) implements `IDesignTimeDbContextFactory<AppIdentityDbContext>` self-contained (its own hardcoded local connection-string fallback at `:19`, one of fact 7's six arms).
- **Snapshot/Designer diff**: since this migration touches no `Property<>`/`Column<>`/`HasConversion` mapping — only raw SQL in `Up`/`Down` — the scaffold produces an **empty model diff**: EF regenerates `AppIdentityDbContextModelSnapshot.cs` and the new migration's own `.Designer.cs`, both reflecting the *unchanged* current model (same `ProductVersion` annotation, same properties). I have not run the scaffold this round (report-only), but this is exactly the shape of both `NullResumeVersionLegacyContent` and `PurgeStaleGradeGateMatches` — two prior "empty-diff, hand-authored SQL" data migrations in this same repo — so I'm confident in this prediction rather than guessing blind.
- **Name**: `NullPasswordHashes`, matching the brief's own example and the verb-prefixed convention of its siblings (`NullResumeVersionLegacyContent`, `PurgeStaleGradeGateMatches`).
- **No `EnsureSchema`**: confirmed by reading both `AddApplicationUserCreatedAt.cs` and `DropAuthProviderColumns.cs` in full — neither calls it, because `identity` schema creation is a Bootstrap-mode, master-creds SQL step (`Migrate/Program.cs:348-355`, `CREATE SCHEMA IF NOT EXISTS`), not something any individual `AppIdentityDbContext` migration does. `NullPasswordHashes` follows the same pattern.

### Q4 — the destructive protocol for a data migration

My charter's enumerated destructive list (`DROP TABLE`/`DROP COLUMN`/`ALTER COLUMN` type change/rename/`NOT NULL` without default) doesn't literally name "bulk UPDATE to NULL," but the top-level mandate — *"Destructive migrations require explicit user approval… you never apply a migration that drops columns or tables without Klas confirming"* — covers this in spirit: it's irrecoverable data loss, by design. Treating it as **⚠ DESTRUCTIVE**:

1. **Generate** — not done this round (report-only); owed in the implementation PR.
2. **`dotnet ef migrations script`** — the implementation session should run this and paste the generated SQL into the PR body per my Output format, so the reviewers see the literal statement, not just the C# wrapper.
3. **Stop before applying** — my tool surface's `dotnet ef database update *` gate (ask-listed) only reaches a **local dev** Postgres; it is **not** the apply path for this migration at all. The only sanctioned apply path is the operator-run `migrate bootstrap` under §3c, gated by Klas's GO *per run* and (once she signs it) security-auditor's backup decision. No session should ever run `dotnet ef database update` against anything but a local/Testcontainers database for this migration.
4. **⚠ DESTRUCTIVE header** — this report constitutes that flag for the form round; the implementation PR carries it forward.
5. **Export step: N/A, correctly** — per the brief. My charter's "include a data-migration step if one can preserve data" instruction is inverted here: preserving the data (a dump of hashes) is exactly the outcome ADR 0142/Q5 rules out. There is no safe backfill-before-drop shape for a crypto-erasure migration; that's the whole point.
6. **Approval** — already given at the ADR level (Klas, ADR 0142 answer 3, "Ja, radera", 2026-09-18) for the migration *existing*. The **apply** step needs its own, separate, per-run Klas GO through §3c — which is a *stricter* gate than my charter's baseline, not a weaker one.

**Pre-read / read-back proposal (counts only, mine — security-auditor's Q5 signs the exact §3c wording and the backup-decision text)**, mirroring 6d's exact form at `vps-deploy-stack.md:487-489,516-521`:

Pre-read (before `bootstrap` runs), added as 5b's own numbered item alongside 6d's precondition 4 — not a "must be 0" gate like 6d's (6d's pre-read asserts an invariant that must already hold; 5b's pre-read is informational, to compare against the read-back):
```sql
select count(*) from identity."AspNetUsers" where password_hash is not null;
```

Read-back (inside the same locked shell, immediately after apply):
```sql
docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -tAc \
  "select count(*) from identity.\"__EFMigrationsHistory\" where migration_id = '<NullPasswordHashes migration id>';"
docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -tAc \
  "select count(*) from identity.\"AspNetUsers\" where password_hash is not null;"
```
Expect `1` and `0`. **No schema-shape check is needed** (contrast 6d's `information_schema.columns`/`pg_indexes` reads) — 5b makes no schema change; `password_hash`/`security_stamp` stay exactly as nullable `text` as before. Never `select password_hash` or any column value — counts only, per the credential-hygiene discipline §3c already states for 6d (`:508-511`).

**Rollback line**: needs an explicit 5b-specific carve-out in §3c, not a silent inheritance of 6d's text — "After a successful apply: generate the `Down` script…" (`:532-534`) has no meaning for 5b once `Down` throws at generation time (Q2). Only "a failed apply… rolled back. Read back and stop." survives for 5b.

### Migration concerns noted in other questions (not mine to answer, flagged per the brief's ask)

- **Q14's retarget of `DropAuthProviderColumnsMigrationTests`** — confirmed necessary, not hypothetical (fact 3 above). The fix is the `UnmapJobSeekerDisplayNameMigrationTests:28-29` precedent: replace `db.Database.MigrateAsync(ct)` at `:188` and `:225` with `db.GetService<IMigrator>().MigrateAsync(ThisMigration, ct)` (pinning `ThisMigration = "20260917195454_DropAuthProviderColumns"` as the stop point), so the test stays meaningful regardless of what gets added to the Identity assembly after it. This must land in the **same PR** as `NullPasswordHashes` (or immediately before it merges), since adding the new migration alone breaks the existing test on its current form.
- **Q15's journey test** — the const-SQL re-run idiom (`ExecuteSqlRawAsync(NullPasswordHashes.NullPasswordHashesSql, ct)` twice, asserting `count(*) where password_hash is not null` = 0 both times) is exactly the mechanism `PurgeStaleGradeGateMatchesMigrationTests.cs:186,203` already uses — no new test infrastructure needed. The `Down`-refuses assertion should use `Should.ThrowAsync<NotSupportedException>()` (or whatever type Q2's panel settles on) around `db.GetService<IMigrator>().MigrateAsync(<previous>, ct)`, then re-read `GetAppliedMigrationsAsync` to confirm `NullPasswordHashes` is still listed (per Q2's "history holds" analysis above) — that's the concrete shape of "the history still holds 5b" the brief's Q15 asks for.

### Grading

- **Blocker**: none.
- **Major**: none.
- **Minor** (routing notes, not defects — none need a GitHub issue since they're already the named subject of another panel agent's question in this same round):
  1. The `concurrency_stamp`-untouched race window (Q1 above) — routed to security-auditor's Q11, not resolved unilaterally.
  2. My SQL/exception text will trip `NoPasswordSymbolTests` once Infrastructure enters scope (fact 7's sixth arm, confirmed by reading the test) — routed to dotnet-architect's Q13; not something I should or can design around (the column is genuinely named `password_hash`).
  3. §3c's precondition-5 sentence "Every migration in the set has a `Down` that restores the state the pre-read measured" / "The `Down` script is the backup" will need a 5b-specific carve-out once 5b enters the runnable set — flagged for whoever writes 5b's §3c diff (likely alongside security-auditor's Q5 sign-off), not a defect in the current text (which correctly excludes 5b today).

### Escalation to Klas

None from this domain. ADR 0142 already carries Klas's binding decision for everything Q1–Q4 touch; my design fits inside it without needing a fresh decision from him. The open items above are all already addressed to the right panel agent by the brief's own Q5/Q11/Q13, not gaps that need Klas directly.
