using System.Diagnostics;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2285 (dotnet-architect, #2285 plan read, point 8) — the count half of the capture command: the count of the criteria
/// the gate admitted, through the shared search port's <c>CountAsync</c>, against ADR 0045 class (c), command/write:
/// <b>p95 400 ms</b>. The corpus is the one <see cref="ExcludedTermLatencyMeasurement"/> seeds, 40 000 ads with
/// descriptions stored out of line. The store half is <c>RecentSearchCaptureLatencyMeasurement</c>.
/// <para>
/// ⚠ <b>OBSERVE-ONLY</b> (ADR 0045): durations are written to test output and read into the PR body; what is asserted is
/// that each search counted something.
/// </para>
/// </summary>
[Collection("JobAdBrowsePlan")]
[Trait("Category", "SmokeTest")]
public class CaptureCountLatencyMeasurement(JobAdBrowsePlanFixture fixture, ITestOutputHelper output)
{
    private const int TotalRows = 40_000;

    [Fact]
    public async Task TheCountOfAnAdmittedSearch_AgainstAdr0045ClassC()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeding = Stopwatch.StartNew();
        await SeedAsync(db, ct);
        output.WriteLine($"Seeded {TotalRows:N0} ads in {seeding.Elapsed.TotalSeconds:F0} s.");

        var scopeFactory = fixture.Services.GetRequiredService<IServiceScopeFactory>();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Test");
        await new TaxonomySnapshotSeeder(scopeFactory, environment, NullLogger<TaxonomySnapshotSeeder>.Instance)
            .StartAsync(ct);
        var resolver = new JobAdSearchTextResolver(
            new SearchQueryParser(),
            new OccupationSynonymExpander(Options.Create(new SearchSynonymsOptions())),
            new TaxonomyReadModel(scopeFactory),
            fixture.Services.GetRequiredService<IOccupationNameLineageProvider>(),
            db,
            fixture.Services.GetRequiredService<Jobbliggaren.Application.JobAds.Junior.IJuniorFieldCatalog>());
        var sut = new JobAdSearchQuery(db, resolver);

        output.WriteLine($"ADR 0045 class (c): p95 400 ms. {TotalRows:N0} ads, {LatencyMeasurementTools.Iterations} warmed iterations each.");
        foreach (var q in new[] { "stockholm", "systemutvecklare", "lärare stockholm", "svenska", "deltid -stockholm" })
        {
            (await sut.CountAsync(Filter(q), ct)).ShouldBeGreaterThan(0, $"\"{q}\" must count something, or the timing measures nothing");
            var (p50, p95) = await LatencyMeasurementTools.MeasureAsync(() => sut.CountAsync(Filter(q), ct).AsTask(), ct);
            output.WriteLine($"q = \"{q}\"{new string(' ', Math.Max(1, 24 - q.Length))}p50 {p50,6:F1} ms  p95 {p95,6:F1} ms");
        }
    }

    private static JobAdFilterCriteria Filter(string q) =>
        new(
            OccupationGroup: [], OccupationName: [], Municipality: [], Region: [], EmploymentType: [], WorktimeExtent: [],
            Employer: [], Remote: false, JuniorIt: false, Q: q);

    // The same corpus as ExcludedTermLatencyMeasurement's, so the two classes' numbers describe one table.
    private static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        db.Database.SetCommandTimeout(600);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE job_ads CASCADE;", ct);

        var bulk =
            "INSERT INTO job_ads "
            + "(id, title, description, url, published_at, created_at, status, source, company_name, remote) "
            + "SELECT gen_random_uuid(), "
            + "CASE WHEN i % 20 IN (7, 9) THEN 'Lärare ' ELSE 'Systemutvecklare ' END || i, "
            + "(SELECT string_agg(md5(((i * 31 + g * 17) % 500)::text), ' ') FROM generate_series(1, 70) AS g) "
            + "|| CASE WHEN i % 10 = 3 OR i % 20 = 7 THEN ' Tjänsten är på deltid.' ELSE '' END "
            + "|| CASE WHEN i % 4 = 1 THEN ' Placering Stockholm.' ELSE '' END "
            + "|| CASE WHEN i % 10 <> 0 THEN ' Svenska krävs.' ELSE '' END, "
            + "'https://example.com/jobb/' || i, "
            + "now() - (((i * 7919) % " + TotalRows + ") || ' minutes')::interval, "
            + "now(), "
            + "CASE WHEN i % 5 = 0 THEN 'Archived' ELSE 'Active' END, "
            + "'Platsbanken', "
            + "'Arbetsgivare ' || (i % 997) || ' AB', "
            + "false "
            + "FROM generate_series(0, " + (TotalRows - 1) + ") AS i;";
        await db.Database.ExecuteSqlRawAsync(bulk, ct);

        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) job_ads;", ct);
    }
}
