using Jobbliggaren.Application.JobAds.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds;

/// <summary>
/// STEG 6 Approach B (2026-05-24) — verifierar att SearchSynonymsOptions
/// är binding-ready (kontrakt mot IOptions-binding i Infrastructure DI).
///
/// <para>
/// Full IOccupationSynonymExpander-impl-test sker i Infrastructure-test eller
/// integration; här verifierar vi bara DTO-shape + case-insensitive dict.
/// </para>
/// </summary>
public class OccupationSynonymExpanderConfigTests
{
    [Fact]
    public void SearchSynonymsOptions_DefaultsToEmptyOccupations()
    {
        var sut = new SearchSynonymsOptions();
        sut.Occupations.ShouldNotBeNull();
        sut.Occupations.Count.ShouldBe(0);
    }

    [Fact]
    public void SearchSynonymsOptions_DictionaryIsCaseInsensitive()
    {
        var sut = new SearchSynonymsOptions();
        sut.Occupations["Systemutvecklare"] = ["fg7B_yov_smw"];

        sut.Occupations.ContainsKey("systemutvecklare").ShouldBeTrue();
        sut.Occupations.ContainsKey("SYSTEMUTVECKLARE").ShouldBeTrue();
        sut.Occupations["systemutvecklare"][0].ShouldBe("fg7B_yov_smw");
    }

    [Fact]
    public void SearchSynonymsOptions_SectionNameMatches()
    {
        SearchSynonymsOptions.SectionName.ShouldBe("SearchSynonyms");
    }
}
