using System.Security.Cryptography;
using System.Text;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.RecentJobSearches;
using JobbPilot.Domain.SavedSearches;
using Shouldly;

namespace JobbPilot.Domain.UnitTests.RecentJobSearches;

// FilterHashCalculator — deterministic SHA-256 över canonical-JSON av filter-shape.
// C2 (ADR 0067, CTO-dom (d)/(f) + architect F4): canonical-formen är
// {"q":...,"occupationGroup":[...],"municipality":[...],"region":[...],"sortBy":int}
// — "ssyk"-nyckeln UTGÅR. Ingen hash-versionering (recent-raderna raderas i
// C2-migrationen). Bär uniqueness-kontraktet UNIQUE(job_seeker_id, filter_hash)
// (ADR 0060).
//
// RÖD tills FilterHashCalculator implementerar nya canonical-formen + overloaden.
public class FilterHashCalculatorTests
{
    private static SearchCriteria Criteria(
        IEnumerable<string>? occupationGroup = null,
        IEnumerable<string>? municipality = null,
        IEnumerable<string>? region = null,
        IEnumerable<string>? employmentType = null,
        IEnumerable<string>? worktimeExtent = null,
        string? q = "backend",
        JobAdSortBy sortBy = JobAdSortBy.PublishedAtDesc) =>
        SearchCriteria.Create(
            occupationGroup: occupationGroup ?? ["grp1"],
            municipality: municipality ?? ["sthlm_kn"],
            region: region ?? ["stockholm"],
            employmentType: employmentType ?? ["et_fast"],
            worktimeExtent: worktimeExtent ?? ["wt_heltid"],
            q: q,
            sortBy: sortBy).Value;

    [Fact]
    public void Compute_ReturnsLowerCaseHex64Chars()
    {
        var hash = FilterHashCalculator.Compute(Criteria());

        hash.ShouldNotBeNull();
        hash.Length.ShouldBe(64);
        hash.ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Compute_IsDeterministic_SameInputProducesSameHash()
    {
        var a = FilterHashCalculator.Compute(Criteria());
        var b = FilterHashCalculator.Compute(Criteria());

        a.ShouldBe(b);
    }

    [Fact]
    public void Compute_CanonicalJson_MatchesDocumentedContract()
    {
        // Låser canonical-form-KONTRAKTET (B2, ADR 0067 Beslut 6/7): exakt
        // nyckelordning q → occupationGroup → municipality → region →
        // employmentType → worktimeExtent → sortBy. De två nya arrayerna ligger
        // MELLAN region och sortBy. Om Infrastructure/Domain ändrar
        // serialisering tyst förlorar vi unique-index-integritet — då ska detta
        // test falla.
        const string canonicalJson =
            """{"q":"backend","occupationGroup":["g1"],"municipality":["m1"],"region":["r1"],"employmentType":["e1"],"worktimeExtent":["w1"],"sortBy":0}""";
        var expected = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));

        var actual = FilterHashCalculator.Compute(
            q: "backend",
            occupationGroup: ["g1"],
            municipality: ["m1"],
            region: ["r1"],
            employmentType: ["e1"],
            worktimeExtent: ["w1"],
            sortBy: JobAdSortBy.PublishedAtDesc);

        actual.ShouldBe(expected);
    }

    [Fact]
    public void Compute_CriteriaOverload_EqualsExplicitOverload()
    {
        var criteria = Criteria();

        var fromCriteria = FilterHashCalculator.Compute(criteria);
        var fromExplicit = FilterHashCalculator.Compute(
            q: criteria.Q,
            occupationGroup: criteria.OccupationGroup,
            municipality: criteria.Municipality,
            region: criteria.Region,
            employmentType: criteria.EmploymentType,
            worktimeExtent: criteria.WorktimeExtent,
            sortBy: criteria.SortBy);

        fromCriteria.ShouldBe(fromExplicit);
    }

    [Fact]
    public void Compute_DifferentQ_ProducesDifferentHash()
    {
        var a = FilterHashCalculator.Compute(Criteria(q: "backend"));
        var b = FilterHashCalculator.Compute(Criteria(q: "frontend"));

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Compute_DifferentSortBy_ProducesDifferentHash()
    {
        var a = FilterHashCalculator.Compute(Criteria(sortBy: JobAdSortBy.PublishedAtDesc));
        var b = FilterHashCalculator.Compute(Criteria(sortBy: JobAdSortBy.PublishedAtAsc));

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Compute_DifferentOccupationGroup_ProducesDifferentHash()
    {
        var a = FilterHashCalculator.Compute(Criteria(occupationGroup: ["grp1"]));
        var b = FilterHashCalculator.Compute(Criteria(occupationGroup: ["grp9"]));

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Compute_DifferentMunicipality_ProducesDifferentHash()
    {
        var a = FilterHashCalculator.Compute(Criteria(municipality: ["sthlm_kn"]));
        var b = FilterHashCalculator.Compute(Criteria(municipality: ["uppsala_kn"]));

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Compute_DifferentRegion_ProducesDifferentHash()
    {
        var a = FilterHashCalculator.Compute(Criteria(region: ["stockholm"]));
        var b = FilterHashCalculator.Compute(Criteria(region: ["goteborg"]));

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Compute_SameValueInDifferentDimension_ProducesDifferentHash()
    {
        // Dimension-förväxlingsgrind: canonical-JSON:ens nycklar skiljer
        // dimensionerna åt — ["x1"] som yrkesgrupp ≠ ["x1"] som kommun.
        var a = FilterHashCalculator.Compute(
            q: null, occupationGroup: ["x1"], municipality: [], region: [],
            employmentType: [], worktimeExtent: [],
            sortBy: JobAdSortBy.PublishedAtDesc);
        var b = FilterHashCalculator.Compute(
            q: null, occupationGroup: [], municipality: ["x1"], region: [],
            employmentType: [], worktimeExtent: [],
            sortBy: JobAdSortBy.PublishedAtDesc);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Compute_NullQ_ProducesDifferentHashThanEmptyQ()
    {
        // Q=null vs Q="backend" → olika hash (null = inget filter)
        var withQ = FilterHashCalculator.Compute(Criteria(q: "backend"));
        var withoutQ = FilterHashCalculator.Compute(Criteria(q: null));

        withQ.ShouldNotBe(withoutQ);
    }

    [Fact]
    public void Compute_UnsortedOccupationGroupInput_ProducesSameHashAsSorted()
    {
        // SearchCriteria.NormalizeList sorterar ordinalt — två logiskt lika
        // kriterie-uppsättningar med olika input-ordning ska producera SAMMA hash.
        var a = FilterHashCalculator.Compute(Criteria(occupationGroup: ["zzz", "aaa", "mmm"]));
        var b = FilterHashCalculator.Compute(Criteria(occupationGroup: ["aaa", "mmm", "zzz"]));

        a.ShouldBe(b);
    }

    [Fact]
    public void Compute_DuplicateMunicipalityInput_ProducesSameHashAsDeduplicated()
    {
        var a = FilterHashCalculator.Compute(
            Criteria(municipality: ["sthlm_kn", "sthlm_kn", "uppsala_kn"]));
        var b = FilterHashCalculator.Compute(
            Criteria(municipality: ["sthlm_kn", "uppsala_kn"]));

        a.ShouldBe(b);
    }

    [Fact]
    public void Compute_NullCriteria_Throws()
    {
        Should.Throw<ArgumentNullException>(() => FilterHashCalculator.Compute(null!));
    }

    [Fact]
    public void Compute_NullLists_Throws()
    {
        Should.Throw<ArgumentNullException>(() => FilterHashCalculator.Compute(
            q: null, occupationGroup: null!, municipality: [], region: [],
            employmentType: [], worktimeExtent: [], sortBy: JobAdSortBy.PublishedAtDesc));
        Should.Throw<ArgumentNullException>(() => FilterHashCalculator.Compute(
            q: null, occupationGroup: [], municipality: null!, region: [],
            employmentType: [], worktimeExtent: [], sortBy: JobAdSortBy.PublishedAtDesc));
        Should.Throw<ArgumentNullException>(() => FilterHashCalculator.Compute(
            q: null, occupationGroup: [], municipality: [], region: null!,
            employmentType: [], worktimeExtent: [], sortBy: JobAdSortBy.PublishedAtDesc));
        // B2: de två nya list-params bär samma null-guard som de befintliga.
        Should.Throw<ArgumentNullException>(() => FilterHashCalculator.Compute(
            q: null, occupationGroup: [], municipality: [], region: [],
            employmentType: null!, worktimeExtent: [], sortBy: JobAdSortBy.PublishedAtDesc));
        Should.Throw<ArgumentNullException>(() => FilterHashCalculator.Compute(
            q: null, occupationGroup: [], municipality: [], region: [],
            employmentType: [], worktimeExtent: null!, sortBy: JobAdSortBy.PublishedAtDesc));
    }

    // ===============================================================
    // B2 (ADR 0067 Beslut 6/7) — EmploymentType + WorktimeExtent ingår i hashen.
    // ===============================================================

    [Fact]
    public void Compute_DifferentEmploymentType_ProducesDifferentHash()
    {
        // (a) Två criteria identiska utom EmploymentType → OLIKA hash.
        var a = FilterHashCalculator.Compute(Criteria(employmentType: ["et_fast"]));
        var b = FilterHashCalculator.Compute(Criteria(employmentType: ["et_vikariat"]));

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Compute_DifferentWorktimeExtent_ProducesDifferentHash()
    {
        // (b) Identiska utom WorktimeExtent → OLIKA hash.
        var a = FilterHashCalculator.Compute(Criteria(worktimeExtent: ["wt_heltid"]));
        var b = FilterHashCalculator.Compute(Criteria(worktimeExtent: ["wt_deltid"]));

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Compute_SameNewDimensionInputDifferentOrder_ProducesSameHash()
    {
        // (d) Listordnings-oberoende: VO:t normaliserar (sort+distinct) →
        // osorterad input ger samma 64-tecken-hex.
        var a = FilterHashCalculator.Compute(Criteria(
            employmentType: ["et_z", "et_a"], worktimeExtent: ["wt_y", "wt_x"]));
        var b = FilterHashCalculator.Compute(Criteria(
            employmentType: ["et_a", "et_z"], worktimeExtent: ["wt_x", "wt_y"]));

        a.ShouldBe(b);
        a.Length.ShouldBe(64);
    }

    [Fact]
    public void Compute_SameValueInEmploymentVsWorktimeDimension_ProducesDifferentHash()
    {
        // Dimension-förväxlingsgrind för de två nya nycklarna.
        var a = FilterHashCalculator.Compute(
            q: null, occupationGroup: [], municipality: [], region: [],
            employmentType: ["x1"], worktimeExtent: [],
            sortBy: JobAdSortBy.PublishedAtDesc);
        var b = FilterHashCalculator.Compute(
            q: null, occupationGroup: [], municipality: [], region: [],
            employmentType: [], worktimeExtent: ["x1"],
            sortBy: JobAdSortBy.PublishedAtDesc);

        a.ShouldNotBe(b);
    }
}
