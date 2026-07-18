using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.RecentJobSearches;

// FilterHashCalculator — deterministic SHA-256 över canonical-JSON av filter-shape.
// Canonical-formen (ADR 0067 Fas C2/B2, #311 PR-2b C1, #551 PR-D) är
// {"q":...,"occupationGroup":[...],"municipality":[...],"region":[...],
//  "employmentType":[...],"worktimeExtent":[...],"employer":[...],"remote":bool,"sortBy":int}
// — "ssyk"-nyckeln UTGÅR (C2). "employer" (org.nr) ligger mellan worktimeExtent och
// sortBy (#311 PR-2b C1); "remote" (distans, bool) ligger mellan employer och sortBy
// (#551 PR-D — skalär-svans före sortBy). Additivt format-bump, ingen hash-versionering
// (recent-raderna är efemär cache; cap-20-eviction självläker). Bär uniqueness-kontraktet
// UNIQUE(job_seeker_id, filter_hash) (ADR 0060).
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
        IEnumerable<string>? employer = null,
        bool remote = false,
        string? q = "backend",
        JobAdSortBy sortBy = JobAdSortBy.PublishedAtDesc) =>
        SearchCriteria.Create(
            occupationGroup: occupationGroup ?? ["grp1"],
            municipality: municipality ?? ["sthlm_kn"],
            region: region ?? ["stockholm"],
            employmentType: employmentType ?? ["et_fast"],
            worktimeExtent: worktimeExtent ?? ["wt_heltid"],
            employer: employer ?? ["5566010101"],
            remote: remote,
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
        // Låser canonical-form-KONTRAKTET (B2, #311 PR-2b C1, #551 PR-D): exakt
        // nyckelordning q → occupationGroup → municipality → region →
        // employmentType → worktimeExtent → employer → remote → sortBy. Om
        // Infrastructure/Domain ändrar serialisering tyst förlorar vi unique-index-
        // integritet — då ska detta test falla.
        // #311 PR-2b C1: "employer" ligger MELLAN worktimeExtent och sortBy.
        // #551 PR-D: "remote" (bool) ligger MELLAN employer och sortBy — skrivs OVILLKORLIGT
        // (som list-arrayerna, inte conditional). Ett ändrat läge/utelämnat fält bryter unique-
        // index-integriteten → detta test faller (den avsiktliga format-bump-beviskedjan).
        const string canonicalJson =
            """{"q":"backend","occupationGroup":["g1"],"municipality":["m1"],"region":["r1"],"employmentType":["e1"],"worktimeExtent":["w1"],"employer":["5560000001"],"remote":false,"sortBy":0}""";
        var expected = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));

        var actual = FilterHashCalculator.Compute(
            q: "backend",
            occupationGroup: ["g1"],
            municipality: ["m1"],
            region: ["r1"],
            employmentType: ["e1"],
            worktimeExtent: ["w1"],
            employer: ["5560000001"],
            remote: false,
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
            employer: criteria.Employer,
            remote: criteria.Remote,
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
            employmentType: [], worktimeExtent: [], employer: [], remote: false,
            sortBy: JobAdSortBy.PublishedAtDesc);
        var b = FilterHashCalculator.Compute(
            q: null, occupationGroup: [], municipality: ["x1"], region: [],
            employmentType: [], worktimeExtent: [], employer: [], remote: false,
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
            employmentType: [], worktimeExtent: [], employer: [], remote: false, sortBy: JobAdSortBy.PublishedAtDesc));
        Should.Throw<ArgumentNullException>(() => FilterHashCalculator.Compute(
            q: null, occupationGroup: [], municipality: null!, region: [],
            employmentType: [], worktimeExtent: [], employer: [], remote: false, sortBy: JobAdSortBy.PublishedAtDesc));
        Should.Throw<ArgumentNullException>(() => FilterHashCalculator.Compute(
            q: null, occupationGroup: [], municipality: [], region: null!,
            employmentType: [], worktimeExtent: [], employer: [], remote: false, sortBy: JobAdSortBy.PublishedAtDesc));
        // B2: de två nya list-params bär samma null-guard som de befintliga.
        Should.Throw<ArgumentNullException>(() => FilterHashCalculator.Compute(
            q: null, occupationGroup: [], municipality: [], region: [],
            employmentType: null!, worktimeExtent: [], employer: [], remote: false, sortBy: JobAdSortBy.PublishedAtDesc));
        Should.Throw<ArgumentNullException>(() => FilterHashCalculator.Compute(
            q: null, occupationGroup: [], municipality: [], region: [],
            employmentType: [], worktimeExtent: null!, employer: [], remote: false, sortBy: JobAdSortBy.PublishedAtDesc));
        // #311 PR-2b C1: employer-param bär samma null-guard. (remote är en bool → ingen null-guard.)
        Should.Throw<ArgumentNullException>(() => FilterHashCalculator.Compute(
            q: null, occupationGroup: [], municipality: [], region: [],
            employmentType: [], worktimeExtent: [], employer: null!, remote: false, sortBy: JobAdSortBy.PublishedAtDesc));
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
            employmentType: ["x1"], worktimeExtent: [], employer: [], remote: false,
            sortBy: JobAdSortBy.PublishedAtDesc);
        var b = FilterHashCalculator.Compute(
            q: null, occupationGroup: [], municipality: [], region: [],
            employmentType: [], worktimeExtent: ["x1"], employer: [], remote: false,
            sortBy: JobAdSortBy.PublishedAtDesc);

        a.ShouldNotBe(b);
    }

    // ===============================================================
    // #311 PR-2b C1 (ADR 0087 D6) — Employer (org.nr) ingår i hashen.
    // ===============================================================

    [Fact]
    public void Compute_DifferentEmployer_ProducesDifferentHash()
    {
        var a = FilterHashCalculator.Compute(Criteria(employer: ["5566010101"]));
        var b = FilterHashCalculator.Compute(Criteria(employer: ["5566020202"]));

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Compute_SameEmployerInputDifferentOrder_ProducesSameHash()
    {
        // VO:t normaliserar (sort+distinct) → osorterad org.nr-input ger samma hash.
        var a = FilterHashCalculator.Compute(Criteria(employer: ["5566020202", "5566010101"]));
        var b = FilterHashCalculator.Compute(Criteria(employer: ["5566010101", "5566020202"]));

        a.ShouldBe(b);
        a.Length.ShouldBe(64);
    }

    [Fact]
    public void Compute_SameValueInEmployerVsOtherDimension_ProducesDifferentHash()
    {
        // Dimension-förväxlingsgrind på hash-nivå: samma sträng som "employer" vs som
        // "occupationGroup" ger olika canonical-JSON-nyckel → olika hash (jsonb-dedupe-säkerhet).
        var a = FilterHashCalculator.Compute(
            q: null, occupationGroup: [], municipality: [], region: [],
            employmentType: [], worktimeExtent: [], employer: ["5566010101"], remote: false,
            sortBy: JobAdSortBy.PublishedAtDesc);
        var b = FilterHashCalculator.Compute(
            q: null, occupationGroup: ["5566010101"], municipality: [], region: [],
            employmentType: [], worktimeExtent: [], employer: [], remote: false,
            sortBy: JobAdSortBy.PublishedAtDesc);

        a.ShouldNotBe(b);
    }

    // ===============================================================
    // #551 PR-D (ADR 0087 D6-paritet) — Remote (distans, bool) ingår i hashen.
    // ===============================================================

    [Fact]
    public void Compute_DifferentRemote_ProducesDifferentHash()
    {
        // THE key guard: två criteria identiska utom Remote (true vs false) MÅSTE hasha olika.
        // Om produktionen glömde writer.WriteBoolean("remote", ...) skulle remote=true och
        // remote=false serialisera identiskt → hash-kollision → en distans-sökning dedupe:as onto
        // sin on-site-tvilling (UNIQUE(job_seeker_id, filter_hash)-identitetsläcka). Faller högt då.
        var a = FilterHashCalculator.Compute(Criteria(remote: true));
        var b = FilterHashCalculator.Compute(Criteria(remote: false));

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Compute_CriteriaOverload_ThreadsRemote()
    {
        // Single-arg Compute(criteria)-overloaden MÅSTE tråda criteria.Remote in i den explicita
        // overloaden (produktion: Compute(criteria) passar criteria.Remote). Bevisat åt båda håll:
        // en remote=true-criteria hashar identiskt med den explicita overloaden anropad remote:true,
        // och OLIKT den explicita overloaden anropad remote:false. Om single-arg-overloaden hårdkodade
        // remote:false (eller tappade fältet) skulle fromCriteria ≠ explicitTrue → faller.
        var criteria = Criteria(remote: true);

        var fromCriteria = FilterHashCalculator.Compute(criteria);
        var explicitTrue = FilterHashCalculator.Compute(
            q: criteria.Q,
            occupationGroup: criteria.OccupationGroup,
            municipality: criteria.Municipality,
            region: criteria.Region,
            employmentType: criteria.EmploymentType,
            worktimeExtent: criteria.WorktimeExtent,
            employer: criteria.Employer,
            remote: true,
            sortBy: criteria.SortBy);
        var explicitFalse = FilterHashCalculator.Compute(
            q: criteria.Q,
            occupationGroup: criteria.OccupationGroup,
            municipality: criteria.Municipality,
            region: criteria.Region,
            employmentType: criteria.EmploymentType,
            worktimeExtent: criteria.WorktimeExtent,
            employer: criteria.Employer,
            remote: false,
            sortBy: criteria.SortBy);

        fromCriteria.ShouldBe(explicitTrue);
        fromCriteria.ShouldNotBe(explicitFalse);
    }
}
