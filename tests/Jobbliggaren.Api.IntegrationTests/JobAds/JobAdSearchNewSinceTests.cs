using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #312 (ADR 0115) — <see cref="JobAdSearchQuery.CountNewSinceAsync"/> against real Postgres
/// (Testcontainers, NEVER EF-InMemory: the value-converted <c>Status == Active</c> equality +
/// facet <c>IN(...)</c> + the <c>CreatedAt &gt; since</c> window are Npgsql-only). The method's
/// NEW behaviour over the already-pinned <see cref="JobAdSearchComposition.ApplyFilter"/> SPOT is
/// exactly the <c>CreatedAt &gt; since</c> window; the SPOT's <c>Status=Active</c> allow-list and
/// synonym-expansion are INHERITED and pinned elsewhere (JobAdSearchLifecycleOracleTests /
/// ListJobAdsFtsTests) — so this class pins the window boundary and RE-confirms Active-only within
/// it (the #864-immunity the CTO's live-count model claims by construction).
/// <para>
/// created_at is stamped via raw SQL after seeding (the DI clock is not per-seed controllable), so
/// the window boundary is deterministic. Run-isolation over the shared [Collection("Api")] Postgres:
/// a unique per-run occupation-group, ANDed into every criterion — a sibling run's ads never count.
/// </para>
/// </summary>
[Collection("Api")]
public class JobAdSearchNewSinceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);

    private static JobAdSearchQuery CreateSut(IServiceScope scope) =>
        new(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            Substitute.For<IOccupationSynonymExpander>());

    private static JobAdFilterCriteria CriteriaFor(string occupationGroup) => new(
        OccupationGroup: [occupationGroup],
        Municipality: [],
        Region: [],
        EmploymentType: [],
        WorktimeExtent: [],
        Employer: [],
        Remote: false,
        Q: null);

    // Seeds one imported ad tagged with the run's occupation-group; lifecycle transitions run
    // through the REAL domain methods (never a fabricated column stamp, #843/#864 AC 4).
    private async Task<JobAdId> SeedAsync(
        string title, string occupationGroup, CancellationToken ct, bool archived = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var externalId = $"newsince-{Guid.NewGuid():N}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\","
            + $"\"occupation_group\":{{\"concept_id\":\"{occupationGroup}\"}}}}";

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: []).Value;

        if (archived)
            jobAd.Archive(clock).IsSuccess.ShouldBeTrue("Archive-seeden får inte tyst misslyckas");

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // Stamps created_at deterministically (the ingest-time axis the window filters on).
    private async Task SetCreatedAtAsync(JobAdId id, DateTimeOffset createdAt, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE job_ads SET created_at = {createdAt} WHERE id = {id.Value}", ct);
    }

    private async Task<int> CountNewSinceAsync(
        JobAdFilterCriteria criteria, DateTimeOffset since, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        return await CreateSut(scope).CountNewSinceAsync(criteria, since, ct);
    }

    // Seeds old(T0) + mid(T1) + recent(T2) Active ads + a recentArchived(T2) tombstone, all on `grp`.
    private async Task SeedWindowFixtureAsync(string grp, CancellationToken ct)
    {
        var old = await SeedAsync("Gammal aktiv", grp, ct);
        var mid = await SeedAsync("Mellan aktiv", grp, ct);
        var recent = await SeedAsync("Ny aktiv", grp, ct);
        var recentArchived = await SeedAsync("Ny arkiverad", grp, ct, archived: true);
        await SetCreatedAtAsync(old, T0, ct);
        await SetCreatedAtAsync(mid, T1, ct);
        await SetCreatedAtAsync(recent, T2, ct);
        await SetCreatedAtAsync(recentArchived, T2, ct);   // newest, but Archived
    }

    [Fact]
    public async Task CountNewSinceAsync_CountsOnlyActiveAdsCreatedStrictlyAfterSince()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = $"grp{Guid.NewGuid():N}"[..16];
        await SeedWindowFixtureAsync(grp, ct);

        // since = T1 → Active ads with created_at > T1: {recent (T2)}. mid (T1) excluded (strict >),
        // old (T0) excluded, recentArchived (T2 but Archived) excluded (#864-immune by construction).
        var count = await CountNewSinceAsync(CriteriaFor(grp), since: T1, ct);

        count.ShouldBe(1);
    }

    [Fact]
    public async Task CountNewSinceAsync_SinceBeforeAll_CountsEveryActiveAd_ArchivedExcluded()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = $"grp{Guid.NewGuid():N}"[..16];
        await SeedWindowFixtureAsync(grp, ct);

        // Window opens before every ad → all three Active ads, the Archived tombstone excluded.
        // A deny-list (!= Archived) or a dropped Active gate would return 4; a broken window, 0.
        var count = await CountNewSinceAsync(CriteriaFor(grp), since: T0.AddYears(-1), ct);

        count.ShouldBe(3);
    }

    [Fact]
    public async Task CountNewSinceAsync_SinceAfterAll_CountsZero()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = $"grp{Guid.NewGuid():N}"[..16];
        await SeedWindowFixtureAsync(grp, ct);

        // Window opens after every ad's created_at → nothing is "new".
        var count = await CountNewSinceAsync(CriteriaFor(grp), since: T2.AddYears(1), ct);

        count.ShouldBe(0);
    }
}
