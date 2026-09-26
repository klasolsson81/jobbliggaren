# db-migration-writer — #1747 (epic #1732 part 6d)

- **PR:** #1752 · **Branch:** `chore/drop-auth-provider-1747` · **Base:** `d90414f3`
- **Round 1 against:** `5ec3a1e6` · **Scoped re-check against:** `62c169f0`
- **Migration:** `20260917195454_DropAuthProviderColumns` (`AppIdentityDbContext`) — DESTRUCTIVE
- **Verdict round 1:** 0 Blocker / 0 Major / 2 Minor · **Re-check:** 0 / 0 / 0 remaining
- **Escalations:** none

## Round 1

Regenerated Up and Down SQL independently — byte-for-byte identical to the session's,
including transaction boundaries and the history insert.

| Check | Verdict |
|---|---|
| Locks | `ACCESS EXCLUSIVE` on both, but metadata-only (no table scan); negligible |
| Table rewrite | None. `DROP COLUMN` marks `pg_attribute.attisdropped`; `ADD COLUMN ... DEFAULT 'Local'` is a non-volatile literal → PG 11+ fast path |
| `CONCURRENTLY` | Not applicable. `DROP INDEX` is O(1); forcing it would need `suppressTransaction: true` and split atomicity. Right call to decline |
| Transaction boundaries | One `START TRANSACTION; … COMMIT;` including `__EFMigrationsHistory`. Atomic |
| `IF EXISTS` | Absent and correct — idempotence comes from the history table |
| Index-before-column (Up) | **Correct, and load-bearing.** Reversed, the column drop would take the index silently and the explicit `DropIndex` would hit `index does not exist` |
| Column-before-index (Down) | Correct mirror; only valid order |
| History table in `identity` schema | Confirmed, matches `HasDefaultSchema("identity")` |

**Zero information loss — verified, holds.** Broader grep across the whole repo (not just
`src/`, excluding `Migrations/`) at branch HEAD: zero hits. Grounded in a dated, named
measurement (`081e4c67`, ADR 0017's 2026-09-17 amendment).

**Down's `NOT NULL DEFAULT 'Local'` on a populated table in PG 18 — safe.** Non-volatile
literal → fast-default path, no rewrite. The restored partial unique index cannot collide:
every pre-existing row gets `provider_user_id = NULL` when a nullable column without default
is re-added.

**Privilege model (extra verification, unrequested).** `PhaseASchemaGrants.IdentitySchema`
grants `jobbliggaren_app` `USAGE, CREATE` on `identity`, so that role originally ran
`CREATE TABLE identity."AspNetUsers"` and therefore owns it. Ownership carries full
`ALTER`/`DROP`. No 42501 risk at deploy.

### Minor 1 — Down's `NOT NULL DEFAULT` step is never tested against a populated table

The journey inserted its row *after* the rollback, so `Down` only ever ran against an empty
catalog. A mutation removing `defaultValue: "Local"` would not be caught: on an empty table
Postgres accepts `NOT NULL` without a default, while the same statement against rows fails
with `column "provider" contains null values`. Not a defect in the migration — the
`defaultValue` is present and PG 11+ behaviour makes it safe regardless of row count.

*(Same hole `test-writer` graded Major. Severity belongs to each reporting agent; the session
took the stricter grade for disposal.)*

### Minor 2 — zero-information-loss is a code-based proof, not a data-based one

Grep covers what `src/` can have written, not a hand-written `UPDATE` or seed outside it.
Recommendation, costs nothing: run
`SELECT DISTINCT provider, provider_user_id FROM identity."AspNetUsers";` against the actual
target immediately before apply — expect exactly one row, `('Local', NULL)`.

### Destructive protocol status

1. Migration created ✓ · 2. Script run ✓ (independently, identical) · 3. **Stopped** — no
`database update` run ✓ · 4. Report ✓ · 5. Data migration/backfill: N/A, nothing to preserve
· 6. **Awaiting explicit approval before apply** — and out of scope for this session anyway
(shared-Postgres rule; Testcontainers has already applied/reversed/applied on real Postgres 18
as the app role).

## Scoped re-check (`5ec3a1e6..62c169f0`, report-only, static)

Migration file confirmed **unchanged** — empty diff, byte-identical.

**Minor 1 — CLOSED.** The journey is now head → insert → previous → head. Three independent
checks on the populated rollback: the catalog's `column_default`, the nullable column's null
default, and the specific row's own values after rollback. Counted through both named mutants
against the new code; both consistent with the session's measured RED.

> The chosen solution (one account riding the whole journey) is cleaner than the one I
> sketched (a second account inserted before the rollback) — same thing proven, less code,
> and the identity verified at each step is the same throughout.

**Minor 2 — nothing to close in this delta.** Belongs to the apply moment; disposal as
scheduling, not fact, is correct per §9.6.

**New findings in the delta: none.** Protocol point 6 remains unaddressed by this session.
