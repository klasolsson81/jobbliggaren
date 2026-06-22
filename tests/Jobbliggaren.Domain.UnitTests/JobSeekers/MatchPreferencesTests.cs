using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

// F4-12 (CTO-frozen) — MatchPreferences VO på JobSeeker-aggregatet bär
// användarens STATED jobbsöks-preferenser (yrkesgrupper, regioner,
// anställningsformer). Speglar SearchCriteria:s normalisering + strukturella
// likhet (ADR 0042 Beslut B) för jsonb-collection-equality, MED ETT MEDVETET
// AVSTEG: tom-invarianten finns INTE här — alla tre listorna tomma är en
// GILTIG MatchPreferences (en användare som ännu inte angett preferenser).
// Per-element-format + per-list-cap återanvänder SearchCriteria-kontraktet
// (concept-id-regex ^[A-Za-z0-9_-]{1,32}$, MaxConceptIds = 400).
//
// RÖD tills MatchPreferences.cs implementeras (typen finns inte ännu) — TDD-
// RED. Kompilerar mot mål-API:t (Create-signatur + properties) så att impl-
// bygget blockeras tills produktionstypen finns.
//
// ANTAGANDE (att verifiera av Klas/impl): MatchPreferences.Create returnerar
// Result<MatchPreferences> med DomainError (speglar SearchCriteria.Create
// exakt) och felkoder med prefix "MatchPreferences." per dimension. Om impl
// väljer en annan signalering (t.ex. throw) faller dessa tester och signalen
// är att kontraktet behöver bekräftas.
public class MatchPreferencesTests
{
    // Helper — named args obligatoriskt (fyra likatypade listor i rad,
    // architect-disciplin speglad från SearchCriteriaTests).
    // Spår 3 PR-A (ADR 0076-amendment): preferredMunicipalities är en 4:e
    // OPTIONAL peer-dimension (default null → tom). Befintliga 3-arg-anrop nedan
    // kompilerar oförändrat.
    private static Result<MatchPreferences> Create(
        IEnumerable<string>? preferredOccupationGroups = null,
        IEnumerable<string>? preferredRegions = null,
        IEnumerable<string>? preferredEmploymentTypes = null,
        IEnumerable<string>? preferredMunicipalities = null,
        IEnumerable<string>? preferredSkills = null,
        int? experienceYears = null) =>
        MatchPreferences.Create(
            preferredOccupationGroups: preferredOccupationGroups,
            preferredRegions: preferredRegions,
            preferredEmploymentTypes: preferredEmploymentTypes,
            preferredMunicipalities: preferredMunicipalities,
            preferredSkills: preferredSkills,
            experienceYears: experienceYears);

    // ---------------------------------------------------------------
    // Happy path — varje dimension samt allihop
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithOccupationGroupsOnly_ReturnsSuccess()
    {
        var result = Create(preferredOccupationGroups: ["grp_12345"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBe(["grp_12345"]);
        result.Value.PreferredRegions.ShouldBeEmpty();
        result.Value.PreferredEmploymentTypes.ShouldBeEmpty();
        result.Value.PreferredMunicipalities.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithRegionsOnly_ReturnsSuccess()
    {
        var result = Create(preferredRegions: ["stockholm_AB"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredRegions.ShouldBe(["stockholm_AB"]);
        result.Value.PreferredOccupationGroups.ShouldBeEmpty();
        result.Value.PreferredEmploymentTypes.ShouldBeEmpty();
        result.Value.PreferredMunicipalities.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithEmploymentTypesOnly_ReturnsSuccess()
    {
        var result = Create(preferredEmploymentTypes: ["et_fast"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredEmploymentTypes.ShouldBe(["et_fast"]);
        result.Value.PreferredOccupationGroups.ShouldBeEmpty();
        result.Value.PreferredRegions.ShouldBeEmpty();
        result.Value.PreferredMunicipalities.ShouldBeEmpty();
    }

    // Spår 3 PR-A — kommun är en 4:e peer-dimension (parallell med de tre ovan).
    [Fact]
    public void Create_WithMunicipalitiesOnly_ReturnsSuccess()
    {
        var result = Create(preferredMunicipalities: ["sthlm_kn"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredMunicipalities.ShouldBe(["sthlm_kn"]);
        result.Value.PreferredOccupationGroups.ShouldBeEmpty();
        result.Value.PreferredRegions.ShouldBeEmpty();
        result.Value.PreferredEmploymentTypes.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithAllFourDimensions_ReturnsSuccess()
    {
        var result = Create(
            preferredOccupationGroups: ["grp1", "grp2"],
            preferredRegions: ["stockholm", "uppsala"],
            preferredEmploymentTypes: ["et_fast", "et_vikariat"],
            preferredMunicipalities: ["sthlm_kn", "uppsala_kn"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBe(["grp1", "grp2"]);
        result.Value.PreferredRegions.ShouldBe(["stockholm", "uppsala"]);
        result.Value.PreferredEmploymentTypes.ShouldBe(["et_fast", "et_vikariat"]);
        result.Value.PreferredMunicipalities.ShouldBe(["sthlm_kn", "uppsala_kn"]);
    }

    // ---------------------------------------------------------------
    // MEDVETET AVSTEG mot SearchCriteria — EMPTY ÄR GILTIGT.
    // Ingen "minst ett kriterium"-invariant. En användare utan angivna
    // preferenser har en giltig (tom) MatchPreferences.
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithAllEmpty_ReturnsSuccess()
    {
        var result = Create();

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBeEmpty();
        result.Value.PreferredRegions.ShouldBeEmpty();
        result.Value.PreferredEmploymentTypes.ShouldBeEmpty();
        result.Value.PreferredMunicipalities.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithAllExplicitlyEmptyLists_ReturnsSuccess()
    {
        var result = Create(
            preferredOccupationGroups: [],
            preferredRegions: [],
            preferredEmploymentTypes: []);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithOnlyWhitespaceElements_NormalizesToEmpty_AndStaysValid()
    {
        // Whitespace-element droppas i normaliseringen → tom lista → fortfarande
        // GILTIG (till skillnad från SearchCriteria.Empty).
        var result = Create(
            preferredOccupationGroups: ["", "  "],
            preferredRegions: [" "],
            preferredEmploymentTypes: ["\t"],
            preferredMunicipalities: ["  ", "\t"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBeEmpty();
        result.Value.PreferredRegions.ShouldBeEmpty();
        result.Value.PreferredEmploymentTypes.ShouldBeEmpty();
        result.Value.PreferredMunicipalities.ShouldBeEmpty();
    }

    [Fact]
    public void Empty_IsAValidNoneInstance_WithAllListsEmpty()
    {
        // ANTAGANDE: VO:t exponerar en statisk Empty/None-instans (parity med
        // hur SearchCriteria saknar en sådan men MatchPreferences behöver den
        // som honest "inga preferenser angivna"-default). Om impl väljer ett
        // annat namn faller detta och kontraktet behöver bekräftas.
        var none = MatchPreferences.Empty;

        none.PreferredOccupationGroups.ShouldBeEmpty();
        none.PreferredRegions.ShouldBeEmpty();
        none.PreferredEmploymentTypes.ShouldBeEmpty();
        // Spår 3 PR-A — Empty bär tom municipality-dimension.
        none.PreferredMunicipalities.ShouldBeEmpty();
    }

    [Fact]
    public void Empty_EqualsCreateWithAllEmpty()
    {
        MatchPreferences.Empty.ShouldBe(Create().Value);
    }

    // Spår 3 PR-A — det 4-arg-additiva kontraktet: Create(null,null,null) (3-arg-
    // formen, municipalities default) ger tom municipality-dimension och == Empty.
    [Fact]
    public void Create_WithThreeArgNullForm_HasEmptyMunicipalities_AndEqualsEmpty()
    {
        var result = MatchPreferences.Create(
            preferredOccupationGroups: null,
            preferredRegions: null,
            preferredEmploymentTypes: null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredMunicipalities.ShouldBeEmpty();
        result.Value.ShouldBe(MatchPreferences.Empty);
    }

    // ---------------------------------------------------------------
    // Normalisering — trim + droppa tom/whitespace + distinct ordinal +
    // sorterad ordinal (identiskt med SearchCriteria.NormalizeList).
    // ---------------------------------------------------------------

    [Fact]
    public void Create_NormalizesOccupationGroups_SortedDistinctOrdinal()
    {
        var result = Create(preferredOccupationGroups: ["b", "a", "b", " c "]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public void Create_NormalizesRegions_SortedDistinctOrdinal()
    {
        var result = Create(preferredRegions: ["uppsala", "stockholm", "uppsala"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredRegions.ShouldBe(["stockholm", "uppsala"]);
    }

    [Fact]
    public void Create_NormalizesEmploymentTypes_SortedDistinctOrdinal()
    {
        var result = Create(preferredEmploymentTypes: ["et_vikariat", "et_fast", "et_fast"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredEmploymentTypes.ShouldBe(["et_fast", "et_vikariat"]);
    }

    // Spår 3 PR-A — municipality normalisering identisk med övriga dimensioner:
    // trim per element, droppa tom/whitespace, distinct ordinal, sort ordinal.
    [Fact]
    public void Create_NormalizesMunicipalities_SortedDistinctOrdinal()
    {
        var result = Create(preferredMunicipalities: ["uppsala_kn", "sthlm_kn", "uppsala_kn", " gbg_kn "]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredMunicipalities.ShouldBe(["gbg_kn", "sthlm_kn", "uppsala_kn"]);
    }

    [Fact]
    public void Create_DropsEmptyAndWhitespaceMunicipalityElements_KeepsValidOnes()
    {
        var result = Create(preferredMunicipalities: ["sthlm_kn", "", "   ", "gbg_kn"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredMunicipalities.ShouldBe(["gbg_kn", "sthlm_kn"]);
    }

    [Fact]
    public void Create_DropsEmptyAndWhitespaceElements_KeepsValidOnes()
    {
        var result = Create(preferredOccupationGroups: ["grp1", "", "   ", "grp2"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBe(["grp1", "grp2"]);
    }

    [Fact]
    public void Create_OrdinalSort_IsCaseSensitive()
    {
        var a = Create(preferredOccupationGroups: ["zebra", "Apple"]).Value;
        var b = Create(preferredOccupationGroups: ["Apple", "zebra"]).Value;

        a.PreferredOccupationGroups.ShouldBe(b.PreferredOccupationGroups);
        // ordinal: 'A' (65) < 'z' (122)
        a.PreferredOccupationGroups[0].ShouldBe("Apple");
    }

    // ---------------------------------------------------------------
    // Strukturell likhet — Equals + GetHashCode i kanonisk dimensionsordning
    // (OccupationGroups, Regions, EmploymentTypes). Krävs för jsonb-collection-
    // equality (record + IReadOnlyList får annars referens-equality).
    // ---------------------------------------------------------------

    [Fact]
    public void TwoPreferences_SameElementsDifferentOrder_AreValueEqual()
    {
        var a = Create(
            preferredOccupationGroups: ["b", "a"],
            preferredRegions: ["y", "x"],
            preferredEmploymentTypes: ["et_b", "et_a"],
            preferredMunicipalities: ["kn_b", "kn_a"]).Value;
        var b = Create(
            preferredOccupationGroups: ["a", "b"],
            preferredRegions: ["x", "y"],
            preferredEmploymentTypes: ["et_a", "et_b"],
            preferredMunicipalities: ["kn_a", "kn_b"]).Value;

        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ShouldBe(b);
    }

    // Spår 3 PR-A — kommun ingår i strukturell likhet (ordinal sequence). Två
    // prefs identiska UTOM municipalities är INTE lika.
    [Fact]
    public void TwoPreferences_DifferentMunicipalities_AreNotValueEqual()
    {
        var a = Create(preferredMunicipalities: ["sthlm_kn"]).Value;
        var b = Create(preferredMunicipalities: ["gbg_kn"]).Value;

        a.Equals(b).ShouldBeFalse();
        (a == b).ShouldBeFalse();
        a.ShouldNotBe(b);
    }

    // Spår 3 PR-A — identiska municipalities (övriga dims lika) ÄR lika, inkl.
    // GetHashCode (ordinal sequence i kanonisk ordning, municipalities sist).
    [Fact]
    public void TwoPreferences_SameMunicipalities_AreValueEqual_IncludingHashCode()
    {
        var a = Create(
            preferredOccupationGroups: ["grp1"],
            preferredMunicipalities: ["sthlm_kn", "gbg_kn"]).Value;
        var b = Create(
            preferredOccupationGroups: ["grp1"],
            preferredMunicipalities: ["gbg_kn", "sthlm_kn"]).Value;

        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ShouldBe(b);
    }

    [Fact]
    public void TwoPreferences_DifferentOccupationGroups_AreNotValueEqual()
    {
        var a = Create(preferredOccupationGroups: ["grp1"]).Value;
        var b = Create(preferredOccupationGroups: ["grp9"]).Value;

        a.Equals(b).ShouldBeFalse();
        (a == b).ShouldBeFalse();
        a.ShouldNotBe(b);
    }

    [Fact]
    public void TwoPreferences_DifferentRegions_AreNotValueEqual()
    {
        var a = Create(preferredRegions: ["stockholm"]).Value;
        var b = Create(preferredRegions: ["uppsala"]).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void TwoPreferences_DifferentEmploymentTypes_AreNotValueEqual()
    {
        var a = Create(preferredEmploymentTypes: ["et_fast"]).Value;
        var b = Create(preferredEmploymentTypes: ["et_vikariat"]).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void TwoPreferences_SameValueInDifferentDimension_AreNotValueEqual()
    {
        // Dimension-förväxlingsgrind: samma concept-id i OLIKA dimensioner får
        // ALDRIG vara lika (jsonb-dedupe/equality-säkerhet). Spår 3 PR-A — kommun
        // är en 4:e ortogonal dimension i grinden.
        var a = Create(preferredOccupationGroups: ["x1"]).Value;
        var b = Create(preferredRegions: ["x1"]).Value;
        var c = Create(preferredEmploymentTypes: ["x1"]).Value;
        var d = Create(preferredMunicipalities: ["x1"]).Value;

        a.ShouldNotBe(b);
        b.ShouldNotBe(c);
        a.ShouldNotBe(c);
        d.ShouldNotBe(a);
        d.ShouldNotBe(b);
        d.ShouldNotBe(c);
    }

    // Spår 3 PR-A — independence: ett municipality-värde läcker ALDRIG in i
    // Regions/OccupationGroups/EmploymentTypes (de förblir tomma).
    [Fact]
    public void Create_WithMunicipalities_DoesNotLeakIntoOtherDimensions()
    {
        var result = Create(preferredMunicipalities: ["sthlm_kn", "gbg_kn"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredMunicipalities.ShouldBe(["gbg_kn", "sthlm_kn"]);
        result.Value.PreferredOccupationGroups.ShouldBeEmpty();
        result.Value.PreferredRegions.ShouldBeEmpty();
        result.Value.PreferredEmploymentTypes.ShouldBeEmpty();
    }

    [Fact]
    public void TrimNormalized_AreValueEqualToUntrimmed()
    {
        var a = Create(preferredOccupationGroups: ["  grp1  "]).Value;
        var b = Create(preferredOccupationGroups: ["grp1"]).Value;

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void DuplicateElements_NormalizeToSame_AreValueEqual()
    {
        var a = Create(preferredRegions: ["stockholm", "stockholm"]).Value;
        var b = Create(preferredRegions: ["stockholm"]).Value;

        a.ShouldBe(b);
    }

    // ---------------------------------------------------------------
    // Maxantal-cap = SearchCriteria.MaxConceptIds (ÅTERANVÄND KONSTANTEN —
    // refererar aldrig literalen 400, så testet följer med vid framtida ändring).
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithExactlyMaxOccupationGroups_ReturnsSuccess()
    {
        var max = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"grp{i}").ToArray();

        var result = Create(preferredOccupationGroups: max);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.Count.ShouldBe(SearchCriteria.MaxConceptIds);
    }

    [Fact]
    public void Create_WithOneOverMaxOccupationGroups_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"grp{i}").ToArray();

        var result = Create(preferredOccupationGroups: overMax);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.TooManyOccupationGroups");
    }

    [Fact]
    public void Create_WithOneOverMaxRegions_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"reg{i}").ToArray();

        var result = Create(preferredRegions: overMax);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.TooManyRegions");
    }

    [Fact]
    public void Create_WithOneOverMaxEmploymentTypes_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"et{i}").ToArray();

        var result = Create(preferredEmploymentTypes: overMax);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.TooManyEmploymentTypes");
    }

    // Spår 3 PR-A — municipality cap = SearchCriteria.MaxConceptIds (samma
    // återanvända konstant; refererar aldrig literalen).
    [Fact]
    public void Create_WithExactlyMaxMunicipalities_ReturnsSuccess()
    {
        var max = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"kn{i}").ToArray();

        var result = Create(preferredMunicipalities: max);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredMunicipalities.Count.ShouldBe(SearchCriteria.MaxConceptIds);
    }

    [Fact]
    public void Create_WithOneOverMaxMunicipalities_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"kn{i}").ToArray();

        var result = Create(preferredMunicipalities: overMax);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.TooManyMunicipalities");
    }

    [Fact]
    public void Create_CapAppliesAfterDistinct_MaxPlusOneWithDuplicateUnderCap_ReturnsSuccess()
    {
        // Cap appliceras EFTER distinct-normaliseringen (paritet SearchCriteria).
        var raw = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"grp{i}").ToList();
        raw.Add("grp1"); // dubblett

        var result = Create(preferredOccupationGroups: raw);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.Count.ShouldBe(SearchCriteria.MaxConceptIds);
    }

    // ---------------------------------------------------------------
    // Per-element regex ^[A-Za-z0-9_-]{1,32}$ per dimension (default-deny,
    // speglar SearchCriteria.ConceptIdPattern).
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
        var result = Create(preferredOccupationGroups: ["grp1", bad]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.InvalidOccupationGroup");
    }

    [Theory]
    [InlineData("region space")]
    [InlineData("åäö")]
    [InlineData("123456789012345678901234567890123")]
    public void Create_WithInvalidRegionElement_ReturnsFailure(string bad)
    {
        var result = Create(preferredRegions: ["stockholm", bad]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.InvalidRegion");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    [InlineData("dot.notation")]
    [InlineData("123456789012345678901234567890123")]
    public void Create_WithInvalidEmploymentTypeElement_ReturnsFailure(string bad)
    {
        var result = Create(preferredEmploymentTypes: ["et_fast", bad]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.InvalidEmploymentType");
    }

    // Spår 3 PR-A — municipality per-element regex ^[A-Za-z0-9_-]{1,32}$ (default-deny).
    [Theory]
    [InlineData("bad id!")]
    [InlineData("kommun space")]
    [InlineData("åäö")]
    [InlineData("dot.notation")]
    [InlineData("123456789012345678901234567890123")] // 33 tecken > 32
    public void Create_WithInvalidMunicipalityElement_ReturnsFailure(string bad)
    {
        var result = Create(preferredMunicipalities: ["sthlm_kn", bad]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.InvalidMunicipality");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ABC-123_xyz")]
    [InlineData("12345678901234567890123456789012")] // exakt 32 tecken
    public void Create_WithValidElementFormat_ReturnsSuccess(string conceptId)
    {
        var result = Create(preferredOccupationGroups: [conceptId]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBe([conceptId]);
    }

    // ===============================================================
    // STEG 3 (ADR 0079 Beslut 1) — PreferredSkills: a 5th concept-id
    // dimension, same contract as the four above (normalization, cap,
    // regex, equality). Empty stays valid.
    // ===============================================================

    [Fact]
    public void Create_WithSkillsOnly_ReturnsSuccess()
    {
        var result = Create(preferredSkills: ["skill_java"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredSkills.ShouldBe(["skill_java"]);
        result.Value.PreferredOccupationGroups.ShouldBeEmpty();
        result.Value.PreferredRegions.ShouldBeEmpty();
        result.Value.PreferredEmploymentTypes.ShouldBeEmpty();
        result.Value.PreferredMunicipalities.ShouldBeEmpty();
    }

    [Fact]
    public void Create_NormalizesSkills_SortedDistinctOrdinal()
    {
        var result = Create(preferredSkills: ["skill_spring", "skill_java", "skill_java", " skill_docker "]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredSkills.ShouldBe(["skill_docker", "skill_java", "skill_spring"]);
    }

    [Fact]
    public void Create_DropsEmptyAndWhitespaceSkillElements_KeepsValidOnes()
    {
        var result = Create(preferredSkills: ["skill_java", "", "   ", "skill_spring"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredSkills.ShouldBe(["skill_java", "skill_spring"]);
    }

    [Fact]
    public void Create_WithExactlyMaxSkills_ReturnsSuccess()
    {
        var max = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"sk{i}").ToArray();

        var result = Create(preferredSkills: max);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredSkills.Count.ShouldBe(SearchCriteria.MaxConceptIds);
    }

    [Fact]
    public void Create_WithOneOverMaxSkills_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"sk{i}").ToArray();

        var result = Create(preferredSkills: overMax);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.TooManySkills");
    }

    [Theory]
    [InlineData("skill space")]
    [InlineData("åäö")]
    [InlineData("dot.notation")]
    [InlineData("123456789012345678901234567890123")] // 33 tecken > 32
    public void Create_WithInvalidSkillElement_ReturnsFailure(string bad)
    {
        var result = Create(preferredSkills: ["skill_java", bad]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.InvalidSkill");
    }

    [Fact]
    public void Create_SkillsCapAppliesAfterDistinct_MaxPlusOneWithDuplicateUnderCap_ReturnsSuccess()
    {
        // Cap appliceras EFTER distinct-normaliseringen för skills också (samma
        // ordning som OccupationGroups ovan; bevisar att 5:e dimensionen följer
        // kontraktet, inte bara råräknar inputen).
        var raw = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"sk{i}").ToList();
        raw.Add("sk1"); // dubblett → distinct → exakt cap, inte över

        var result = Create(preferredSkills: raw);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredSkills.Count.ShouldBe(SearchCriteria.MaxConceptIds);
    }

    [Fact]
    public void Create_WithSkills_DoesNotLeakIntoOtherDimensions()
    {
        var result = Create(preferredSkills: ["skill_java", "skill_spring"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredSkills.ShouldBe(["skill_java", "skill_spring"]);
        result.Value.PreferredOccupationGroups.ShouldBeEmpty();
        result.Value.PreferredRegions.ShouldBeEmpty();
        result.Value.PreferredEmploymentTypes.ShouldBeEmpty();
        result.Value.PreferredMunicipalities.ShouldBeEmpty();
    }

    [Fact]
    public void TwoPreferences_DifferentSkills_AreNotValueEqual()
    {
        var a = Create(preferredSkills: ["skill_java"]).Value;
        var b = Create(preferredSkills: ["skill_python"]).Value;

        a.Equals(b).ShouldBeFalse();
        (a == b).ShouldBeFalse();
        a.ShouldNotBe(b);
    }

    [Fact]
    public void TwoPreferences_SameSkills_AreValueEqual_IncludingHashCode()
    {
        var a = Create(
            preferredOccupationGroups: ["grp1"],
            preferredSkills: ["skill_java", "skill_docker"]).Value;
        var b = Create(
            preferredOccupationGroups: ["grp1"],
            preferredSkills: ["skill_docker", "skill_java"]).Value;

        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ShouldBe(b);
    }

    // Dimension-förväxlingsgrind utökad: samma concept-id som SKILL vs övriga
    // dimensioner får ALDRIG vara lika (jsonb-equality-säkerhet).
    [Fact]
    public void SameValueAsSkillVsOtherDimension_AreNotValueEqual()
    {
        var occ = Create(preferredOccupationGroups: ["x1"]).Value;
        var skill = Create(preferredSkills: ["x1"]).Value;
        var muni = Create(preferredMunicipalities: ["x1"]).Value;

        skill.ShouldNotBe(occ);
        skill.ShouldNotBe(muni);
    }

    // ===============================================================
    // STEG 3 (ADR 0079; Klas product decision 2026-06-22) — ExperienceYears:
    // a single nullable scalar. null = not stated; 0 = stated zero (distinct);
    // 0..70 believed range (mirrors Resume Skill.YearsExperience).
    // ===============================================================

    [Fact]
    public void Create_WithExperienceYears_ReturnsSuccess()
    {
        var result = Create(experienceYears: 5);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ExperienceYears.ShouldBe(5);
    }

    [Fact]
    public void Create_WithZeroExperienceYears_IsValid_AndDistinctFromNull()
    {
        var zero = Create(experienceYears: 0).Value;
        var notStated = Create().Value;

        zero.ExperienceYears.ShouldBe(0);
        notStated.ExperienceYears.ShouldBeNull();
        zero.ShouldNotBe(notStated); // 0 ≠ "not stated"
    }

    [Fact]
    public void Create_WithNoExperienceYears_DefaultsToNull_NotStated()
    {
        Create().Value.ExperienceYears.ShouldBeNull();
        MatchPreferences.Empty.ExperienceYears.ShouldBeNull();
    }

    [Fact]
    public void Create_WithMaxExperienceYears_ReturnsSuccess()
    {
        Create(experienceYears: 70).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(71)]
    [InlineData(1000)]
    public void Create_WithOutOfRangeExperienceYears_ReturnsFailure(int bad)
    {
        var result = Create(experienceYears: bad);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.ExperienceYearsOutOfRange");
    }

    [Fact]
    public void TwoPreferences_DifferentExperienceYears_AreNotValueEqual()
    {
        var a = Create(experienceYears: 5).Value;
        var b = Create(experienceYears: 6).Value;

        a.ShouldNotBe(b);
        a.GetHashCode().ShouldNotBe(b.GetHashCode());
    }

    [Fact]
    public void TwoPreferences_SameExperienceYears_AreValueEqual()
    {
        var a = Create(preferredOccupationGroups: ["grp1"], experienceYears: 5).Value;
        var b = Create(preferredOccupationGroups: ["grp1"], experienceYears: 5).Value;

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Empty_HasEmptySkills_AndNullExperience()
    {
        MatchPreferences.Empty.PreferredSkills.ShouldBeEmpty();
        MatchPreferences.Empty.ExperienceYears.ShouldBeNull();
    }
}
