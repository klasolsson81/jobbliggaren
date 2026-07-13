using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

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
        // TD-13 C3: cover_letter ("Bara brev") krypteras → värm ägar-DEK i
        // samma scope FÖRE Add (direkt-seed förbi Mediator-prefetch).
        // Reload-grenen läser tillbaka cover_letter ⇒ läs-scopet (= samma
        // scope) måste också ha varm DEK; warm:ad ovan räcker.
        await EncryptionKeyTestSeed.WarmAsync(scope, seekerId, ct);
        var app = Jobbliggaren.Domain.Applications.Application.Create(seekerId, null, "Bara brev", null, clock).Value;
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
        var app = Jobbliggaren.Domain.Applications.Application.Create(seekerId, null, null, ManualVo(), clock).Value;
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
    // §5c — .ToQueryString() SQL-verifiering: EN LEFT JOIN mot job_ads, och
    // EXAKT ETT deleted_at-predikat i hela frågan.
    //
    // #821: det enda soft-delete-predikatet kommer från APPLICATION-aggregatet
    // (ApplicationConfiguration HasQueryFilter — en RIKTIG axel med en riktig
    // SoftDelete-writer). JobAd bidrar med NOLL: dess axel är retirerad. Räkningen
    // är därför en regressionsvakt åt BÅDA håll — den faller om någon återinför
    // ett query-filter på JobAd (2), och den faller om Applications egna
    // soft-delete-filter tappas bort (0). Talet 1 är empiriskt avläst ur den
    // genererade SQL:en, inte resonerat fram.
    // ---------------------------------------------------------------

    [Fact]
    public void ReadJoinQuery_GeneratesOneLeftJoinAndExactlyOneSoftDeletePredicate()
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
        // EXAKT ETT deleted_at-predikat: Applications egna (riktiga) soft-delete-filter.
        // JobAd bidrar med noll (#821). Två ⇒ någon har återinfört JobAd:s query-filter.
        CountOccurrences(sql, "deleted_at").ShouldBe(1);
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
