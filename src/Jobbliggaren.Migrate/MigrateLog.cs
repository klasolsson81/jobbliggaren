using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Migrate;

// LoggerMessage source-gen per repo-konvention (CA1848). Top-level Program.cs
// anropar dessa istället för LogInformation/LogError direkt.
internal static partial class MigrateLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Starting Migrate: host={Host}:{Port} db={Db}")]
    public static partial void StartingMigrate(ILogger logger, string host, int port, string db);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Master creds loaded ({Phase}): user={User}")]
    public static partial void MasterCredsLoaded(ILogger logger, string user, string phase);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information,
        Message = "Phase A: REVOKE PUBLIC + CREATE ROLE x 3 + GRANTs + CREATE SCHEMA hangfire")]
    public static partial void PhaseAStart(ILogger logger);

    [LoggerMessage(EventId = 20, Level = LogLevel.Information,
        Message = "Phase B: Hangfire schema-install (PostgreSqlObjectsInstaller)")]
    public static partial void PhaseBStart(ILogger logger);

    [LoggerMessage(EventId = 21, Level = LogLevel.Information,
        Message = "Hangfire schema-install COMPLETE")]
    public static partial void HangfireInstallComplete(ILogger logger);

    [LoggerMessage(EventId = 30, Level = LogLevel.Information,
        Message = "Phase C: GRANT hangfire.* till jobbliggaren_worker + ALTER DEFAULT PRIVILEGES")]
    public static partial void PhaseCStart(ILogger logger);

    [LoggerMessage(EventId = 50, Level = LogLevel.Information,
        Message = "Migrate COMPLETE — Worker kan nu (re)startas")]
    public static partial void MigrateComplete(ILogger logger);

    [LoggerMessage(EventId = 100, Level = LogLevel.Information,
        Message = "CREATE/ALTER ROLE {Role} OK")]
    public static partial void CreateOrAlterRoleOk(ILogger logger, string role);

    [LoggerMessage(EventId = 101, Level = LogLevel.Information,
        Message = "OK: {Description}")]
    public static partial void StatementOk(ILogger logger, string description);

    [LoggerMessage(EventId = 999, Level = LogLevel.Error,
        Message = "Migrate FAILED")]
    public static partial void MigrateFailed(ILogger logger, Exception ex);

    // ADR 0033 — Phase E (EF Core MigrateAsync) + CLI-dispatch
    [LoggerMessage(EventId = 60, Level = LogLevel.Information,
        Message = "Phase E: EF Core Database.MigrateAsync mot AppDbContext (jobbliggaren_app-creds)")]
    public static partial void PhaseEStart(ILogger logger);

    [LoggerMessage(EventId = 61, Level = LogLevel.Information,
        Message = "Pending migrations: {Count}")]
    public static partial void PendingMigrationsCount(ILogger logger, int count);

    [LoggerMessage(EventId = 62, Level = LogLevel.Information,
        Message = "  -> {Migration}")]
    public static partial void PendingMigrationItem(ILogger logger, string migration);

    [LoggerMessage(EventId = 63, Level = LogLevel.Information,
        Message = "Phase E COMPLETE — applied {Count} migration(s)")]
    public static partial void PhaseEComplete(ILogger logger, int count);

    [LoggerMessage(EventId = 64, Level = LogLevel.Information,
        Message = "Phase E: no pending migrations — schema is up-to-date")]
    public static partial void PhaseENoPending(ILogger logger);

    // #1236 — the schema-ahead gate. Multi-line messages under the SingleLine console formatter
    // are established practice here (ExplainSearchResult); the newlines keep the operator text
    // readable in `docker logs jobbliggaren-migrate`, which is where this is read during an
    // incident. Wording follows the reconcile wrapper's REFUSING template: what was measured,
    // the diagnosis, the repair.
    [LoggerMessage(EventId = 65, Level = LogLevel.Error,
        Message = "REFUSING schema migration (exit 3): the database is AHEAD of this image.\n"
                + "__EFMigrationsHistory holds {UnknownCount} migration(s) this assembly does not contain: {UnknownIds}.\n"
                + "This is what a backwards-pinned IMAGE_TAG looks like — the tag rolled back CODE; the schema did not come with it.\n"
                + "api, worker AND web have already been stopped for recreation and are held down by\n"
                + "service_completed_successfully — the public site answers 502/503 until an exit is taken.\n"
                + "That outage is deliberate: fail-closed beats silent old-code-on-newer-schema (#1236).\n"
                + "Exits, pick one:\n"
                + "  (1) roll forward: restore IMAGE_TAG to a tag whose build contains these migrations, re-run the reconcile unit;\n"
                + "  (2) treat it as a RESTORE problem, not a deploy problem: docs/runbooks/backup-restore.md;\n"
                + "  (3) deliberately run old code against the newer schema: set\n"
                + "      MIGRATE_ALLOW_SCHEMA_AHEAD={UnknownIds}\n"
                + "      in deploy/.env and re-run the reconcile unit. No migrations will run; remove the key after the incident.\n"
                + "Doctrine: docs/runbooks/vps-deploy-stack.md §3a.")]
    public static partial void RefusedSchemaAhead(ILogger logger, int unknownCount, string unknownIds);

    [LoggerMessage(EventId = 66, Level = LogLevel.Error,
        Message = "The override did not match. MIGRATE_ALLOW_SCHEMA_AHEAD must equal the exact "
                + "unknown-ID set (comma-separated, order-insensitive) — never `1` or a partial list, "
                + "so a leftover value cannot bless a LATER accidental pin.\n"
                + "  provided: {Provided}\n"
                + "  expected: {Expected}")]
    public static partial void RefusedOverrideMismatch(ILogger logger, string provided, string expected);

    // Exit 4's text names the squash case itself: an ADR is not in the journal, and #1329
    // measured exactly that defect class — a prerequisite living where no deployer reads.
    [LoggerMessage(EventId = 67, Level = LogLevel.Error,
        Message = "REFUSING schema migration (exit 4): migration histories have DIVERGED.\n"
                + "__EFMigrationsHistory holds {UnknownCount} migration(s) this assembly does not contain: {UnknownIds}\n"
                + "AND the assembly holds {PendingCount} migration(s) the database has not applied: {PendingIds}.\n"
                + "Neither side is a prefix of the other, so NO automatic apply is safe and no override applies.\n"
                + "This shape arises when histories fork — a migration squash/re-baseline, or a branched migration set.\n"
                + "If an image exists whose assembly contains BOTH sets, deploy it. Otherwise STOP and establish\n"
                + "the cause; nothing may write to __EFMigrationsHistory before it is established\n"
                + "(doctrine: docs/runbooks/vps-deploy-stack.md §3a).")]
    public static partial void RefusedDivergence(
        ILogger logger, int unknownCount, string unknownIds, int pendingCount, string pendingIds);

    [LoggerMessage(EventId = 68, Level = LogLevel.Warning,
        Message = "MIGRATE_ALLOW_SCHEMA_AHEAD matched the unknown set ({UnknownIds}) — skipping "
                + "MigrateAsync entirely and exiting 0 so api/worker may start.\n"
                + "This run certifies NOTHING about schema compatibility; that judgment was the operator's.\n"
                + "Remove the key from deploy/.env once the incident is over.")]
    public static partial void OverrideConsumed(ILogger logger, string unknownIds);

    [LoggerMessage(EventId = 69, Level = LogLevel.Information,
        Message = "MIGRATE_ALLOW_SCHEMA_AHEAD is set but the schema is not ahead of this image — "
                + "remove it from deploy/.env. A stale override is the hazard the ID-set design "
                + "exists for, not a convenience.")]
    public static partial void OverrideIdle(ILogger logger);

    // The one operator path where exit 3's instruction (3) cannot work: the box's compose file
    // predates #1236 and forwards no override key at all. Null-vs-empty is the discriminator —
    // compose renders an unset `${VAR:-}` as an EMPTY string, so a truly absent variable means
    // the running compose file has no passthrough line.
    [LoggerMessage(EventId = 75, Level = LogLevel.Error,
        Message = "MIGRATE_ALLOW_SCHEMA_AHEAD is absent from this container's environment entirely —\n"
                + "the compose file running this stack predates #1236 and does not forward the key,\n"
                + "so exit (3) CANNOT work yet: setting the value in deploy/.env changes nothing until\n"
                + "`git pull` in /opt/jobbliggaren brings the passthrough line. Exits (1) and (2) work now.")]
    public static partial void OverrideKeyNotForwarded(ILogger logger);

    [LoggerMessage(EventId = 200, Level = LogLevel.Information,
        Message = "Mode: init (Phase A-C — idempotent DDL mot operatör-givna creds)")]
    public static partial void ModeInit(ILogger logger);

    [LoggerMessage(EventId = 201, Level = LogLevel.Information,
        Message = "Mode: schema (Phase E — EF Core MigrateAsync)")]
    public static partial void ModeSchema(ILogger logger);

    [LoggerMessage(EventId = 202, Level = LogLevel.Error,
        Message = "Usage: Jobbliggaren.Migrate <init|bootstrap|ensure-extensions|schema|explain-search|rewrap-master-key>")]
    public static partial void UsageError(ILogger logger);

    // #198 gate M-3 — master-key rotation. Offline: api and worker must be stopped, because a
    // concurrent first-use would insert a row under the retiring identity behind the scan.
    [LoggerMessage(EventId = 210, Level = LogLevel.Information,
        Message = "Mode: rewrap-master-key (offline master-key rotation — #198, M-3). " +
                  "api and worker MUST be stopped; see docs/runbooks/master-key-ops.md §4")]
    public static partial void ModeRewrapMasterKey(ILogger logger);

    [LoggerMessage(EventId = 211, Level = LogLevel.Information,
        Message = "Re-wrapping DEKs from cmk_key_id {RetiringKeyId} to {IncomingKeyId}")]
    public static partial void RewrapStart(ILogger logger, string retiringKeyId, string incomingKeyId);

    // The exit-0-with-zero-rows path IS the idempotency proof M-3 asks for, so it gets its own
    // line rather than being folded into the summary.
    [LoggerMessage(EventId = 212, Level = LogLevel.Information,
        Message = "Nothing to re-wrap: 0 row(s) carry {RetiringKeyId}, {AlreadyCurrent} already " +
                  "carry {IncomingKeyId}. This run is a no-op (idempotent), and {Verified} " +
                  "row(s) were verified to unwrap under the incoming key.")]
    public static partial void RewrapNoOp(
        ILogger logger, string retiringKeyId, int alreadyCurrent, string incomingKeyId, int verified);

    [LoggerMessage(EventId = 213, Level = LogLevel.Information,
        Message = "Re-wrap COMPLETE — {Rewrapped} row(s) re-wrapped, {AlreadyCurrent} already " +
                  "current, {Verified} row(s) verified to unwrap under the new key")]
    public static partial void RewrapComplete(
        ILogger logger, int rewrapped, int alreadyCurrent, int verified);

    // F6 P4 (2026-05-20) — ensure-extensions-mode (master-creds, PG extensions)
    [LoggerMessage(EventId = 204, Level = LogLevel.Information,
        Message = "Mode: ensure-extensions (master-creds, CREATE EXTENSION IF NOT EXISTS)")]
    public static partial void ModeEnsureExtensions(ILogger logger);

    [LoggerMessage(EventId = 80, Level = LogLevel.Information,
        Message = "EnsureExtensions: säkerställer PostgreSQL extensions (idempotent, master-roll)")]
    public static partial void EnsureExtensionsStart(ILogger logger);

    [LoggerMessage(EventId = 81, Level = LogLevel.Information,
        Message = "EnsureExtensions: complete (idempotent — no-op om extensions redan finns)")]
    public static partial void EnsureExtensionsComplete(ILogger logger);

    // F6 P4 (2026-05-21) — explain-search diagnostik-mode (sök-perf-rotorsak)
    [LoggerMessage(EventId = 205, Level = LogLevel.Information,
        Message = "Mode: explain-search (EXPLAIN ANALYZE på q-search-filter — sök-perf-diagnos)")]
    public static partial void ModeExplainSearch(ILogger logger);

    [LoggerMessage(EventId = 82, Level = LogLevel.Information,
        Message = "EXPLAIN ANALYZE [term={Term}, variant={Variant}]:\n{Plan}")]
    public static partial void ExplainSearchResult(ILogger logger, string term, string variant, string plan);

    // ADR 0034 — Bootstrap-mode (Identity-context via master-creds)
    [LoggerMessage(EventId = 203, Level = LogLevel.Information,
        Message = "Mode: bootstrap (Identity-context via master-creds — ADR 0034)")]
    public static partial void ModeBootstrap(ILogger logger);

    [LoggerMessage(EventId = 70, Level = LogLevel.Information,
        Message = "Bootstrap Step 1: CREATE SCHEMA identity + GRANTs på identity till jobbliggaren_app")]
    public static partial void BootstrapStep1Start(ILogger logger);

    [LoggerMessage(EventId = 71, Level = LogLevel.Information,
        Message = "Bootstrap Step 2: EF Core Database.MigrateAsync mot AppIdentityDbContext (master-creds)")]
    public static partial void BootstrapStep2Start(ILogger logger);

    [LoggerMessage(EventId = 72, Level = LogLevel.Information,
        Message = "Bootstrap Step 2 COMPLETE — applied {Count} Identity-migration(s)")]
    public static partial void BootstrapStep2Complete(ILogger logger, int count);

    [LoggerMessage(EventId = 73, Level = LogLevel.Information,
        Message = "Bootstrap Step 2: no pending Identity-migrations")]
    public static partial void BootstrapStep2NoPending(ILogger logger);

    [LoggerMessage(EventId = 74, Level = LogLevel.Information,
        Message = "Bootstrap COMPLETE — identity-schema klar + Identity-migrations applied")]
    public static partial void BootstrapComplete(ILogger logger);
}
