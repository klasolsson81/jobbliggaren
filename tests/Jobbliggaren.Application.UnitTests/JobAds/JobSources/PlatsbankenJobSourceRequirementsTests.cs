using System.Runtime.CompilerServices;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.JobSources;

/// <summary>
/// Fas 4 STEG 4b (F4-4b, ADR 0071/0074/0075) — the ACL mapping in
/// <see cref="PlatsbankenJobSource"/>: <c>hit.MustHave?.Skills</c> +
/// <c>hit.NiceToHave?.Skills</c> → <see cref="JobAdImportItem.Requirements"/>. v1
/// scope (CTO Decision 1A) maps ONLY the <c>skills</c> sub-array to Requirement
/// terms; languages/work_experiences/education are captured into <c>raw_payload</c>
/// by the POCO but NOT turned into Requirements this STEG.
///
/// Mirrors <see cref="PlatsbankenJobSourceUtcNormalizationTests"/> exactly:
/// constructs the <c>internal</c> <see cref="PlatsbankenJobSource"/> via
/// InternalsVisibleTo with hand-written fake clients (the internal JobTech client
/// interfaces can't be NSubstitute-proxied — no DynamicProxyGenAssembly2 grant), and
/// exercises the public <c>RefetchByExternalIdAsync</c> to reach the private
/// <c>TryConvertToImportItem</c> and assert on the resulting <see cref="JobAdImportItem"/>.
///
/// RED until: the <c>JobTechRequirements</c>/<c>JobTechRequirementConcept</c> POCOs,
/// <c>JobTechHit.must_have/nice_to_have</c>, <c>JobAdImportItem.Requirements</c>, and
/// the ACL mapping ship.
/// </summary>
public class PlatsbankenJobSourceRequirementsTests
{
    private static readonly DateTimeOffset FakeNow =
        new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Published =
        new(2026, 6, 14, 12, 0, 0, TimeSpan.Zero);

    private static PlatsbankenJobSource CreateSut(IJobTechSearchClient searchClient) =>
        new(
            new FakeStreamClient(),
            searchClient,
            new FakeDateTimeProvider(FakeNow),
            NullLogger<PlatsbankenJobSource>.Instance);

    // A minimal valid hit (the non-requirement fields TryConvertToImportItem needs to
    // NOT skip the hit). Requirements are attached per-test.
    private static JobTechHit ValidHit(
        string id,
        JobTechRequirements? mustHave = null,
        JobTechRequirements? niceToHave = null) => new()
        {
            Id = id,
            Headline = "Sjuksköterska till akutmottagning",
            Description = new JobTechDescription { Text = "Beskrivning av tjänsten." },
            Employer = new JobTechEmployer { Name = "Region Stockholm" },
            WebpageUrl = "https://arbetsformedlingen.se/platsbanken/annonser/" + id,
            PublicationDate = Published,
            MustHave = mustHave,
            NiceToHave = niceToHave,
        };

    private static JobTechRequirementConcept Concept(
        string? conceptId, string? label, int? weight, string? legacy = "1") => new()
        {
            ConceptId = conceptId,
            Label = label,
            Weight = weight,
            LegacyAmsTaxonomyId = legacy,
        };

    // ===============================================================
    // must_have + nice_to_have SKILLS → Requirements with the right shape
    // ===============================================================

    [Fact]
    public async Task RefetchByExternalIdAsync_MapsMustHaveAndNiceToHaveSkillsToRequirements()
    {
        var hit = ValidHit(
            "ext-req-1",
            mustHave: new JobTechRequirements
            {
                Skills = [Concept("Rq01_must_aaa", "C#", weight: 10)],
            },
            niceToHave: new JobTechRequirements
            {
                Skills = [Concept("Rq02_nice_bbb", "Azure", weight: 5)],
            });
        var sut = CreateSut(new FakeSearchClient(hit));

        var item = await sut.RefetchByExternalIdAsync(
            "ext-req-1", TestContext.Current.CancellationToken);

        item.ShouldNotBeNull();
        item.Requirements.Count.ShouldBe(2);

        var must = item.Requirements
            .Where(r => r.Source == ExtractedTermSource.MustHave).ShouldHaveSingleItem();
        must.ConceptId.ShouldBe("Rq01_must_aaa");
        must.Label.ShouldBe("C#");
        must.Weight.ShouldBe(10);

        var nice = item.Requirements
            .Where(r => r.Source == ExtractedTermSource.NiceToHave).ShouldHaveSingleItem();
        nice.ConceptId.ShouldBe("Rq02_nice_bbb");
        nice.Label.ShouldBe("Azure");
        nice.Weight.ShouldBe(5);
    }

    // ===============================================================
    // weight: null → 0.0 floor (the VO Weight invariant: finite, non-negative)
    // ===============================================================

    [Fact]
    public async Task RefetchByExternalIdAsync_MapsNullWeightToZeroFloor()
    {
        var hit = ValidHit(
            "ext-req-nullw",
            mustHave: new JobTechRequirements
            {
                Skills = [Concept("Rq03_must_ccc", "Java", weight: null)],
            });
        var sut = CreateSut(new FakeSearchClient(hit));

        var item = await sut.RefetchByExternalIdAsync(
            "ext-req-nullw", TestContext.Current.CancellationToken);

        item.ShouldNotBeNull();
        var req = item.Requirements.ShouldHaveSingleItem();
        req.Weight.ShouldBe(0.0,
            "weight: null ska mappas till golvet 0.0 vid ACL-gränsen (aldrig propagera null " +
            "— det kraschar ExtractedTerm.Weight-invarianten finit & icke-negativ).");
    }

    // ===============================================================
    // Blank concept_id / blank label → the requirement is skipped
    // ===============================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RefetchByExternalIdAsync_SkipsRequirementWithBlankConceptId(string? conceptId)
    {
        var hit = ValidHit(
            "ext-req-blankid",
            mustHave: new JobTechRequirements
            {
                Skills =
                [
                    Concept(conceptId, "Har label men blank concept", weight: 10),
                    Concept("Rq04_must_ddd", "Giltig", weight: 10),
                ],
            });
        var sut = CreateSut(new FakeSearchClient(hit));

        var item = await sut.RefetchByExternalIdAsync(
            "ext-req-blankid", TestContext.Current.CancellationToken);

        item.ShouldNotBeNull();
        // Only the valid one survives; the blank-concept-id one is dropped.
        var req = item.Requirements.ShouldHaveSingleItem();
        req.ConceptId.ShouldBe("Rq04_must_ddd");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RefetchByExternalIdAsync_SkipsRequirementWithBlankLabel(string? label)
    {
        var hit = ValidHit(
            "ext-req-blanklabel",
            mustHave: new JobTechRequirements
            {
                Skills =
                [
                    Concept("Rq05_must_eee", label, weight: 10),
                    Concept("Rq06_must_fff", "Giltig", weight: 10),
                ],
            });
        var sut = CreateSut(new FakeSearchClient(hit));

        var item = await sut.RefetchByExternalIdAsync(
            "ext-req-blanklabel", TestContext.Current.CancellationToken);

        item.ShouldNotBeNull();
        var req = item.Requirements.ShouldHaveSingleItem();
        req.ConceptId.ShouldBe("Rq06_must_fff");
    }

    // ===============================================================
    // 1A scope: languages / education / work_experiences are NOT mapped to
    //           Requirements (only skills)
    // ===============================================================

    [Fact]
    public async Task RefetchByExternalIdAsync_DoesNotMapLanguagesOrEducationToRequirements()
    {
        // CTO Decision 1A: ONLY the skills sub-array becomes Requirement terms v1.
        // languages/work_experiences/education are captured into raw_payload by the
        // POCO but never turned into Requirement terms (no CV-side concept to match).
        var hit = ValidHit(
            "ext-req-1a",
            mustHave: new JobTechRequirements
            {
                Skills = [Concept("Rq07_must_ggg", "C#", weight: 10)],
                Languages = [Concept("Lng_swe_001", "Svenska", weight: 10)],
                Educations = [Concept("Edu_nurse_01", "Sjuksköterskeexamen", weight: 10)],
                WorkExperiences = [Concept("Occ_nurse_01", "Sjuksköterska", weight: 10)],
                EducationLevel = [Concept("Sun_level_5", "Eftergymnasial", weight: 10)],
            });
        var sut = CreateSut(new FakeSearchClient(hit));

        var item = await sut.RefetchByExternalIdAsync(
            "ext-req-1a", TestContext.Current.CancellationToken);

        item.ShouldNotBeNull();
        // ONLY the skill requirement is mapped.
        var req = item.Requirements.ShouldHaveSingleItem();
        req.ConceptId.ShouldBe("Rq07_must_ggg",
            "1A: bara skills mappas till Requirements — språk/utbildning/yrkeserfarenhet " +
            "deserialiseras men blir aldrig Requirement-termer v1.");
        item.Requirements.ShouldNotContain(r => r.ConceptId == "Lng_swe_001");
        item.Requirements.ShouldNotContain(r => r.ConceptId == "Edu_nurse_01");
        item.Requirements.ShouldNotContain(r => r.ConceptId == "Occ_nurse_01");
    }

    // ===============================================================
    // After conversion, SanitizedRawPayload CONTAINS must_have (POCO-expansion proof)
    // ===============================================================

    [Fact]
    public async Task RefetchByExternalIdAsync_SanitizedRawPayloadContainsMustHaveKey()
    {
        // The re-ingest predicate (!JsonExists(raw_payload,'must_have')) only flips to
        // "skip" AFTER a row is re-ingested with the new POCO. So the converted item's
        // SanitizedRawPayload must carry the must_have key — the proof that the POCO
        // re-emits it AND the sanitizer allowlist passes it through.
        var hit = ValidHit(
            "ext-req-payload",
            mustHave: new JobTechRequirements
            {
                Skills = [Concept("Rq08_must_hhh", "C#", weight: 10)],
            });
        var sut = CreateSut(new FakeSearchClient(hit));

        var item = await sut.RefetchByExternalIdAsync(
            "ext-req-payload", TestContext.Current.CancellationToken);

        item.ShouldNotBeNull();
        // raw_payload måste bära must_have-nyckeln efter konvertering (POCO-expansion +
        // sanitizer-allowlist) — annars flippar re-ingest-predikatet aldrig till skip.
        item.SanitizedRawPayload.ShouldContain("\"must_have\"");
    }

    // ===============================================================
    // Absent must_have/nice_to_have → empty Requirements (back-compat, no crash)
    // ===============================================================

    [Fact]
    public async Task RefetchByExternalIdAsync_AbsentRequirements_YieldsEmptyRequirements()
    {
        var hit = ValidHit("ext-req-none"); // no must_have / nice_to_have
        var sut = CreateSut(new FakeSearchClient(hit));

        var item = await sut.RefetchByExternalIdAsync(
            "ext-req-none", TestContext.Current.CancellationToken);

        item.ShouldNotBeNull();
        item.Requirements.ShouldBeEmpty(
            "annons utan must_have/nice_to_have ska ge tom Requirements-lista (bakåtkompatibelt).");
    }

    // Hand-written fakes — the internal JobTech client interfaces can't be
    // NSubstitute-proxied (no DynamicProxyGenAssembly2 grant in Infrastructure);
    // they are visible to this test project via InternalsVisibleTo. Parity
    // PlatsbankenJobSourceUtcNormalizationTests.

    private sealed class FakeSearchClient(JobTechHit? hit = null) : IJobTechSearchClient
    {
        public Task<JobTechSearchResponse> SearchAsync(
            string? q = null,
            int offset = 0,
            int limit = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new JobTechSearchResponse());

        public Task<JobTechHit?> GetAdByIdAsync(
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(hit);
    }

    private sealed class FakeStreamClient(params JobTechHit[] hits) : IJobTechStreamClient
    {
        public IAsyncEnumerable<JobTechHit> FetchSnapshotAsync(
            CancellationToken cancellationToken) =>
            Yield(hits, cancellationToken);

        public IAsyncEnumerable<JobTechHit> StreamChangesAsync(
            DateTimeOffset since,
            CancellationToken cancellationToken) =>
            Yield(hits, cancellationToken);

        private static async IAsyncEnumerable<JobTechHit> Yield(
            JobTechHit[] items,
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }
    }
}
