using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

// #551 PR-B D5 — the remote/distans FACET filter in the shared ApplyFilter SPOT.
// Verifies JobAdSearchComposition.ApplyFilter against real Postgres (Testcontainers,
// NEVER EF-InMemory: the geo-union predicate + EF.Property<string?>-translation is
// Npgsql-only — InMemory gives false greens, cf. JobAdFacetCountsTests). The remote
// column (bool NOT NULL, PR-A) unions with the kommun/län axes:
//   Remote=true  → kommun ∨ län ∨ remote (a remote ad is a location match)
//   Remote=false → BYTE-IDENTICAL to pre-#551 (no remote term emitted). The existing
//                  JobAdSearch/ListJobAdsFts/facet oracles are the byte-identity gate;
//                  this file adds the explicit non-inclusion counterfactual (case 4).
//
// Run-isolation over the shared Api DB (reference_api_integration_shared_db_contamination):
// every seeded ad carries a unique per-run occupation-group concept-id, and every
// assertion ANDs on it — so a sibling run's ads can never be counted (parity
// MatchSortOracleTests.FilterFor).
[Collection("Api")]
public class JobAdSearchRemoteFilterTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task SeedAsync(
        string runKey,
        string title,
        string? municipality,
        string? region,
        bool remote,
        CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var externalId = $"ext-{Guid.NewGuid():N}";

        // workplace_address bär BÅDE region_concept_id och municipality_concept_id.
        var addressFields = new List<string>();
        if (region is not null)
            addressFields.Add($"\"region_concept_id\":\"{region}\"");
        if (municipality is not null)
            addressFields.Add($"\"municipality_concept_id\":\"{municipality}\"");
        var workplaceAddressJson = addressFields.Count == 0
            ? "null"
            : "{" + string.Join(",", addressFields) + "}";

        var rawPayload =
            $"{{\"id\":\"{externalId}\"," +
            $"\"occupation_group\":{{\"concept_id\":\"{runKey}\"}}," +
            $"\"workplace_address\":{workplaceAddressJson}}}";

        // Remote is AF's harvested classification, NOT a raw_payload key (ADR 0067 Beslut 3):
        // state it as a separate facet arg exactly like the ACL does (TestFacets.From),
        // while keeping the payload/facets consistent on occupation-group + ort.
        var facets = TestFacets.From(
            occupationGroup: runKey,
            municipality: municipality,
            region: region,
            remote: remote);

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "desc",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: facets,
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    private static JobAdSearchQuery CreateSut(IServiceScope scope) =>
        new(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            Substitute.For<IOccupationSynonymExpander>());

    private static JobAdFilterCriteria Criteria(
        string runKey,
        IReadOnlyList<string>? municipality = null,
        IReadOnlyList<string>? region = null,
        bool remote = false) =>
        new(
            OccupationGroup: [runKey],
            Municipality: municipality ?? [],
            Region: region ?? [],
            EmploymentType: [],
            WorktimeExtent: [],
            Employer: [],
            Remote: remote,
            Q: null);

    private async Task<int> CountAsync(JobAdFilterCriteria criteria, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        return await CreateSut(scope).CountAsync(criteria, ct);
    }

    // Göteborg = kommun; Skåne = län; the remote ad is location-LESS (the whole premise).
    private const string Goteborg = "GbgKommun";
    private const string Skane = "SkaneLan";
    private const string Stockholm = "SthlmKommun";

    private async Task<string> SeedFixtureAsync(CancellationToken ct)
    {
        var runKey = Guid.NewGuid().ToString("N"); // 32 hex chars ⊂ concept-id regex
        await SeedAsync(runKey, "GBG on-site", municipality: Goteborg, region: null, remote: false, ct);
        await SeedAsync(runKey, "STHLM on-site", municipality: Stockholm, region: null, remote: false, ct);
        await SeedAsync(runKey, "Skane-lan on-site", municipality: null, region: Skane, remote: false, ct);
        await SeedAsync(runKey, "Remote, no location", municipality: null, region: null, remote: true, ct);
        return runKey;
    }

    [Fact]
    public async Task ApplyFilter_RemoteOnly_ReturnsOnlyRemoteAds()
    {
        var ct = TestContext.Current.CancellationToken;
        var runKey = await SeedFixtureAsync(ct);

        // Distans utan ort → endast den locationless remote-annonsen.
        var count = await CountAsync(Criteria(runKey, remote: true), ct);

        count.ShouldBe(1);
    }

    [Fact]
    public async Task ApplyFilter_MunicipalityUnionRemote_ReturnsMunicipalityHitsPlusRemote()
    {
        var ct = TestContext.Current.CancellationToken;
        var runKey = await SeedFixtureAsync(ct);

        // Göteborg + Distans → Göteborgs-annonsen ∪ remote-annonsen (union, inte skärning).
        var count = await CountAsync(
            Criteria(runKey, municipality: [Goteborg], remote: true), ct);

        count.ShouldBe(2);
    }

    [Fact]
    public async Task ApplyFilter_RegionUnionRemote_ReturnsRegionHitsPlusRemote()
    {
        var ct = TestContext.Current.CancellationToken;
        var runKey = await SeedFixtureAsync(ct);

        // Skåne (län) + Distans → läns-annonsen ∪ remote-annonsen.
        var count = await CountAsync(
            Criteria(runKey, region: [Skane], remote: true), ct);

        count.ShouldBe(2);
    }

    [Fact]
    public async Task ApplyFilter_MunicipalityAndRegionUnionRemote_ReturnsAllThreeAxes()
    {
        var ct = TestContext.Current.CancellationToken;
        var runKey = await SeedFixtureAsync(ct);

        // Göteborg ∪ Skåne ∪ remote (kommun-träff ELLER läns-träff ELLER remote).
        var count = await CountAsync(
            Criteria(runKey, municipality: [Goteborg], region: [Skane], remote: true), ct);

        count.ShouldBe(3);
    }

    [Fact]
    public async Task ApplyFilter_MunicipalityWithoutDistans_ExcludesRemoteAd_ByteIdentityCounterfactual()
    {
        var ct = TestContext.Current.CancellationToken;
        var runKey = await SeedFixtureAsync(ct);

        // Remote=false: den befintliga muni-grenen körs ORÖRD → INTE remote-annonsen.
        // Detta är #552-motfaktumet: utan Distans-valet golvar en location-scoped sökning
        // bort remote-annonsen (byte-identisk med pre-#551).
        var count = await CountAsync(
            Criteria(runKey, municipality: [Goteborg], remote: false), ct);

        count.ShouldBe(1);
    }
}
