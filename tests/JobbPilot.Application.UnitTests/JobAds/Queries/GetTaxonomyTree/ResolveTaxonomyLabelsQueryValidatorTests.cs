using JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;
using JobbPilot.Domain.SavedSearches;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Queries.GetTaxonomyTree;

// ADR 0043 MAP-3 — reverse-lookup-cap enforce:as i Validation-pipeline FÖRE
// handlern. Cap = SearchCriteria.MaxConceptIds ×2 (Ssyk + Region i en sparad
// sökning). Konstanten refereras i assert, ALDRIG hårdkodad siffra (DRY/
// domän-konsekvens — om SearchCriteria.MaxConceptIds ändras följer testet med).
// Speglar SuggestJobAdTermsQueryValidatorTests.
public class ResolveTaxonomyLabelsQueryValidatorTests
{
    private readonly ResolveTaxonomyLabelsQueryValidator _validator = new();

    [Fact]
    public void Validate_ShouldExposeCapAsTwiceDomainMaxConceptIds_WhenInspected()
    {
        // Self-dokumenterande: bekräftar att cap härleds från domänkonstanten,
        // inte en magisk literal (CLAUDE.md §5.1 magic-string-förbud).
        ResolveTaxonomyLabelsQueryValidator.MaxConceptIdsPerCall
            .ShouldBe(SearchCriteria.MaxConceptIds * 2);
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
}
