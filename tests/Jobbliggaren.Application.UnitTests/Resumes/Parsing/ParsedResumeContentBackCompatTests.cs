using System.Text.Json;
using Jobbliggaren.Domain.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #815 — the parse artifact is persisted as an ENCRYPTED JSON shadow (Form B), so a row written
/// before <c>Sections</c> existed simply has no "sections" key. The expand half of expand/contract
/// (ADR 0095 D-D): the optional trailing constructor parameter takes its default and the property
/// lands as an empty list. No migration, no backfill — and no guessing about what those older
/// parses contained.
///
/// This test IS the contract. Make <c>Sections</c> a required member and it goes red, before a
/// deserialization failure reaches a real user's stored CV.
/// </summary>
public class ParsedResumeContentBackCompatTests
{
    private static readonly JsonSerializerOptions Options =
        new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Deserialize_LegacyJsonWithoutSectionsKey_YieldsEmptyList_NotNull_NotThrow()
    {
        // Exactly the shape written before #815 — note: no "sections".
        const string legacy =
            """
            {
              "contact": { "fullName": "Anna Andersson", "email": "anna@example.com", "phone": null, "location": null },
              "profile": "Erfaren utvecklare.",
              "experience": [],
              "education": [],
              "skills": ["C#"],
              "languages": ["Svenska"]
            }
            """;

        var content = JsonSerializer.Deserialize<ParsedResumeContent>(legacy, Options);

        content.ShouldNotBeNull();
        content.Sections.ShouldNotBeNull();
        content.Sections.ShouldBeEmpty();
        // Och resten överlever oförändrat.
        content.Profile.ShouldBe("Erfaren utvecklare.");
        content.Skills.ShouldContain("C#");
    }

    [Fact]
    public void Roundtrip_WithSections_PreservesHeadingVerbatimAndEntryOrder()
    {
        var original = new ParsedResumeContent(
            new ParsedContact("Anna Andersson", null, null, null),
            sections:
            [
                new ParsedSection("PROJEKT",
                [
                    new ParsedSectionEntry("Betalplattform", ["Byggde en betaltjänst."]),
                    new ParsedSectionEntry(null, ["- Punkt utan titel"]),
                ]),
                new ParsedSection("Referenser",
                [
                    new ParsedSectionEntry(null, ["Lämnas på begäran."]),
                ]),
            ]);

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<ParsedResumeContent>(json, Options);

        restored.ShouldNotBeNull();
        restored.Sections.Count.ShouldBe(2);
        // Rubriken är användarens text — versaliseringen får inte normaliseras bort.
        restored.Sections[0].Heading.ShouldBe("PROJEKT");
        restored.Sections[1].Heading.ShouldBe("Referenser");
        restored.Sections[0].Entries[0].Title.ShouldBe("Betalplattform");
        restored.Sections[0].Entries[1].Title.ShouldBeNull();
    }
}
