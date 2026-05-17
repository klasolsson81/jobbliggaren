using JobbPilot.Api.IntegrationTests.Infrastructure;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.Applications;

// RÖD svit (TDD) mot Testcontainers Postgres (relationell provider).
// Spec: architect-design §3b (BLOCKING owned-entity round-trip),
// §5c (.ToQueryString() SQL-verifiering), ADR 0048 c (soft-deletad JobAd via
// default-join + query-filter, UTAN IgnoreQueryFilters).
//
// Dessa tre kräver relationell provider — Application.UnitTests kör InMemory
// som varken hedrar global query filter i join meningsfullt eller stöder
// .ToQueryString() join-SQL (architect-design §5c).
[Collection("Api")]
public class ManualPostingPersistenceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static ManualPosting ManualVo() =>
        ManualPosting.Create(
            "Manuell titel", "Manuellt företag", "https://example.com/manuell",
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)).Value;

    private static async Task<JobSeekerId> SeedSeekerAsync(
        AppDbContext db, IDateTimeProvider clock, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(Guid.NewGuid(), "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker.Id;
    }

    // ---------------------------------------------------------------
    // §3b BLOCKING — optional owned-entity null-semantik
    // ---------------------------------------------------------------

    [Fact]
    public async Task Application_WithoutManualPosting_MaterializesManualPostingAsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seekerId = await SeedSeekerAsync(db, clock, ct);
        var app = JobbPilot.Domain.Applications.Application.Create(seekerId, null, "Bara brev", null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var reloaded = await db.Applications
            .AsNoTracking()
            .FirstAsync(a => a.Id == app.Id, ct);

        // BLOCKING: alla manual_*-kolumner NULL → navigeringen materialiseras
        // som null, EJ en all-null ManualPosting-instans.
        reloaded.ManualPosting.ShouldBeNull();
    }

    [Fact]
    public async Task Application_WithManualPosting_RoundTripsAllFields()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seekerId = await SeedSeekerAsync(db, clock, ct);
        var app = JobbPilot.Domain.Applications.Application.Create(seekerId, null, null, ManualVo(), clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var reloaded = await db.Applications
            .AsNoTracking()
            .FirstAsync(a => a.Id == app.Id, ct);

        reloaded.ManualPosting.ShouldNotBeNull();
        reloaded.ManualPosting!.Title.ShouldBe("Manuell titel");
        reloaded.ManualPosting.Company.ShouldBe("Manuellt företag");
        reloaded.ManualPosting.Url.ShouldBe("https://example.com/manuell");
        reloaded.ManualPosting.ExpiresAt.ShouldBe(
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
    }

    // ---------------------------------------------------------------
    // ADR 0048 c — soft-deletad JobAd faller ut via query-filter +
    // DefaultIfEmpty (jobAd = null), UTAN IgnoreQueryFilters/eget predikat.
    // ---------------------------------------------------------------

    [Fact]
    public async Task ReadJoin_WithSoftDeletedJobAd_FallsBackToNullViaQueryFilter()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seekerId = await SeedSeekerAsync(db, clock, ct);

        var jobAd = JobAd.Create(
            "Backend-utvecklare", Company.Create("Klarna").Value, "Beskrivning",
            "https://example.com/jobb/1", JobSource.Platsbanken,
            clock.UtcNow.AddDays(-2), null, clock).Value;
        db.JobAds.Add(jobAd);
        var app = JobbPilot.Domain.Applications.Application.Create(seekerId, jobAd.Id, null, null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);

        // Soft-delete annonsen genom att sätta DeletedAt (JobAd:s globala
        // query filter är DeletedAt == null). JobAd har ingen domän-SoftDelete-
        // metod (Archive sätter Status, ej DeletedAt — verifierat); testet
        // verifierar query-filter-/read-join-kontraktet, ej ett domän-API, så
        // DeletedAt sätts via EF direkt (architect-fix-rapport 2026-05-17).
        db.Entry(jobAd).Property(nameof(JobAd.DeletedAt)).CurrentValue = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // Default-join (query-filtret exkluderar soft-deletad JobAd FÖRE joinen);
        // DefaultIfEmpty → null. Detta speglar read-handler-projektionen.
        var jobAdResult = await (
            from a in db.Applications.AsNoTracking().Where(a => a.Id == app.Id)
            join j in db.JobAds on a.JobAdId equals j.Id into ja
            from j in ja.DefaultIfEmpty()
            select j).FirstOrDefaultAsync(ct);

        // Application själv finns kvar (ej soft-deletad), men JobAd-grenen är null.
        jobAdResult.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // §5c — .ToQueryString() SQL-verifiering: EN LEFT JOIN mot job_ads +
    // query-filter-predikat (deleted_at IS NULL) på den joinade grenen.
    // Producerad SQL klistras verbatim i STOPP 3a-rapporten (ADR 0048-gate).
    // ---------------------------------------------------------------

    [Fact]
    public void ReadJoinQuery_GeneratesExactlyOneLeftJoinAgainstJobAds()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Den projicerade join-formen (architect-design §5b kanonisk form),
        // FÖRE ToListAsync — exakt det read-handlers genererar.
        var query =
            from a in db.Applications.AsNoTracking()
            join j in db.JobAds on a.JobAdId equals j.Id into ja
            from j in ja.DefaultIfEmpty()
            select new { a.Id, JobAdTitle = j != null ? j.Title : null };

        var sql = query.ToQueryString();

        // EN LEFT JOIN mot job_ads (ej post-materialiserings-lookup/N+1).
        CountOccurrences(sql, "LEFT JOIN").ShouldBe(1);
        sql.ShouldContain("job_ads");
        // Query-filter-predikatet ska finnas på den joinade job_ads-grenen
        // (ADR 0048 c — IgnoreQueryFilters FÖRBJUDET; filtret applicerat).
        sql.ShouldContain("deleted_at");
        sql.ShouldNotContain("IgnoreQueryFilters");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
