using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.JobSources;

/// <summary>
/// #841 — the ACL's facet mapping (<c>PlatsbankenJobSource.MapFacets</c>): the seven taxonomy/employer
/// concept ids parsed out of a JobTech hit and carried on <see cref="JobAdImportItem.Facets"/>.
///
/// <para>
/// <b>This file holds the payload-shape knowledge that used to live in SQL.</b> Before #841 the seven were
/// Postgres STORED generated columns whose <c>HasComputedColumnSql</c> expressions encoded the JSON paths —
/// and those expressions were pinned by a Testcontainers test, because a wrong path yields a column that is
/// silently always-NULL, with no compile error and no runtime error. The paths have moved into the ACL, so
/// the pin moves here, and it must be just as sharp: <b>every assertion below fails if a path is wrong,
/// rather than the product quietly losing a facet.</b>
/// </para>
///
/// <para>
/// Two traps are the whole reason this test is not decoration:
/// <list type="number">
///   <item><b>Nesting is inconsistent in the source format.</b> <c>occupation_group</c>,
///     <c>employment_type</c> and <c>working_hours_type</c> are TOP-LEVEL, while <c>occupation</c> (ssyk)
///     and <c>employer</c> (org.nr) are nested objects. Reading ssyk from the top level compiles fine and
///     yields nothing.</item>
///   <item><b>The name gap.</b> The column and taxonomy type are <c>worktime-extent</c> (ADR 0067 Beslut 2
///     locks the column name to the taxonomy type), but the wire key is <c>working_hours_type</c>. Mapping
///     <c>WorktimeExtentConceptId</c> from anything else gives a permanently empty facet — and the Klass 2
///     filter simply stops returning ads.</item>
/// </list>
/// </para>
///
/// Mirrors <see cref="PlatsbankenJobSourceRequirementsTests"/>: constructs the <c>internal</c> source via
/// InternalsVisibleTo with hand-written fakes and drives the public <c>RefetchByExternalIdAsync</c> to
/// reach the private conversion.
/// </summary>
public class PlatsbankenJobSourceFacetMappingTests
{
    private static readonly DateTimeOffset FakeNow = new(2026, 7, 13, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Published = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private static PlatsbankenJobSource CreateSut(JobTechHit hit) =>
        new(new FakeStreamClient(),
            new FakeSearchClient(hit),
            new FakeDateTimeProvider(FakeNow),
            NullLogger<PlatsbankenJobSource>.Instance);

    // A hit carrying every one of the seven facet sources, each at its REAL position in the payload.
    private static JobTechHit FullyFacetedHit(string id) => new()
    {
        Id = id,
        Headline = "Sjuksköterska till akutmottagning",
        Description = new JobTechDescription { Text = "Beskrivning av tjänsten." },
        WebpageUrl = "https://arbetsformedlingen.se/platsbanken/annonser/" + id,
        PublicationDate = Published,

        // NESTED under `occupation` — not top-level.
        Occupation = new JobTechOccupation { ConceptId = "Ssyk_uwa_111" },
        // TOP-LEVEL — despite the name looking like a child of `occupation`.
        OccupationGroup = new JobTechOccupationGroup { ConceptId = "DJh5_yyF_hEM" },
        WorkplaceAddress = new JobTechWorkplaceAddress
        {
            MunicipalityConceptId = "AvNB_uwa_6n6",
            RegionConceptId = "CaRE_1nn_hRb",
        },
        // TOP-LEVEL.
        EmploymentType = new JobTechEmploymentType { ConceptId = "PFZr_Syz_cUq" },
        // TOP-LEVEL, and the NAME GAP: this feeds worktime_extent_concept_id.
        WorkingHoursType = new JobTechWorkingHoursType { ConceptId = "6YE1_gAC_R2G" },
        // NESTED under `employer`.
        Employer = new JobTechEmployer { Name = "Region Stockholm", OrganizationNumber = "5592804784" },
    };

    [Fact]
    public async Task RefetchByExternalIdAsync_MapsAllSevenFacets_FromTheirRealPayloadPositions()
    {
        var sut = CreateSut(FullyFacetedHit("ext-facets-1"));

        var item = await sut.RefetchByExternalIdAsync(
            "ext-facets-1", TestContext.Current.CancellationToken);

        item.ShouldNotBeNull();
        var facets = item.Facets;

        facets.SsykConceptId.ShouldBe("Ssyk_uwa_111", "occupation.concept_id — NESTED");
        facets.OccupationGroupConceptId.ShouldBe("DJh5_yyF_hEM", "occupation_group.concept_id — TOP-LEVEL");
        facets.MunicipalityConceptId.ShouldBe("AvNB_uwa_6n6", "workplace_address.municipality_concept_id");
        facets.RegionConceptId.ShouldBe("CaRE_1nn_hRb", "workplace_address.region_concept_id");
        facets.EmploymentTypeConceptId.ShouldBe("PFZr_Syz_cUq", "employment_type.concept_id — TOP-LEVEL");
        facets.OrganizationNumber.ShouldBe("5592804784", "employer.organization_number — NESTED");

        facets.WorktimeExtentConceptId.ShouldBe("6YE1_gAC_R2G",
            "THE NAME GAP: worktime_extent_concept_id is fed by the payload's working_hours_type key " +
            "(ADR 0067 Beslut 2 names the column after the taxonomy type, not the wire key). Map it from " +
            "anything else and the facet is permanently empty — the Klass 2 filter silently returns " +
            "nothing, with no error anywhere.");
    }

    [Fact]
    public async Task RefetchByExternalIdAsync_DoesNotReadSsykFromTheOccupationGroup()
    {
        // The nesting trap, isolated. A hit with occupation_group but NO occupation must yield an EMPTY
        // ssyk facet — if the mapping reached for the wrong node, ssyk would silently take the yrkesgrupp
        // id and every ssyk-filtered query would match the wrong ads.
        var hit = FullyFacetedHit("ext-facets-2");
        hit.Occupation = null;

        var sut = CreateSut(hit);

        var item = await sut.RefetchByExternalIdAsync(
            "ext-facets-2", TestContext.Current.CancellationToken);

        item.ShouldNotBeNull();
        item.Facets.SsykConceptId.ShouldBeNull(
            "no `occupation` node → no ssyk facet. It must NOT fall back to occupation_group: they are " +
            "different taxonomy levels, and conflating them would return the wrong ads.");
        item.Facets.OccupationGroupConceptId.ShouldBe("DJh5_yyF_hEM", "the sibling is unaffected");
    }

    [Fact]
    public async Task RefetchByExternalIdAsync_YieldsEmptyFacets_WhenTheSourceCarriesNone()
    {
        // Graceful degradation, and the shape the partial `WHERE ... IS NOT NULL` indexes rely on: an ad
        // with no taxonomy data carries seven NULLs (not seven empty strings — see JobAdFacetsTests) and
        // simply does not enter those indexes.
        var hit = FullyFacetedHit("ext-facets-3");
        hit.Occupation = null;
        hit.OccupationGroup = null;
        hit.WorkplaceAddress = null;
        hit.EmploymentType = null;
        hit.WorkingHoursType = null;
        hit.Employer = new JobTechEmployer { Name = "Region Stockholm" }; // name only, no org.nr

        var sut = CreateSut(hit);

        var item = await sut.RefetchByExternalIdAsync(
            "ext-facets-3", TestContext.Current.CancellationToken);

        item.ShouldNotBeNull();
        item.Facets.IsEmpty.ShouldBeTrue(
            "a hit with no taxonomy and no org.nr must produce JobAdFacets with all seven null");
    }

    [Fact]
    public async Task RefetchByExternalIdAsync_NormalisesABlankConceptId_ToNull()
    {
        // The wire can send "" rather than omitting the key. JobAdFacets refuses it at construction, and
        // this proves the ACL does not route around that: "" IS NOT NULL in Postgres, so it would sit
        // inside the partial index matching nothing, forever.
        var hit = FullyFacetedHit("ext-facets-4");
        hit.Occupation = new JobTechOccupation { ConceptId = "" };
        hit.Employer = new JobTechEmployer { Name = "Region Stockholm", OrganizationNumber = "   " };

        var sut = CreateSut(hit);

        var item = await sut.RefetchByExternalIdAsync(
            "ext-facets-4", TestContext.Current.CancellationToken);

        item.ShouldNotBeNull();
        item.Facets.SsykConceptId.ShouldBeNull("a blank concept id is an absent one");
        item.Facets.OrganizationNumber.ShouldBeNull("whitespace is blank too");
    }

    private sealed class FakeSearchClient(JobTechHit? hit = null, IReadOnlyList<string>? remoteIds = null)
        : IJobTechSearchClient
    {
        public Task<JobTechHit?> GetAdByIdAsync(
            string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(hit);

        // #551 — the remote-harvest source. Paginates the injected remote-id list exactly like the
        // real JobSearch client, so a test can assert MapFacets sets JobAdFacets.Remote from membership.
        public Task<JobTechSearchListResponse> SearchRemoteAsync(
            int offset, int limit, CancellationToken cancellationToken = default)
        {
            var all = remoteIds ?? [];
            var hits = all.Skip(offset).Take(limit).Select(id => new JobTechHit { Id = id }).ToList();
            return Task.FromResult(new JobTechSearchListResponse
            {
                Total = new JobTechSearchTotal { Value = all.Count },
                Hits = hits,
            });
        }
    }

    private sealed class FakeStreamClient(params JobTechHit[] hits) : IJobTechStreamClient
    {
        public IAsyncEnumerable<JobTechHit> FetchSnapshotAsync(CancellationToken cancellationToken) =>
            Yield(hits, cancellationToken);

        public IAsyncEnumerable<JobTechHit> StreamChangesAsync(
            DateTimeOffset since, CancellationToken cancellationToken) =>
            Yield(hits, cancellationToken);

        private static async IAsyncEnumerable<JobTechHit> Yield(
            JobTechHit[] items,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }

            await Task.CompletedTask;
        }
    }
}
