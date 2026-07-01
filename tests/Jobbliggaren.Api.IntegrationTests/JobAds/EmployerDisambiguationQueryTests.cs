using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

// ADR 0087 D6/D7 (#311 PR-2b C2) — the employer-disambiguation projection against REAL
// Testcontainers-Postgres (NEVER EF-InMemory: ILIKE + GROUP BY over the STORED organization_number
// shadow column translate ONLY via Npgsql; InMemory gives false greens —
// feedback_ef_strongly_typed_vo_contains_translation).
//
// On-disk payload path: org.nr is a STORED generated column populated by Postgres at INSERT from
// raw_payload->'employer'->>'organization_number'. Each test uses a UNIQUE brand token in the
// company name + queries it, so the shared Api DB's other rows never contaminate the assertions
// (reference_api_integration_shared_db_contamination).
//
// This suite pins the Infra projection ONLY (raw org.nr, no masking — that is the handler's job,
// pinned by DisambiguateEmployersQueryHandlerTests). It proves: ILIKE case-insensitive contains,
// GROUP BY → one row per legal entity with COUNT, distinct entities, NULL-org.nr exclusion, the cap,
// count-desc ordering, and that Infra returns the RAW value even for a personnummer-shaped org.nr.
[Collection("Api")]
public class EmployerDisambiguationQueryTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task SeedAdAsync(
        string organizationNumber, string companyName, string externalId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var employerJson = organizationNumber is null
            ? $"{{\"name\":\"{companyName}\"}}"
            : $"{{\"name\":\"{companyName}\",\"organization_number\":\"{organizationNumber}\"}}";
        var rawPayload = $"{{\"id\":\"{externalId}\",\"employer\":{employerJson}}}";

        var jobAd = JobAd.Import(
            title: "Utvecklare",
            company: Company.Create(companyName).Value,
            description: "desc",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<Application.JobAds.Abstractions.EmployerAdGroup>> RunAsync(
        string nameQuery, int limit, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await new EmployerDisambiguationQuery(db).SearchAsync(nameQuery, limit, ct);
    }

    // Unique brand token so ILIKE %token% matches ONLY this test's seeded ads in the shared DB.
    private static string NewBrand() => "Disam" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task Search_IsCaseInsensitiveContains_OnCompanyName()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();

        await SeedAdAsync("5566010101", $"{brand} Cars AB", $"ext-{Guid.NewGuid():N}", ct);

        // Lower-cased, partial term still matches (ILIKE contains).
        var result = await RunAsync(brand.ToLowerInvariant(), 50, ct);

        result.Count.ShouldBe(1);
        result[0].CompanyName.ShouldBe($"{brand} Cars AB");
        result[0].OrganizationNumber.ShouldBe("5566010101");
        result[0].AdCount.ShouldBe(1);
    }

    [Fact]
    public async Task Search_GroupsByOrgNr_CountingAdsPerEntity()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();
        const string org = "5566020202";

        // Three ads, SAME employer (org.nr + name) → one group, count 3.
        await SeedAdAsync(org, $"{brand} AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync(org, $"{brand} AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync(org, $"{brand} AB", $"ext-{Guid.NewGuid():N}", ct);

        var result = await RunAsync(brand, 50, ct);

        result.Count.ShouldBe(1);
        result[0].OrganizationNumber.ShouldBe(org);
        result[0].AdCount.ShouldBe(3);
    }

    [Fact]
    public async Task Search_DistinctEntities_SameBrand_YieldSeparateRows_OrderedByCountDesc()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();
        const string orgBig = "5566030303";   // 2 ads
        const string orgSmall = "5566040404"; // 1 ad

        await SeedAdAsync(orgBig, $"{brand} Cars AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync(orgBig, $"{brand} Cars AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync(orgSmall, $"{brand} Bussar AB", $"ext-{Guid.NewGuid():N}", ct);

        var result = await RunAsync(brand, 50, ct);

        result.Count.ShouldBe(2);
        // Most-prolific first (count desc).
        result[0].OrganizationNumber.ShouldBe(orgBig);
        result[0].AdCount.ShouldBe(2);
        result[1].OrganizationNumber.ShouldBe(orgSmall);
        result[1].AdCount.ShouldBe(1);
    }

    [Fact]
    public async Task Search_ExcludesAdsWithNullOrgNr()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();

        // One with org.nr, one without (B2-era payload: employer.name only).
        await SeedAdAsync("5566050505", $"{brand} Med AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync(null!, $"{brand} Utan AB", $"ext-{Guid.NewGuid():N}", ct);

        var result = await RunAsync(brand, 50, ct);

        result.Count.ShouldBe(1);
        result[0].CompanyName.ShouldBe($"{brand} Med AB");
    }

    [Fact]
    public async Task Search_ReturnsRawOrgNr_EvenWhenPersonnummerShaped()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();
        const string solePropOrgNr = "8501010101"; // 3rd digit '0' → personnummer-shaped

        await SeedAdAsync(solePropOrgNr, $"{brand} Enskild firma", $"ext-{Guid.NewGuid():N}", ct);

        var result = await RunAsync(brand, 50, ct);

        // Infrastructure does NOT mask — it returns the RAW value (the handler masks at the surfacing
        // boundary, ADR 0087 D8(c) / DisambiguateEmployersQueryHandlerTests). This pins the SoC.
        result.Count.ShouldBe(1);
        result[0].OrganizationNumber.ShouldBe(solePropOrgNr);
    }

    [Fact]
    public async Task Search_TreatsLikeWildcardsInTerm_AsLiterals()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();

        // One name literally contains "50%"; another would match if % were a wildcard ("50" + rest).
        await SeedAdAsync("5566080801", $"{brand} 50% rabatt AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync("5566080802", $"{brand} 5000 kr AB", $"ext-{Guid.NewGuid():N}", ct);

        // The term carries a literal "%"; EscapeLike must neutralise it so ILIKE treats it literally —
        // only the "50%"-named row matches, NEVER the "5000" row (which % would match as a wildcard).
        var result = await RunAsync($"{brand} 50%", 50, ct);

        result.Count.ShouldBe(1);
        result[0].CompanyName.ShouldBe($"{brand} 50% rabatt AB");
    }

    [Fact]
    public async Task Search_TieBreaksByNameAscending_WhenCountsEqual()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();

        // Two entities, one ad each (EQUAL count) — the deterministic tiebreak is name ascending
        // (ordinal). Seeded Beta-before-Alfa so a stable order must re-sort them, not echo insert order.
        await SeedAdAsync("5566090901", $"{brand} Beta AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync("5566090902", $"{brand} Alfa AB", $"ext-{Guid.NewGuid():N}", ct);

        var result = await RunAsync(brand, 50, ct);

        result.Count.ShouldBe(2);
        result[0].CompanyName.ShouldBe($"{brand} Alfa AB");
        result[1].CompanyName.ShouldBe($"{brand} Beta AB");
    }

    [Fact]
    public async Task Search_CapsResultsAtLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();

        // Four distinct entities sharing the brand; a limit of 2 returns only the top 2.
        await SeedAdAsync("5566060601", $"{brand} A AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync("5566060602", $"{brand} B AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync("5566060603", $"{brand} C AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync("5566060604", $"{brand} D AB", $"ext-{Guid.NewGuid():N}", ct);

        var result = await RunAsync(brand, 2, ct);

        result.Count.ShouldBe(2);
    }
}
