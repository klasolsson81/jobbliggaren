using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Persistence;

// ADR 0080 Vag 4 PR-1 — persistence round-trip for UserJobAdMatch + the JobSeeker consent/
// watermark surface against a REAL Postgres (Testcontainers, the fixture applies the new
// 20260624021830_AddUserJobAdMatchAndConsentWatermarks migration). This is the converter
// proof InMemory CANNOT give (repo lesson denormalized_projection_plaintext_dek_free / the
// exp-per-occ jsonb-round-trip Blocker): InMemory hides the strongly-typed id converter,
// the JobAdId-as-uuid converter, the Grade/NotificationStatus HasConversion<string>() enum-
// NAME columns, the matched_skill_concept_ids jsonb backing-field converter, and the
// UNIQUE(user_id, job_ad_id) constraint — every one of those is only honoured by the
// relational provider. (#868 retired the soft-delete axis: no DeletedAt column, no query filter.)
//
// Each test uses fresh random UserId/JobAdId so rows do not collide across the shared
// [Collection("Api")] table.
[Collection("Api")]
public sealed class UserJobAdMatchPersistenceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // Postgres timestamptz stores MICROSECOND precision; .NET DateTimeOffset carries 100-ns
    // ticks. The registered DateTimeProvider returns DateTimeOffset.UtcNow (sub-microsecond
    // ticks), so an exact equality on a captured "now" loses the truncated remainder on the
    // round-trip. This is the documented Postgres resolution, NOT a converter defect — assert
    // with a tolerance comfortably above the truncation and below any real-time drift.
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromMilliseconds(1);

    private (AppDbContext Db, IDateTimeProvider Clock, IServiceScope Scope) NewScope()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        return (db, clock, scope);
    }

    // ---------------------------------------------------------------
    // 1. Round-trip — every converter survives a real Postgres round-trip
    // ---------------------------------------------------------------

    [Fact]
    public async Task UserJobAdMatch_RoundTrips_AllConvertedColumns()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var jobAdId = JobAdId.New();
        UserJobAdMatchId matchId;
        DateTimeOffset createdAt;

        // Write scope.
        {
            var (db, clock, scope) = NewScope();
            using (scope)
            {
                var match = UserJobAdMatch.Create(
                    userId, jobAdId, NotifiableMatchGrade.Strong,
                    ["csharp", "sql", "postgresql"], clock).Value;
                matchId = match.Id;
                createdAt = match.CreatedAt;
                db.UserJobAdMatches.Add(match);
                await db.SaveChangesAsync(ct);
            }
        }

        // FRESH scope — proves the values came back from the DB, not the change-tracker.
        {
            var (db, _, scope) = NewScope();
            using (scope)
            {
                var reloaded = await db.UserJobAdMatches
                    .AsNoTracking()
                    .SingleAsync(m => m.Id == matchId, ct);

                reloaded.UserId.ShouldBe(userId);
                reloaded.JobAdId.ShouldBe(jobAdId);
                reloaded.Grade.ShouldBe(NotifiableMatchGrade.Strong);
                reloaded.NotificationStatus.ShouldBe(NotificationStatus.Pending);
                // The jsonb list round-trips element-for-element, ordinal order preserved.
                reloaded.MatchedSkillConceptIds.ShouldBe(["csharp", "sql", "postgresql"]);
                reloaded.CreatedAt.ShouldBe(createdAt, TimestampTolerance);
                reloaded.SentAt.ShouldBeNull();
            }
        }
    }

    [Fact]
    public async Task UserJobAdMatch_GradeAndStatus_PersistAsEnumNames_NotOrdinals()
    {
        // HasConversion<string>() proof: the on-disk columns hold the enum NAMES
        // ("Top"/"Pending"), not "2"/"0". Read the raw text columns to assert this — an
        // ordinal-stored enum would silently break the reorder-safety the names buy us.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var jobAdId = JobAdId.New();
        UserJobAdMatchId matchId;

        var (db, clock, scope) = NewScope();
        using (scope)
        {
            var match = UserJobAdMatch.Create(
                userId, jobAdId, NotifiableMatchGrade.Top, ["csharp"], clock).Value;
            matchId = match.Id;
            db.UserJobAdMatches.Add(match);
            await db.SaveChangesAsync(ct);

            // Read the raw text columns (parity with SearchCriteriaJsonbBackcompatTests'
            // NpgsqlConnection approach — proves the on-disk form, not just what EF reads back).
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await conn.OpenAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "SELECT grade, notification_status FROM user_job_ad_matches WHERE id = @id";
                cmd.Parameters.AddWithValue("id", matchId.Value);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                (await reader.ReadAsync(ct)).ShouldBeTrue();

                reader.GetString(0).ShouldBe("Top");      // grade — enum NAME, not "2"
                reader.GetString(1).ShouldBe("Pending");  // notification_status — name, not "0"
            }
            finally
            {
                await conn.CloseAsync();
            }
        }
    }

    // ---------------------------------------------------------------
    // 2. State transition persists — MarkQueued → MarkSent survives a round-trip
    // ---------------------------------------------------------------

    [Fact]
    public async Task UserJobAdMatch_SentState_PersistsWithSentAt()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var jobAdId = JobAdId.New();
        UserJobAdMatchId matchId;
        DateTimeOffset sentAt;

        var (db, clock, scope) = NewScope();
        using (scope)
        {
            var match = UserJobAdMatch.Create(
                userId, jobAdId, NotifiableMatchGrade.Strong, ["csharp"], clock).Value;
            matchId = match.Id;
            db.UserJobAdMatches.Add(match);
            await db.SaveChangesAsync(ct);

            match.MarkQueued().IsSuccess.ShouldBeTrue();
            match.MarkSent(clock).IsSuccess.ShouldBeTrue();
            sentAt = match.SentAt!.Value;
            await db.SaveChangesAsync(ct);
        }

        var (readDb, _, readScope) = NewScope();
        using (readScope)
        {
            var reloaded = await readDb.UserJobAdMatches
                .AsNoTracking()
                .SingleAsync(m => m.Id == matchId, ct);

            reloaded.NotificationStatus.ShouldBe(NotificationStatus.Sent);
            reloaded.SentAt.ShouldNotBeNull();
            reloaded.SentAt!.Value.ShouldBe(sentAt, TimestampTolerance);
        }
    }

    // ---------------------------------------------------------------
    // 3. UNIQUE(user_id, job_ad_id) dedup spine — second insert throws at the DB level
    // ---------------------------------------------------------------

    [Fact]
    public async Task UserJobAdMatch_DuplicateUserAndJobAd_ThrowsDbUpdateException()
    {
        // The idempotency spine: a re-scan that tries to insert a SECOND row for the SAME
        // (UserId, JobAdId) must be rejected by the UNIQUE constraint — this is what lets the
        // Worker scan be re-runnable without re-notifying. Proven at the DB level (InMemory
        // would silently accept the duplicate).
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var jobAdId = JobAdId.New();

        // First insert — own scope (clean change-tracker).
        var (db1, clock1, scope1) = NewScope();
        using (scope1)
        {
            var first = UserJobAdMatch.Create(
                userId, jobAdId, NotifiableMatchGrade.Good, ["csharp"], clock1).Value;
            db1.UserJobAdMatches.Add(first);
            await db1.SaveChangesAsync(ct);
        }

        // Second insert with the SAME (UserId, JobAdId) — own scope so the failed
        // SaveChanges does not poison the first context.
        var (db2, clock2, scope2) = NewScope();
        using (scope2)
        {
            var duplicate = UserJobAdMatch.Create(
                userId, jobAdId, NotifiableMatchGrade.Top, ["sql"], clock2).Value;
            db2.UserJobAdMatches.Add(duplicate);

            await Should.ThrowAsync<DbUpdateException>(async () =>
                await db2.SaveChangesAsync(ct));
        }
    }

    // ---------------------------------------------------------------
    // 4. Schema pins (#868) — the deleted_at axis is physically gone, and the drop took no index
    // ---------------------------------------------------------------

    [Fact]
    public async Task DeletedAtColumn_IsPhysicallyGone_FromTheMatchesTable()
    {
        // #868 retired the writerless soft-delete axis (migration RetireMatchAndHitDeletedAtAxis).
        // This pin guards the SNAPSHOT → PHYSICAL DATABASE link, the one thing model==snapshot cannot:
        // EF's PendingModelChangesWarning fires on model ≠ snapshot, but a hand-written Up() that
        // updates the snapshot while dropping the wrong thing (or nothing) satisfies EF completely —
        // only a read of information_schema after the migration has run closes that gap. No "the filter
        // is gone, so ordinary reads see every row" test lives here, deliberately (the #915 lesson): a
        // freshly seeded row passes any filter that does not exclude fresh rows, so no seed-and-read
        // shape can prove "no filter exists". That property is guarded at build time by the EF model —
        // re-adding HasQueryFilter flips this aggregate into AccountHardDeleteCascadeFitnessTests'
        // filtered set, which then demands the matching IgnoreQueryFilters in the Art. 17 cascade.
        var ct = TestContext.Current.CancellationToken;
        var (db, _, scope) = NewScope();
        using (scope)
        {
            (await ColumnExistsAsync(db, "deleted_at", ct)).ShouldBeFalse(
                "deleted_at ska vara fysiskt borta ur user_job_ad_matches — en writerless decoy (#868)");

            // Self-proving positive: the same probe finds a column that IS there, so the assertion
            // above cannot pass vacuously (a typo'd table / changed information_schema shape).
            (await ColumnExistsAsync(db, "grade", ct)).ShouldBeTrue(
                "kontroll-probe: helpern måste kunna SE en kolumn som finns, annars bevisar raden inget");
        }
    }

    [Theory]
    [InlineData("ux_user_job_ad_matches_user_jobad")]
    [InlineData("ix_user_job_ad_matches_user_created_at")]
    [InlineData("ix_user_job_ad_matches_user_status")]
    [InlineData("ix_user_job_ad_matches_user_id")]
    public async Task Index_SurvivesTheDeletedAtDrop_AndIsNotPartial(string indexName)
    {
        // DROP COLUMN silently drops every index whose predicate names the column, and the EF model
        // snapshot is blind to it (#821's hard-won lesson). None of these four names deleted_at — all
        // are plain B-trees — so the drop must take none of them; ix_..._user_id in particular serves
        // the Art. 17 erasure sweep. Non-partial is structural now: with the column gone there is no
        // predicate left to make one partial. The assertion stays as the tripwire against re-adding one.
        var ct = TestContext.Current.CancellationToken;
        var (db, _, scope) = NewScope();
        using (scope)
        {
            var indexDef = await IndexDefAsync(db, indexName, ct);
            indexDef.ShouldNotBeNull($"{indexName} måste finnas i schemat efter deleted_at-droppen");
            indexDef!.ShouldNotContain("WHERE",
                customMessage: $"{indexName} får INTE vara partiellt — det finns inget deleted_at-predikat kvar");
        }
    }

    // ---------------------------------------------------------------
    // 5. JobSeeker consent (jsonb) + watermark columns — full round-trip
    // ---------------------------------------------------------------

    [Fact]
    public async Task JobSeeker_ConsentAndWatermarks_RoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        JobSeekerId seekerId;
        DateTimeOffset consentAt;
        DateTimeOffset scannedThrough;
        DateTimeOffset seenAt;

        var (db, clock, scope) = NewScope();
        using (scope)
        {
            var seeker = JobSeeker.Register(Guid.NewGuid(), "Consent User", clock).Value;
            seekerId = seeker.Id;

            // Consent fields live in the preferences jsonb (additive — no migration).
            seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Daily, clock);
            consentAt = seeker.Preferences.NotificationConsentAt!.Value;

            // Watermarks are first-class columns (last_match_scan_at / last_seen_matches_at).
            // A scan watermark is "how far the Worker scanned" — always <= now (the scan
            // window end). AdvanceMatchScan clamps a future value to now (defense-in-depth),
            // so the round-trip uses a realistic past timestamp.
            scannedThrough = clock.UtcNow.AddMinutes(-15);
            seeker.AdvanceMatchScan(scannedThrough, clock);
            seeker.SetLastSeenMatches(clock.UtcNow, clock);
            seenAt = seeker.LastSeenMatchesAt!.Value;

            db.JobSeekers.Add(seeker);
            await db.SaveChangesAsync(ct);
        }

        var (readDb, _, readScope) = NewScope();
        using (readScope)
        {
            var reloaded = await readDb.JobSeekers
                .AsNoTracking()
                .SingleAsync(s => s.Id == seekerId, ct);

            // jsonb consent fields.
            reloaded.Preferences.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
            reloaded.Preferences.DigestCadence.ShouldBe(DigestCadence.Daily);
            reloaded.Preferences.NotificationConsentAt.ShouldNotBeNull();
            reloaded.Preferences.NotificationConsentAt!.Value.ShouldBe(consentAt, TimestampTolerance);
            reloaded.Preferences.NotificationConsentWithdrawnAt.ShouldBeNull();

            // First-class watermark columns.
            reloaded.LastMatchScanAt.ShouldNotBeNull();
            reloaded.LastMatchScanAt!.Value.ShouldBe(scannedThrough, TimestampTolerance);
            reloaded.LastSeenMatchesAt.ShouldNotBeNull();
            reloaded.LastSeenMatchesAt!.Value.ShouldBe(seenAt, TimestampTolerance);
        }
    }

    // Physical column existence, straight from the catalog. Column name PARAMETERISED (a value in the
    // WHERE clause, never an identifier spliced into SQL). Precedent: CompanyWatchCriterionPersistenceTests.
    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string column, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<int>(
                """
                SELECT count(*)::int AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'user_job_ad_matches' AND column_name = {0}
                """,
                column)
            .ToListAsync(ct);
        return rows.ShouldHaveSingleItem() > 0;
    }

    private static async Task<string?> IndexDefAsync(AppDbContext db, string indexName, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT indexdef AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'public' AND indexname = {0}
                """,
                indexName)
            .ToListAsync(ct);
        return rows.SingleOrDefault();
    }
}
