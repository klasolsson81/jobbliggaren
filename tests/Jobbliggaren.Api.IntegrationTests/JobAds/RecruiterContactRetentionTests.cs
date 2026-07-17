using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Commands.ArchiveExternalJobAd;
using Jobbliggaren.Application.JobAds.Jobs.ExpireJobAds;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds.SnapshotMisses;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #842 Tier A retention fitness (re-bind R4, b1 §4.2) — the invariant <b>no non-Active ad holds a
/// contact</b>, proven against real Postgres for EACH of the THREE archival writers that must clear
/// <c>job_ads.contacts</c>. Three writers on one rule is a rule that needs a test, not a convention:
/// two of them (<see cref="ExpireJobAdsJob"/>, <see cref="JobAdSnapshotMissTracker"/>) bypass the
/// aggregate via <c>ExecuteUpdateAsync</c>, so <c>Archive()</c>'s contact clear must be repeated in
/// their bulk <c>SetProperty</c> — a repeat nothing but this test binds together.
/// </summary>
/// <remarks>
/// Each writer is seeded with a COUNTERFACTUAL: an Active ad that PROVABLY holds a contact first
/// (asserted non-null), because <c>SELECT count(*) WHERE status &lt;&gt; 'Active' AND contacts IS
/// NOT NULL == 0</c> is satisfied vacuously by an empty table — an absence proves a gate only
/// against a prior presence. Every ad is built through <see cref="JobAd.Import"/> (the production
/// funnel endpoint, V20/#843), never a hand-seeded column.
/// <para>
/// #864 / #842 writer-durability siblings (CTO 2026-07-17 R2/R3): the same two bulk writers must
/// also never RESURRECT a GDPR Art. 17 <see cref="JobAdStatus.Erased"/> tombstone. Each
/// resurrection witness seeds a real tombstone through the production funnel
/// (<see cref="JobAd.Import"/> then <see cref="JobAd.Erase"/> — never a hand-stamped status),
/// asymmetrically: <see cref="JobAd.Erase"/> touches neither <c>ExpiresAt</c> nor the
/// <c>External</c> key, so the ONLY thing excluding the tombstone from each writer's bulk
/// selection is its <c>Status == Active</c> allow-list. The resurrection mutant
/// (<c>== Active</c> → <c>!= Archived</c>) re-stamps the tombstone Archived and the read-back goes
/// RED — a deny-list is worse than a leak, because a re-stamped tombstone bypasses the aggregate's
/// <c>Archive()</c> guard and un-keys <c>UpdateFromSource</c>'s re-import refusal. The tracker
/// witness seeds 2+1 (tombstone + qualifying Active companion): its count oracle then also kills a
/// no-op'd writer (<c>archived == 0</c> → RED) instead of leaning on the retention sibling.
/// </para>
/// </remarks>
public sealed class RecruiterContactRetentionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private ServiceProvider _provider = default!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(_postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // The fixed instant every writer measures against. ExpiresAt below sits well before it.
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    // ================================================================================
    // Writer 1 — JobAd.Archive via its command path (ArchiveExternalJobAdCommand).
    // ================================================================================

    [Fact]
    public async Task Archive_via_the_command_path_clears_contacts_and_leaves_no_non_Active_ad_holding_one()
    {
        var ct = TestContext.Current.CancellationToken;
        const string externalId = "retention-archive-1";
        await SeedActiveAdWithContactsAsync(externalId, ct);

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var handler = new ArchiveExternalJobAdCommandHandler(
                db, new FixedClock(Now), NullLogger<ArchiveExternalJobAdCommandHandler>.Instance);

            var result = await handler.Handle(
                new ArchiveExternalJobAdCommand(JobSource.Platsbanken, externalId), ct);
            result.IsSuccess.ShouldBeTrue();
            result.Value.ShouldBe(ArchiveOutcome.Archived);
            await db.SaveChangesAsync(ct);
        }

        await AssertNoNonActiveAdHoldsAContactAsync(ct);
        await AssertAdIsArchivedWithoutContactsAsync(externalId, ct);
    }

    // ================================================================================
    // Writer 2 — ExpireJobAdsJob (bulk ExecuteUpdateAsync; the SetProperty(Contacts → null) rider).
    // ================================================================================

    [Fact]
    public async Task ExpireJobAdsJob_clears_contacts_and_leaves_no_non_Active_ad_holding_one()
    {
        var ct = TestContext.Current.CancellationToken;
        const string externalId = "retention-expire-1";
        // ExpiresAt in the past relative to Now → the expiry sweep archives it.
        await SeedActiveAdWithContactsAsync(
            externalId, ct, expiresAt: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = new ExpireJobAdsJob(
                db, new FixedClock(Now), Substitute.For<ISystemEventAuditor>(),
                NullLogger<ExpireJobAdsJob>.Instance);
            await job.RunAsync(ct);
        }

        await AssertNoNonActiveAdHoldsAContactAsync(ct);
        await AssertAdIsArchivedWithoutContactsAsync(externalId, ct);
    }

    // ================================================================================
    // Writer 3 — JobAdSnapshotMissTracker.ArchiveJobAdsWithMissCountAtLeastAsync (the primary
    // archival path; bulk ExecuteUpdateAsync with the same SetProperty(Contacts → null) rider).
    // ================================================================================

    [Fact]
    public async Task JobAdSnapshotMissTracker_archive_clears_contacts_and_leaves_no_non_Active_ad_holding_one()
    {
        var ct = TestContext.Current.CancellationToken;
        const string externalId = "retention-miss-1";
        await SeedActiveAdWithContactsAsync(externalId, ct);

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracker = new JobAdSnapshotMissTracker(
                db, NullLogger<JobAdSnapshotMissTracker>.Instance);

            // The ad is absent from an (empty) complete snapshot → miss_count ticks to 1.
            await tracker.ApplyAsync(
                JobSource.Platsbanken, new HashSet<string>(StringComparer.Ordinal), Now, ct);

            var archived = await tracker.ArchiveJobAdsWithMissCountAtLeastAsync(
                JobSource.Platsbanken, threshold: 1, Now, ct);
            archived.ShouldBe(1, "the missed ad is archived by the primary retention writer.");
        }

        await AssertNoNonActiveAdHoldsAContactAsync(ct);
        await AssertAdIsArchivedWithoutContactsAsync(externalId, ct);
    }

    // ================================================================================
    // #864 / #842 writer-durability (CTO 2026-07-17 R2/R3) — the two BULK writers must not
    // RESURRECT a GDPR Art. 17 Erased tombstone. Asymmetric seed via the production funnel
    // (Import + Erase()): Erase() touches neither ExpiresAt nor the External key, so the SOLE
    // excluder from each bulk selection is the Status == Active allow-list. The resurrection
    // mutant (== Active → != Archived) re-stamps the tombstone Archived — RED here.
    // ================================================================================

    [Fact]
    public async Task ExpireJobAdsJob_does_not_resurrect_an_expired_Erased_tombstone()
    {
        var ct = TestContext.Current.CancellationToken;
        const string externalId = "resurrection-expire-1";

        // A real Erased tombstone whose ExpiresAt is in the past. Erase() does not touch ExpiresAt,
        // so the ONLY thing keeping this row out of the expiry sweep is the Status == Active gate.
        await SeedActiveAdWithContactsAsync(
            externalId, ct, expiresAt: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        await EraseAsync(externalId, ct);
        await AssertExpiredRelativeToNowAsync(externalId, ct);

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = new ExpireJobAdsJob(
                db, new FixedClock(Now), Substitute.For<ISystemEventAuditor>(),
                NullLogger<ExpireJobAdsJob>.Instance);
            await job.RunAsync(ct);
        }

        // The tombstone stays a tombstone. Under the resurrection mutant (== Active → != Archived)
        // the expiry sweep selects it and re-stamps it Archived, and this read-back goes RED.
        await AssertAdIsErasedTombstoneAsync(externalId, ct);
    }

    [Fact]
    public async Task JobAdSnapshotMissTracker_archive_does_not_resurrect_an_Erased_tombstone()
    {
        var ct = TestContext.Current.CancellationToken;
        const string externalId = "resurrection-miss-1";
        const string companionId = "resurrection-miss-companion-1";

        // Asymmetric 2+1 seed: the tombstone-to-be AND a qualifying Active companion, both missed
        // in the same complete snapshot (ApplyAsync ticks only Active ads), THEN one is erased.
        // The companion is what makes the count oracle below asymmetric — a count-only zero is
        // satisfied by a writer that did nothing at all, so the witness would otherwise lean on
        // its retention sibling to catch a no-op'd writer. Erase() leaves the External key intact,
        // so the tombstone's miss row still joins the archival select — the ONLY excluder left is
        // the Status == Active gate.
        await SeedActiveAdWithContactsAsync(externalId, ct);
        await SeedActiveAdWithContactsAsync(companionId, ct);
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracker = new JobAdSnapshotMissTracker(
                db, NullLogger<JobAdSnapshotMissTracker>.Instance);
            await tracker.ApplyAsync(
                JobSource.Platsbanken, new HashSet<string>(StringComparer.Ordinal), Now, ct);
        }
        await EraseAsync(externalId, ct);
        await AssertMissCountAtLeastAsync(externalId, threshold: 1, ct);
        await AssertMissCountAtLeastAsync(companionId, threshold: 1, ct);

        int archived;
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracker = new JobAdSnapshotMissTracker(
                db, NullLogger<JobAdSnapshotMissTracker>.Instance);
            archived = await tracker.ArchiveJobAdsWithMissCountAtLeastAsync(
                JobSource.Platsbanken, threshold: 1, Now, ct);
        }

        // Exactly the companion: archiving it proves the writer RAN (a no-op'd writer yields 0 —
        // RED), and the tombstone's exclusion is then attributable to the Status gate alone. Under
        // the resurrection mutant (== Active → != Archived) the tombstone is selected too →
        // archived == 2 and its status flips to Archived, so the count and the tombstone
        // read-back both go RED.
        archived.ShouldBe(1,
            "the primary archival writer archives the qualifying Active companion and must not "
            + "select the Erased tombstone — it is not Active.");
        await AssertAdIsArchivedWithoutContactsAsync(companionId, ct);
        await AssertAdIsErasedTombstoneAsync(externalId, ct);
    }

    // ================================================================================
    // Helpers
    // ================================================================================

    private async Task SeedActiveAdWithContactsAsync(
        string externalId, CancellationToken ct, DateTimeOffset? expiresAt = null)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        var company = Company.Create("Rekrytering AB").Value;
        var external = ExternalReference.Create(JobSource.Platsbanken, externalId).Value;
        var declared = AdContact.TryCreate(
            "Astrid Wallander", "Rekryterare", "astrid.wallander@rekrytering.example",
            "070-333 44 55", AdContactOrigin.Declared);
        declared.ShouldNotBeNull();

        var jobAd = JobAd.Import(
            "Backend-utvecklare", company, "Neutral annonstext utan kontaktspår.",
            "https://arbetsformedlingen.se/platsbanken/annonser/" + externalId,
            external, "{\"id\":\"" + externalId + "\"}", TestFacets.None,
            [declared!],
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            expiresAt, clock).Value;

        // Counterfactual: the Active ad HELD a contact — Import populated it while Active.
        jobAd.Contacts.ShouldNotBeNull("the Active ad must hold a contact before archival.");
        jobAd.Contacts!.IsEmpty.ShouldBeFalse();

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    private async Task AssertNoNonActiveAdHoldsAContactAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var offenders = (await db.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                FROM job_ads
                WHERE status <> 'Active' AND contacts IS NOT NULL
                """)
            .ToListAsync(ct)).Single();

        offenders.ShouldBe(0,
            "the retention invariant: no non-Active ad may hold a contact. A writer that archives "
            + "without clearing contacts breaks this — which is why all three carry the same clear.");
    }

    private async Task AssertAdIsArchivedWithoutContactsAsync(string externalId, CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var status = (await db.Database
            .SqlQueryRaw<string>(
                "SELECT status AS \"Value\" FROM job_ads WHERE external_id = {0}", externalId)
            .ToListAsync(ct)).Single();
        var contacts = (await db.Database
            .SqlQueryRaw<string?>(
                "SELECT contacts::text AS \"Value\" FROM job_ads WHERE external_id = {0}", externalId)
            .ToListAsync(ct)).Single();

        status.ShouldNotBe("Active", "the ad was archived by the writer under test.");
        contacts.ShouldBeNull("and its contacts column is cleared.");
    }

    // Runs the production Art. 17 funnel (JobAd.Erase) over an already-seeded Active ad, then
    // proves through a FRESH context that the row IS a persisted Erased tombstone — so a silently
    // no-op'd Erase() cannot make a resurrection witness pass vacuously (the #864 broken-seed
    // lesson: an inclusion assertion cannot detect its own broken seed).
    private async Task EraseAsync(string externalId, CancellationToken ct)
    {
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jobAd = await db.JobAds.SingleAsync(j => j.External!.ExternalId == externalId, ct);

            // Never a hand-stamped Status: Erase() clears the PII fields but leaves External and
            // ExpiresAt, which is exactly what makes each resurrection seed asymmetric.
            jobAd.Erase(new FixedClock(Now)).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(ct);
        }

        await AssertAdIsErasedTombstoneAsync(externalId, ct);
    }

    private async Task AssertAdIsErasedTombstoneAsync(string externalId, CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var status = (await db.Database
            .SqlQueryRaw<string>(
                "SELECT status AS \"Value\" FROM job_ads WHERE external_id = {0}", externalId)
            .ToListAsync(ct)).Single();

        status.ShouldBe("Erased",
            "the ad is a GDPR Art. 17 tombstone; a bulk archival writer that re-stamps it Archived "
            + "resurrects it — Archive()'s guard is bypassed and UpdateFromSource's re-import refusal "
            + "keys on Status == Erased, so the erased ad would walk back in on the next sync.");
    }

    private async Task AssertExpiredRelativeToNowAsync(string externalId, CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Counterfactual: the tombstone WOULD be swept if it were Active — a past ExpiresAt is
        // present, so only the Status gate excludes it. Without this a null/future ExpiresAt would
        // exclude the row by a DIFFERENT predicate and the mutant could pass for the wrong reason.
        var count = (await db.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM job_ads "
                + "WHERE external_id = {0} AND expires_at IS NOT NULL AND expires_at < {1}",
                externalId, Now)
            .ToListAsync(ct)).Single();

        count.ShouldBe(1, "the tombstone keeps a past ExpiresAt (Erase() does not touch it).");
    }

    private async Task AssertMissCountAtLeastAsync(string externalId, int threshold, CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Counterfactual: a qualifying miss row joins the archival select, so only the Status gate
        // keeps the tombstone out. Without it the mutant would pass for the wrong reason. The
        // source predicate mirrors the archival join's (source, external_id) key exactly.
        var missCount = (await db.Database
            .SqlQueryRaw<int>(
                "SELECT miss_count AS \"Value\" FROM job_ad_snapshot_misses "
                + "WHERE source = {0} AND external_id = {1}",
                JobSource.Platsbanken.Value, externalId)
            .ToListAsync(ct)).Single();

        missCount.ShouldBeGreaterThanOrEqualTo(threshold,
            "the miss row must join the archival select for the Status gate to be the sole excluder.");
    }

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
