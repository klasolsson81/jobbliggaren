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

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
