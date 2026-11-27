using System.Text.Json;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;
using Jobbliggaren.Infrastructure.Security;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #2353 (senior-cto-advisor D11): the segmenter reads an entry's description once, at parse, with
/// the same function the review reads an older entry's block with, so the two cannot disagree.
/// </summary>
public class EntryDescriptionTests
{
    private readonly HeadingDrivenResumeSegmenter _sut = CvParsingLexiconFixture.Segmenter();

    [Theory]
    [InlineData("Testare — Acme AB\n2020 – 2023\nTestade betalflöden.\nSkrev testfall.", "Acme AB",
        new[] { "Testade betalflöden.", "Skrev testfall." })]
    [InlineData("Testare\nAcme AB\n2020 – 2023\nTestade betalflöden.", "Acme AB", new[] { "Testade betalflöden." })]
    [InlineData("Testare — Acme AB\n2020 – 2023\n• Testade betalflöden.\n  ● Skrev testfall  ", "Acme AB",
        new[] { "• Testade betalflöden.", "● Skrev testfall" })]
    [InlineData("Testare — Acme AB\n2020 – 2023", "Acme AB", new string[0])]
    public void Lines_AreTheBlockWithoutItsHeaderPeriodAndOrganisationLines(
        string entryText, string organization, string[] expected) =>
        EntryDescription.Lines(entryText, organization).ShouldBe(expected);

    // The header of a period-first entry is its second line. Before #2353 only the first line was
    // skipped, so the header was scored as a description bullet.
    [Fact]
    public void Lines_OfAPeriodFirstEntry_SkipItsHeaderLine()
    {
        EntryDescription.Lines("2005 – 2010\nOperatör - Acme AB\nKörde maskiner.", "Acme AB")
            .ShouldBe(["Körde maskiner."]);
    }

    [Fact]
    public void Segment_StoresEachEntrysDescription_OrNoneWhenItHasNoLines()
    {
        var experiences = _sut.Segment(string.Join('\n',
            "Arbetslivserfarenhet",
            "Testledare — Acme AB", "2020 – 2023", "• Testade betalflöden.", "• Skrev testfall.",
            "Test Analyst", "Fiktiva Konsult AB, Malmö", "2018 – 2020", "Rapporterade fel i Jira.",
            "Utvecklare — Beta AB", "2016 – 2018")).Content.Experience;

        experiences.Select(e => e.Description).ShouldBe(
            ["• Testade betalflöden.\n• Skrev testfall.", "Rapporterade fel i Jira.", null]);
    }

    // The review's bullet unit is the same for an entry parsed now and for the same entry stored
    // before the segmenter read descriptions.
    [Theory]
    [InlineData("Testare — Acme AB\n2020 – 2023\n• Testade betalflöden.\n• Skrev testfall.")]
    [InlineData("Test Analyst\nFiktiva Konsult AB, Malmö\n2018 – 2021\nRapporterade fel i Jira.")]
    [InlineData("2005 – 2010\nOperatör - Acme AB\nKörde maskiner.")]
    [InlineData("Testare — Acme AB\n2020 – 2023")]
    public void DescriptionBullets_AreTheSameForANewEntryAndForTheSameEntryStoredBefore(string block)
    {
        var parsed = _sut.Segment("Arbetslivserfarenhet\n" + block).Content.Experience.ShouldHaveSingleItem();
        var stored = parsed with { Description = null };

        ReviewText.DescriptionBullets(parsed).ShouldBe(ReviewText.DescriptionBullets(stored));
    }

    [Fact]
    public void FromParsed_ReadsTheDescriptionWhenThereIsOne_AndTheBlockOtherwise()
    {
        var withDescription = new ParsedExperience("Testare", "Acme AB", "2020 – 2023",
            "Testare — Acme AB\n2020 – 2023\nTestade.", "Testade.");
        var stored = withDescription with { Description = null };

        var read = ReviewableExperience.FromParsed(withDescription);
        var legacy = ReviewableExperience.FromParsed(stored);

        read.Text.ShouldBe("Testade.");
        read.TextIsDescriptionOnly.ShouldBeTrue();
        legacy.Text.ShouldBe(stored.RawText);
        legacy.TextIsDescriptionOnly.ShouldBeFalse();
    }

    // An entry stored before the segmenter read descriptions has no "description" key; it reads as
    // none, with no migration and no backfill (the #815 expand pattern).
    [Fact]
    public void Deserialize_AnEntryStoredWithoutADescription_ReadsNone()
    {
        const string legacy =
            """
            {
              "contact": { "fullName": null, "email": null, "phone": null, "location": null },
              "profile": null,
              "experience": [
                { "title": "Testare", "organization": "Acme AB", "period": "2020 – 2023",
                  "rawText": "Testare — Acme AB\n2020 – 2023\nTestade." }
              ],
              "education": [],
              "skills": [],
              "languages": []
            }
            """;

        var content = JsonSerializer.Deserialize<ParsedResumeContent>(legacy, EncryptedFieldRegistry.ContentJsonOptions);

        content.ShouldNotBeNull();
        var experience = content.Experience.ShouldHaveSingleItem();
        experience.Description.ShouldBeNull();
        experience.RawText.ShouldBe("Testare — Acme AB\n2020 – 2023\nTestade.");
    }

    [Fact]
    public void Roundtrip_KeepsTheDescription()
    {
        var original = new ParsedResumeContent(
            ParsedContact.Empty,
            experience: [new ParsedExperience("Testare", "Acme AB", "2020 – 2023", "Testare — Acme AB\n2020 – 2023\nTestade.", "Testade.")]);

        var json = JsonSerializer.Serialize(original, EncryptedFieldRegistry.ContentJsonOptions);
        var restored = JsonSerializer.Deserialize<ParsedResumeContent>(json, EncryptedFieldRegistry.ContentJsonOptions);

        restored.ShouldNotBeNull();
        restored.Experience.ShouldHaveSingleItem().Description.ShouldBe("Testade.");
    }
}
