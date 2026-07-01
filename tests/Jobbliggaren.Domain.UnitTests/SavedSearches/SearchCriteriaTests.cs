using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.SavedSearches;

// C2 (ADR 0067 Platsbanken sök-paritet, CTO-dom 2026-06-09 (a)/(f) + architect F1):
// Ssyk-fältet UTGÅR ur VO:t (occupation-name avvecklas ur sök-identiteten);
// nya dimensioner OccupationGroup (ssyk-level-4/yrkesgrupp) + Municipality
// (kommun). Ny Create-signatur i kanonisk dimensionsordning (paritet med
// JobAdFilterCriteria-SPOT:en): Create(occupationGroup, municipality, region,
// q, sortBy). Per dimension upprätthålls ADR 0042 Beslut B:s fyra invarianter:
// (1) sorterad+distinct ordinal-normalisering, (2) MaxConceptIds-cap,
// (3) generaliserad tom-invariant, (4) per-element-regex.
//
// RÖD tills SearchCriteria.cs implementerar nya signaturen + dimensionerna.
// Kompilerar mot mål-API:t (annars blockeras impl-bygget).
public class SearchCriteriaTests
{
    // Helper — named arguments obligatoriskt (nu FEM likatypade listor i rad,
    // architect F1-disciplin). B2 (ADR 0067 Beslut 6/7): EmploymentType
    // (anställningsform) + WorktimeExtent (omfattning) tillkommer som de fjärde/
    // femte list-dimensionerna i kanonisk dimensionsordning EFTER region, FÖRE q.
    private static Result<SearchCriteria> Create(
        IEnumerable<string>? occupationGroup = null,
        IEnumerable<string>? municipality = null,
        IEnumerable<string>? region = null,
        IEnumerable<string>? employmentType = null,
        IEnumerable<string>? worktimeExtent = null,
        IEnumerable<string>? employer = null,
        string? q = null,
        JobAdSortBy sortBy = JobAdSortBy.PublishedAtDesc) =>
        SearchCriteria.Create(
            occupationGroup: occupationGroup,
            municipality: municipality,
            region: region,
            employmentType: employmentType,
            worktimeExtent: worktimeExtent,
            employer: employer,
            q: q,
            sortBy: sortBy);

    // ---------------------------------------------------------------
    // Happy path + minst-ett-kriterium (per dimension)
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithOccupationGroupOnly_ReturnsSuccess()
    {
        var result = Create(occupationGroup: ["grp_12345"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OccupationGroup.ShouldBe(["grp_12345"]);
        result.Value.Municipality.ShouldBeEmpty();
        result.Value.Region.ShouldBeEmpty();
        result.Value.Q.ShouldBeNull();
        result.Value.SortBy.ShouldBe(JobAdSortBy.PublishedAtDesc);
    }

    [Fact]
    public void Create_WithMunicipalityOnly_ReturnsSuccess()
    {
        var result = Create(municipality: ["sthlm_kn"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Municipality.ShouldBe(["sthlm_kn"]);
        result.Value.OccupationGroup.ShouldBeEmpty();
        result.Value.Region.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithRegionOnly_ReturnsSuccess()
    {
        var result = Create(region: ["stockholm_AB"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Region.ShouldBe(["stockholm_AB"]);
        result.Value.OccupationGroup.ShouldBeEmpty();
        result.Value.Municipality.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithQOnly_ReturnsSuccess()
    {
        var result = Create(q: "backend");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Q.ShouldBe("backend");
        result.Value.OccupationGroup.ShouldBeEmpty();
        result.Value.Municipality.ShouldBeEmpty();
        result.Value.Region.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithAllCriteria_ReturnsSuccess()
    {
        var result = Create(
            occupationGroup: ["grp1", "grp2"],
            municipality: ["sthlm_kn", "uppsala_kn"],
            region: ["stockholm", "uppsala"],
            q: "backend",
            sortBy: JobAdSortBy.ExpiresAtAsc);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OccupationGroup.ShouldBe(["grp1", "grp2"]);
        result.Value.Municipality.ShouldBe(["sthlm_kn", "uppsala_kn"]);
        result.Value.Region.ShouldBe(["stockholm", "uppsala"]);
        result.Value.Q.ShouldBe("backend");
        result.Value.SortBy.ShouldBe(JobAdSortBy.ExpiresAtAsc);
    }

    [Fact]
    public void Create_WithMultiOccupationGroup_ReturnsSuccess()
    {
        // Genuint produktbehov (ADR 0042/0067): OR-bevakning över yrkesgrupper.
        var result = Create(occupationGroup: ["grp_system", "grp_frontend"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OccupationGroup.Count.ShouldBe(2);
    }

    // ---------------------------------------------------------------
    // C2 (f) — Ssyk-fältet UTGÅR ur VO:t. Reflektionsgrind: ett återinfört
    // Ssyk-fält vore en död dimension i sök-identiteten (Evans kap. 2/5).
    // ---------------------------------------------------------------

    [Fact]
    public void SearchCriteria_HasNoSsykProperty_AfterC2()
    {
        typeof(SearchCriteria).GetProperty("Ssyk").ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // Invariant 1 — Normalisering: trim + drop tom/whitespace + distinct
    //                ordinal + sorterad ordinal (per dimension)
    // ---------------------------------------------------------------

    [Fact]
    public void Create_NormalizesOccupationGroup_SortedDistinctOrdinal()
    {
        var result = Create(occupationGroup: ["b", "a", "b", " c "]);

        result.IsSuccess.ShouldBeTrue();
        // distinct + sorterad ordinal + trim
        result.Value.OccupationGroup.ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public void Create_NormalizesMunicipality_SortedDistinctOrdinal()
    {
        var result = Create(municipality: ["uppsala_kn", "sthlm_kn", "uppsala_kn"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Municipality.ShouldBe(["sthlm_kn", "uppsala_kn"]);
    }

    [Fact]
    public void Create_NormalizesRegion_SortedDistinctOrdinal()
    {
        var result = Create(region: ["uppsala", "stockholm", "uppsala"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Region.ShouldBe(["stockholm", "uppsala"]);
    }

    [Fact]
    public void Create_DropsEmptyAndWhitespaceElements()
    {
        // Tomma/whitespace-element droppas; minst ett giltigt kvar → success.
        var result = Create(occupationGroup: ["grp1", "", "   ", "grp2"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OccupationGroup.ShouldBe(["grp1", "grp2"]);
    }

    [Fact]
    public void Create_OrdinalSort_IsCaseSensitive()
    {
        // Ordinal-sortering: versaler före gemener (ASCII). Säkerställer
        // deterministisk ordning för jsonb-dedupe oavsett input-ordning.
        var a = Create(occupationGroup: ["zebra", "Apple"]).Value;
        var b = Create(occupationGroup: ["Apple", "zebra"]).Value;

        a.OccupationGroup.ShouldBe(b.OccupationGroup);
        // ordinal: 'A' (65) < 'z' (122)
        a.OccupationGroup[0].ShouldBe("Apple");
    }

    // ---------------------------------------------------------------
    // Invariant 2 — Equality strukturell (SequenceEqual ordinal) i kanonisk
    //                ordning (OccupationGroup, Municipality, Region).
    //                SavedSearch jsonb-dedupe-grunden.
    // ---------------------------------------------------------------

    [Fact]
    public void TwoCriteria_SameElementsDifferentOrder_AreValueEqual()
    {
        // KRITISKT: jsonb-dedupe vilar på detta. Normalisering gör att samma
        // element i olika ordning producerar strukturellt lika VO:n.
        var a = Create(
            occupationGroup: ["b", "a"], municipality: ["n", "m"],
            region: ["x", "y"], q: "backend").Value;
        var b = Create(
            occupationGroup: ["a", "b"], municipality: ["m", "n"],
            region: ["y", "x"], q: "backend").Value;

        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ShouldBe(b);
    }

    [Fact]
    public void TwoCriteria_DifferentOccupationGroupElements_AreNotValueEqual()
    {
        var a = Create(occupationGroup: ["grp1"]).Value;
        var b = Create(occupationGroup: ["grp9"]).Value;

        a.Equals(b).ShouldBeFalse();
        (a == b).ShouldBeFalse();
        a.ShouldNotBe(b);
    }

    [Fact]
    public void TwoCriteria_DifferentMunicipalityElements_AreNotValueEqual()
    {
        var a = Create(municipality: ["sthlm_kn"]).Value;
        var b = Create(municipality: ["uppsala_kn"]).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void TwoCriteria_DifferentOccupationGroupCount_AreNotValueEqual()
    {
        var a = Create(occupationGroup: ["grp1"]).Value;
        var b = Create(occupationGroup: ["grp1", "grp2"]).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void TwoCriteria_SameValueInDifferentDimension_AreNotValueEqual()
    {
        // Dimension-förväxlingsgrind: samma concept-id i OLIKA dimensioner får
        // ALDRIG vara lika (annars dedupe:ar jsonb fel sökning bort).
        var a = Create(occupationGroup: ["x1"]).Value;
        var b = Create(municipality: ["x1"]).Value;
        var c = Create(region: ["x1"]).Value;

        a.ShouldNotBe(b);
        b.ShouldNotBe(c);
        a.ShouldNotBe(c);
    }

    [Fact]
    public void CriteriaDifferingOnlyByQ_AreNotValueEqual()
    {
        var a = Create(occupationGroup: ["grp1"], q: "backend").Value;
        var b = Create(occupationGroup: ["grp1"], q: "frontend").Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void CriteriaDifferingOnlyBySortBy_AreNotValueEqual()
    {
        // SortBy ingår i identiteten (ADR 0039 Beslut 3 hålls).
        var a = Create(occupationGroup: ["grp1"], sortBy: JobAdSortBy.PublishedAtDesc).Value;
        var b = Create(occupationGroup: ["grp1"], sortBy: JobAdSortBy.PublishedAtAsc).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void TrimNormalizedCriteria_AreValueEqualToUntrimmed()
    {
        var a = Create(occupationGroup: ["  grp1  "]).Value;
        var b = Create(occupationGroup: ["grp1"]).Value;

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void DuplicateElements_NormalizeToSame_AreValueEqual()
    {
        var a = Create(municipality: ["sthlm_kn", "sthlm_kn"]).Value;
        var b = Create(municipality: ["sthlm_kn"]).Value;

        a.ShouldBe(b);
    }

    // ---------------------------------------------------------------
    // Invariant 3 — Maxantal-cap = MaxConceptIds per lista (Domain-konstant).
    // Boundary-testerna refererar konstanten, ALDRIG literalen 400, så
    // testerna följer med om MaxConceptIds ändras igen (DRY, CLAUDE.md §5.1).
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithExactlyMaxOccupationGroup_ReturnsSuccess()
    {
        var max = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"grp{i}").ToArray();

        var result = Create(occupationGroup: max);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OccupationGroup.Count.ShouldBe(SearchCriteria.MaxConceptIds);
    }

    [Fact]
    public void Create_WithOneOverMaxOccupationGroup_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"grp{i}").ToArray();

        var result = Create(occupationGroup: overMax);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.TooManyOccupationGroup");
        // Copy är kontrakt per architect F1-tabellen (speglar
        // ListJobAdsQueryValidator — ingen ny copy uppfinns).
        result.Error.Message.ShouldBe(
            $"Max {SearchCriteria.MaxConceptIds} yrkesgrupper per sökning.");
    }

    [Fact]
    public void Create_WithExactlyMaxMunicipality_ReturnsSuccess()
    {
        var max = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"kn{i}").ToArray();

        var result = Create(municipality: max);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Municipality.Count.ShouldBe(SearchCriteria.MaxConceptIds);
    }

    [Fact]
    public void Create_WithOneOverMaxMunicipality_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"kn{i}").ToArray();

        var result = Create(municipality: overMax);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.TooManyMunicipality");
        result.Error.Message.ShouldBe(
            $"Max {SearchCriteria.MaxConceptIds} kommuner per sökning.");
    }

    [Fact]
    public void Create_WithOneOverMaxRegion_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"reg{i}").ToArray();

        var result = Create(region: overMax);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.TooManyRegion");
    }

    [Fact]
    public void Create_CapIsFourHundred_AfterC1Raise()
    {
        // C1 (ADR 0067) låser den nya cap-nivån. Self-dokumenterande grind:
        // bevisar att 10→400-höjningen faktiskt skett (om någon råkar sänka
        // tillbaka konstanten faller detta test).
        SearchCriteria.MaxConceptIds.ShouldBe(400);
    }

    [Fact]
    public void Create_CapAppliesAfterDistinct_MaxPlusOneWithDuplicateUnderCap_ReturnsSuccess()
    {
        // MaxConceptIds+1 råelement varav 1 dubblett → MaxConceptIds distinkta
        // → under cap → success (cap appliceras EFTER distinct-normaliseringen).
        var raw = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"grp{i}").ToList();
        raw.Add("grp1"); // dubblett

        var result = Create(occupationGroup: raw);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OccupationGroup.Count.ShouldBe(SearchCriteria.MaxConceptIds);
    }

    // ---------------------------------------------------------------
    // Invariant 4 — Tom-invariant generaliserad: tomma listor (alla tre) +
    //                null/whitespace Q → SearchCriteria.Empty.
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithAllNull_ReturnsEmptyFailure()
    {
        var result = Create();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.Empty");
        // Copy-kontrakt per architect F1. B2 (ADR 0067 Beslut 6/7): meddelandet
        // nämner nu även anställningsform + omfattning (de två nya dimensionerna).
        result.Error.Message.ShouldBe(
            "Minst ett sökkriterium (yrkesgrupp, kommun, region, anställningsform, omfattning, arbetsgivare eller fritext) krävs.");
    }

    [Fact]
    public void Create_WithEmptyListsAndNullQ_ReturnsEmptyFailure()
    {
        var result = Create(occupationGroup: [], municipality: [], region: [], q: null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.Empty");
    }

    [Fact]
    public void Create_WithOnlyWhitespaceElementsAndWhitespaceQ_ReturnsEmptyFailure()
    {
        // Tom efter normalisering (alla element whitespace) + whitespace Q
        // = inget filter = SearchCriteria.Empty.
        var result = Create(
            occupationGroup: ["", "  "], municipality: [" "], region: [" "], q: "   ");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.Empty");
    }

    [Fact]
    public void Create_WithEmptyListsButQNonNull_ReturnsSuccess()
    {
        var result = Create(occupationGroup: [], municipality: [], region: [], q: "backend");

        result.IsSuccess.ShouldBeTrue();
        result.Value.OccupationGroup.ShouldBeEmpty();
        result.Value.Municipality.ShouldBeEmpty();
        result.Value.Region.ShouldBeEmpty();
        result.Value.Q.ShouldBe("backend");
    }

    [Fact]
    public void Create_WithOneNonEmptyListAndNullQ_ReturnsSuccess()
    {
        var result = Create(occupationGroup: ["grp1"], municipality: [], region: []);

        result.IsSuccess.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // Invariant 5 — Per-element regex ^[A-Za-z0-9_-]{1,32}$ per dimension
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    [InlineData("semi;colon")]
    [InlineData("dot.notation")]
    [InlineData("plus+sign")]
    [InlineData("123456789012345678901234567890123")] // 33 tecken > 32
    public void Create_WithInvalidOccupationGroupElement_ReturnsFailure(string bad)
    {
        // Ett ogiltigt element bland giltiga → hela Create faller.
        var result = Create(occupationGroup: ["grp1", bad]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidOccupationGroup");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    [InlineData("123456789012345678901234567890123")]
    public void Create_WithInvalidMunicipalityElement_ReturnsFailure(string bad)
    {
        var result = Create(municipality: ["sthlm_kn", bad]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidMunicipality");
    }

    [Theory]
    [InlineData("region space")]
    [InlineData("åäö")]
    [InlineData("123456789012345678901234567890123")]
    public void Create_WithInvalidRegionElement_ReturnsFailure(string bad)
    {
        var result = Create(region: ["stockholm", bad]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidRegion");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ABC-123_xyz")]
    [InlineData("12345678901234567890123456789012")] // exakt 32 tecken
    public void Create_WithValidOccupationGroupElementFormat_ReturnsSuccess(string group)
    {
        var result = Create(occupationGroup: [group]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OccupationGroup.ShouldBe([group]);
    }

    // ---------------------------------------------------------------
    // Q oförändrat — 2-100 tecken-regeln kvar
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithQTooShort_ReturnsFailure()
    {
        var result = Create(q: "a");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidQ");
    }

    [Fact]
    public void Create_WithQTooLong_ReturnsFailure()
    {
        var result = Create(q: new string('x', 101));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidQ");
    }

    [Theory]
    [InlineData("ab")]                  // exakt min 2
    [InlineData("backend developer")]
    public void Create_WithQAtBoundaries_ReturnsSuccess(string q)
    {
        var result = Create(q: q);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Q.ShouldBe(q);
    }

    [Fact]
    public void Create_WithQAtMaxLength_ReturnsSuccess()
    {
        var result = Create(q: new string('x', 100));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Q!.Length.ShouldBe(100);
    }

    // ---------------------------------------------------------------
    // SortBy — Enum.IsDefined (oförändrat)
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithUndefinedSortBy_ReturnsFailure()
    {
        var result = Create(occupationGroup: ["grp1"], sortBy: (JobAdSortBy)999);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidSortBy");
    }

    // ---------------------------------------------------------------
    // ADR 0042 Beslut D — Relevance kräver q (relevans utan söktext odefinierad)
    // ---------------------------------------------------------------

    [Fact]
    public void Create_RelevanceSortWithoutQ_ReturnsFailure()
    {
        var result = Create(occupationGroup: ["grp1"], sortBy: JobAdSortBy.Relevance);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.RelevanceRequiresQ");
    }

    [Fact]
    public void Create_RelevanceSortWithQ_ReturnsSuccess()
    {
        var result = Create(q: "backend", sortBy: JobAdSortBy.Relevance);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SortBy.ShouldBe(JobAdSortBy.Relevance);
        result.Value.Q.ShouldBe("backend");
    }

    // ===============================================================
    // B2 (ADR 0067 Beslut 6/7 Fas B2) — EmploymentType (anställningsform)
    // + WorktimeExtent (omfattning) som nya list-dimensioner. Samma fyra
    // ADR 0042 Beslut B-invarianter som de befintliga listorna; nya felkoder
    // SearchCriteria.{TooMany,Invalid}{EmploymentType,WorktimeExtent}.
    // RÖD tills SearchCriteria.cs har de två nya properties + Create-paramen.
    // ===============================================================

    // --- Happy path + tom-invariant (minst EN av fem listor ELLER Q) ---

    [Fact]
    public void Create_WithEmploymentTypeOnly_ReturnsSuccess()
    {
        // Tom-invarianten utökad: enbart EmploymentType (alla andra tomma, Q
        // null) räcker → giltig sökning.
        var result = Create(employmentType: ["et_fast"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.EmploymentType.ShouldBe(["et_fast"]);
        result.Value.WorktimeExtent.ShouldBeEmpty();
        result.Value.OccupationGroup.ShouldBeEmpty();
        result.Value.Municipality.ShouldBeEmpty();
        result.Value.Region.ShouldBeEmpty();
        result.Value.Q.ShouldBeNull();
    }

    [Fact]
    public void Create_WithWorktimeExtentOnly_ReturnsSuccess()
    {
        var result = Create(worktimeExtent: ["wt_heltid"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WorktimeExtent.ShouldBe(["wt_heltid"]);
        result.Value.EmploymentType.ShouldBeEmpty();
    }

    [Fact]
    public void Create_NewDimensions_DefaultToEmpty_WhenNotSupplied()
    {
        // De två nya listorna defaultar till [] (aldrig null) precis som de
        // befintliga list-dimensionerna.
        var result = Create(occupationGroup: ["grp1"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.EmploymentType.ShouldBeEmpty();
        result.Value.WorktimeExtent.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithAllFiveDimensionsAndQ_ReturnsSuccess()
    {
        var result = Create(
            occupationGroup: ["grp1"],
            municipality: ["sthlm_kn"],
            region: ["stockholm"],
            employmentType: ["et_fast", "et_vikariat"],
            worktimeExtent: ["wt_heltid"],
            q: "backend");

        result.IsSuccess.ShouldBeTrue();
        result.Value.EmploymentType.ShouldBe(["et_fast", "et_vikariat"]);
        result.Value.WorktimeExtent.ShouldBe(["wt_heltid"]);
    }

    // --- Invariant 1 — normalisering (trim/distinct/sort ordinal) ---

    [Fact]
    public void Create_NormalizesEmploymentType_SortedDistinctOrdinal()
    {
        var result = Create(employmentType: ["b", "a", "b", " c "]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.EmploymentType.ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public void Create_NormalizesWorktimeExtent_SortedDistinctOrdinal()
    {
        var result = Create(worktimeExtent: ["wt_deltid", "wt_heltid", "wt_deltid"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WorktimeExtent.ShouldBe(["wt_deltid", "wt_heltid"]);
    }

    // --- Invariant 2 — equality inkluderar de två nya listorna ---

    [Fact]
    public void TwoCriteria_DifferentEmploymentType_AreNotValueEqual()
    {
        var a = Create(employmentType: ["et_fast"]).Value;
        var b = Create(employmentType: ["et_vikariat"]).Value;

        a.Equals(b).ShouldBeFalse();
        (a == b).ShouldBeFalse();
        a.ShouldNotBe(b);
    }

    [Fact]
    public void TwoCriteria_DifferentWorktimeExtent_AreNotValueEqual()
    {
        var a = Create(worktimeExtent: ["wt_heltid"]).Value;
        var b = Create(worktimeExtent: ["wt_deltid"]).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void TwoCriteria_SameValueInEmploymentVsWorktimeDimension_AreNotValueEqual()
    {
        // Dimension-förväxlingsgrind: samma concept-id i de TVÅ nya dimensionerna
        // får aldrig vara lika (jsonb-dedupe-säkerhet).
        var a = Create(employmentType: ["x1"]).Value;
        var b = Create(worktimeExtent: ["x1"]).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void TwoCriteria_NewDimensionsSameElementsDifferentOrder_AreValueEqual()
    {
        // Normalisering → strukturell likhet trots input-ordning (jsonb-dedupe).
        var a = Create(
            employmentType: ["b", "a"], worktimeExtent: ["y", "x"]).Value;
        var b = Create(
            employmentType: ["a", "b"], worktimeExtent: ["x", "y"]).Value;

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void TwoCriteria_IdenticalIncludingNewDimensions_AreValueEqual()
    {
        var a = Create(
            occupationGroup: ["grp1"], employmentType: ["et_fast"],
            worktimeExtent: ["wt_heltid"], q: "backend").Value;
        var b = Create(
            occupationGroup: ["grp1"], employmentType: ["et_fast"],
            worktimeExtent: ["wt_heltid"], q: "backend").Value;

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    // --- Invariant 3 — maxantal-cap per ny lista (MaxConceptIds) ---

    [Fact]
    public void Create_WithExactlyMaxEmploymentType_ReturnsSuccess()
    {
        var max = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"et{i}").ToArray();

        var result = Create(employmentType: max);

        result.IsSuccess.ShouldBeTrue();
        result.Value.EmploymentType.Count.ShouldBe(SearchCriteria.MaxConceptIds);
    }

    [Fact]
    public void Create_WithOneOverMaxEmploymentType_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"et{i}").ToArray();

        var result = Create(employmentType: overMax);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.TooManyEmploymentType");
    }

    [Fact]
    public void Create_WithOneOverMaxWorktimeExtent_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"wt{i}").ToArray();

        var result = Create(worktimeExtent: overMax);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.TooManyWorktimeExtent");
    }

    // --- Invariant 4 — tom-invariant generaliserad till FEM listor ---

    [Fact]
    public void Create_WithAllFiveListsEmptyAndNullQ_ReturnsEmptyFailure()
    {
        var result = Create(
            occupationGroup: [], municipality: [], region: [],
            employmentType: [], worktimeExtent: [], q: null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.Empty");
    }

    [Fact]
    public void Create_WithOnlyEmploymentTypeAndEmptyOthers_ReturnsSuccess()
    {
        var result = Create(
            occupationGroup: [], municipality: [], region: [],
            employmentType: ["et_fast"], worktimeExtent: []);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_WithOnlyWorktimeExtentAndEmptyOthers_ReturnsSuccess()
    {
        var result = Create(
            occupationGroup: [], municipality: [], region: [],
            employmentType: [], worktimeExtent: ["wt_heltid"]);

        result.IsSuccess.ShouldBeTrue();
    }

    // --- Invariant 5 — per-element-regex ^[A-Za-z0-9_-]{1,32}$ ---

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    [InlineData("dot.notation")]
    [InlineData("123456789012345678901234567890123")] // 33 tecken
    public void Create_WithInvalidEmploymentTypeElement_ReturnsFailure(string bad)
    {
        var result = Create(employmentType: ["et_fast", bad]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidEmploymentType");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    [InlineData("123456789012345678901234567890123")]
    public void Create_WithInvalidWorktimeExtentElement_ReturnsFailure(string bad)
    {
        var result = Create(worktimeExtent: ["wt_heltid", bad]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidWorktimeExtent");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ABC-123_xyz")]
    [InlineData("12345678901234567890123456789012")] // exakt 32 tecken
    public void Create_WithValidEmploymentTypeElementFormat_ReturnsSuccess(string et)
    {
        var result = Create(employmentType: [et]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.EmploymentType.ShouldBe([et]);
    }

    // ===============================================================
    // #311 PR-2b C1 (ADR 0087 D6) — Employer (org.nr) som SJÄTTE list-dimension.
    // Samma ADR 0042 Beslut B-invarianter (normalisering, cap, tom-invariant,
    // equality/hash) MEN med org.nr-FORMAT (exakt 10 siffror) i stället för
    // ConceptIdPattern — arbetsgivar-nyckeln är ett organisationsnummer, inte en
    // JobTech concept-id. Nya felkoder SearchCriteria.{TooMany,Invalid}Employer.
    // ===============================================================

    [Fact]
    public void Create_WithEmployerOnly_ReturnsSuccess()
    {
        // Tom-invarianten utökad: enbart Employer (alla andra tomma, Q null) räcker
        // → giltig sökning (CTO-bind D3: employer-only är en giltig sökning).
        var result = Create(employer: ["5566010101"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Employer.ShouldBe(["5566010101"]);
        result.Value.OccupationGroup.ShouldBeEmpty();
        result.Value.EmploymentType.ShouldBeEmpty();
        result.Value.Q.ShouldBeNull();
    }

    [Fact]
    public void Create_Employer_DefaultsToEmpty_WhenNotSupplied()
    {
        var result = Create(occupationGroup: ["grp1"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Employer.ShouldBeEmpty();
    }

    [Fact]
    public void Create_NormalizesEmployer_SortedDistinctOrdinal()
    {
        var result = Create(employer: ["5566020202", "5566010101", "5566020202", " 5566030303 "]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Employer.ShouldBe(["5566010101", "5566020202", "5566030303"]);
    }

    [Fact]
    public void TwoCriteria_DifferentEmployer_AreNotValueEqual()
    {
        var a = Create(employer: ["5566010101"]).Value;
        var b = Create(employer: ["5566020202"]).Value;

        a.Equals(b).ShouldBeFalse();
        (a == b).ShouldBeFalse();
        a.ShouldNotBe(b);
    }

    [Fact]
    public void TwoCriteria_SameEmployerDifferentOrder_AreValueEqual()
    {
        // Normalisering → strukturell likhet trots input-ordning (jsonb-dedupe).
        var a = Create(employer: ["5566020202", "5566010101"]).Value;
        var b = Create(employer: ["5566010101", "5566020202"]).Value;

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void TwoCriteria_SameValueInEmployerVsOtherDimension_AreNotValueEqual()
    {
        // Dimension-förväxlingsgrind: ett org.nr som råkar vara alfanumeriskt-giltigt
        // som concept-id i en annan dimension får aldrig dedupe:a fel sökning bort.
        var a = Create(employer: ["5566010101"]).Value;
        var b = Create(occupationGroup: ["5566010101"]).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Create_WithExactlyMaxEmployer_ReturnsSuccess()
    {
        // Distinkta 10-siffriga org.nr (5-prefix + 9-siffrig löpande, alltid 10 siffror).
        var max = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"5{i:D9}").ToArray();

        var result = Create(employer: max);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Employer.Count.ShouldBe(SearchCriteria.MaxConceptIds);
    }

    [Fact]
    public void Create_WithOneOverMaxEmployer_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"5{i:D9}").ToArray();

        var result = Create(employer: overMax);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.TooManyEmployer");
        result.Error.Message.ShouldBe($"Max {SearchCriteria.MaxConceptIds} arbetsgivare per sökning.");
    }

    [Theory]
    [InlineData("123")]              // för kort
    [InlineData("55660101011")]      // 11 siffror
    [InlineData("55660A0101")]       // ej rena siffror
    [InlineData("grp_12345")]        // giltig concept-id men INTE ett org.nr
    [InlineData("556601-0101")]      // bindestreck (org.nr lagras utan)
    public void Create_WithInvalidEmployerElement_ReturnsFailure(string bad)
    {
        // org.nr-formatet (exakt 10 siffror) skiljer sig från ConceptIdPattern —
        // ett giltigt concept-id är INTE nödvändigtvis ett giltigt org.nr.
        var result = Create(employer: ["5566010101", bad]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidEmployer");
    }

    [Theory]
    [InlineData("5566010101")]
    [InlineData("0000000000")]
    [InlineData("9999999999")]
    public void Create_WithValidEmployerFormat_ReturnsSuccess(string orgNr)
    {
        var result = Create(employer: [orgNr]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Employer.ShouldBe([orgNr]);
    }
}
