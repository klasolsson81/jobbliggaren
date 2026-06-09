using JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;
using JobbPilot.Domain.SavedSearches;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Queries.GetTaxonomyTree;

// ADR 0043 MAP-3 + C1 (ADR 0067 Platsbanken sök-paritet) — reverse-lookup-cap
// enforce:as i Validation-pipeline FÖRE handlern. C1 höjer multiplikatorn ×2→×4:
// en sparad sökning kan nu bära fyra MaxConceptIds-dimensioner (OccupationGroup
// + Municipality + Region + Ssyk) → cap = SearchCriteria.MaxConceptIds ×4.
// Konstanten refereras i assert, ALDRIG hårdkodad siffra (DRY/domän-konsekvens
// — om SearchCriteria.MaxConceptIds ändras följer testet med).
// Speglar SuggestJobAdTermsQueryValidatorTests.
public class ResolveTaxonomyLabelsQueryValidatorTests
{
    private readonly ResolveTaxonomyLabelsQueryValidator _validator = new();

    [Fact]
    public void Validate_ShouldExposeCapAsFourTimesDomainMaxConceptIds_WhenInspected()
    {
        // Self-dokumenterande: bekräftar att cap härleds från domänkonstanten
        // ×4 (fyra filter-dimensioner per sökning efter C1), inte en magisk
        // literal (CLAUDE.md §5.1 magic-string-förbud).
        ResolveTaxonomyLabelsQueryValidator.MaxConceptIdsPerCall
            .ShouldBe(SearchCriteria.MaxConceptIds * 4);
    }

    [Fact]
    public void Validate_ShouldPass_WhenConceptIdCountEqualsCap()
    {
        var ids = Enumerable
            .Range(0, ResolveTaxonomyLabelsQueryValidator.MaxConceptIdsPerCall)
            .Select(i => $"id-{i}")
            .ToList();

        var result = _validator.Validate(new ResolveTaxonomyLabelsQuery(ids));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ShouldBeInvalid_WhenConceptIdCountExceedsCap()
    {
        var ids = Enumerable
            .Range(0, ResolveTaxonomyLabelsQueryValidator.MaxConceptIdsPerCall + 1)
            .Select(i => $"id-{i}")
            .ToList();

        var result = _validator.Validate(new ResolveTaxonomyLabelsQuery(ids));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_ShouldPass_WhenConceptIdListIsEmpty()
    {
        // Tom lista är giltig: en sparad sökning utan Ssyk/Region ger inget
        // reverse-lookup-behov men endpointen ska inte 400:a på tomt anrop.
        var result = _validator.Validate(
            new ResolveTaxonomyLabelsQuery([]));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ShouldBeInvalid_WhenConceptIdListIsNull()
    {
        var result = _validator.Validate(
            new ResolveTaxonomyLabelsQuery(null!));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_ShouldBeInvalid_WhenElementExceeds32Chars()
    {
        // Speglar SearchCriteria concept-id-format (^[A-Za-z0-9_-]{1,32}$).
        var result = _validator.Validate(
            new ResolveTaxonomyLabelsQuery([new string('x', 33)]));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_ShouldPass_WhenElementIsExactly32Chars()
    {
        var result = _validator.Validate(
            new ResolveTaxonomyLabelsQuery([new string('x', 32)]));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ShouldBeInvalid_WhenElementIsEmpty()
    {
        var result = _validator.Validate(
            new ResolveTaxonomyLabelsQuery([string.Empty]));

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("bad<id>")]
    [InlineData("åäö")]
    [InlineData("semi;colon")]
    public void Validate_ShouldBeInvalid_WhenElementBreaksConceptIdCharset(string badId)
    {
        // C1 (security-auditor 2026-06-09 Minor) — ConceptIdPattern
        // (^[A-Za-z0-9_-]{1,32}$) speglas nu även här. Charset-cap begränsar den
        // reflekterade id-strängen i svars-DTO:n (defense-in-depth mot XSS-stuffing).
        var result = _validator.Validate(
            new ResolveTaxonomyLabelsQuery([badId]));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_ShouldPass_WhenElementMatchesConceptIdCharset()
    {
        // Giltigt JobTech-format (alfanumeriskt + _- ) inkl. okänd-men-välformad
        // kod (taxonomi-drift) — graceful "Okänd kod"-fallback sker i handlern,
        // inte via 400. Validatorn släpper igenom välformade ids.
        var result = _validator.Validate(
            new ResolveTaxonomyLabelsQuery(["MVqp_eS8_kDZ", "helt-okand-77"]));

        result.IsValid.ShouldBeTrue();
    }
}
