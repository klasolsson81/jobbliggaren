using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

// Fas 4 STEG 8 (F4-8, NO AI/LLM) — HeadingDrivenResumeSegmenter is the pure
// string-algorithm port impl (internal; visible via InternalsVisibleTo). The split
// from ICvTextExtractor exists precisely so the segmentation logic is unit-testable on
// a plain string with no binary PDF/DOCX fixture (CLAUDE.md §2.4). SPEC-DRIVEN tests of
// the documented behaviour: Swedish heading detection → Confident sections, English CV
// → En, headingless text → Degraded + NoSectionsDetected, determinism, and a degraded
// (heading-present-but-empty) block.
public class HeadingDrivenResumeSegmenterTests
{
    private readonly HeadingDrivenResumeSegmenter _sut = new();

    private const string SwedishCv =
        """
        Anna Andersson
        anna.andersson@example.com
        070-123 45 67

        Profil
        Erfaren backend-utvecklare med fokus på betaltjänster.

        Arbetslivserfarenhet
        Backend-utvecklare — Acme AB
        2021 - 2024
        Byggde betaltjänster i .NET.

        Senior-utvecklare — Globex AB
        2024 - nuvarande

        Utbildning
        Civilingenjör — KTH
        2016 - 2021

        Kompetenser
        C#, PostgreSQL, Docker

        Språk
        Svenska, Engelska
        """;

    private const string EnglishCv =
        """
        John Smith
        john.smith@example.com
        +44 20 7946 0958

        Profile
        Experienced backend developer with a focus on payment services.

        Work Experience
        Backend Developer at Acme Ltd
        2021 - 2024
        Developed and managed payment services and was responsible for the platform.

        Education
        MSc Computer Science from Imperial College
        2016 - 2021

        Skills
        C#, PostgreSQL, Docker
        """;

    [Fact]
    public void Segment_SwedishCvWithHeadings_OverallConfident()
    {
        var result = _sut.Segment(SwedishCv);

        result.Confidence.Overall.ShouldBe(OverallConfidenceLevel.Confident);
        result.Confidence.Fallback.ShouldBe(ParseFallbackReason.None);
        result.DetectedLanguage.ShouldBe(ResumeLanguage.Sv);
    }

    [Fact]
    public void Segment_SwedishCv_ExtractsContactEmailAndPhone()
    {
        var result = _sut.Segment(SwedishCv);

        result.Content.Contact.FullName.ShouldBe("Anna Andersson");
        result.Content.Contact.Email.ShouldBe("anna.andersson@example.com");
        result.Content.Contact.Phone.ShouldNotBeNull();
    }

    [Fact]
    public void Segment_SwedishCv_ContactExperienceEducationConfident()
    {
        var result = _sut.Segment(SwedishCv);

        LevelOf(result, ParsedSectionKind.Contact).ShouldBe(SectionConfidenceLevel.Confident);
        LevelOf(result, ParsedSectionKind.Experience).ShouldBe(SectionConfidenceLevel.Confident);
        LevelOf(result, ParsedSectionKind.Education).ShouldBe(SectionConfidenceLevel.Confident);

        result.Content.Experience.Count.ShouldBeGreaterThanOrEqualTo(1);
        result.Content.Education.Count.ShouldBeGreaterThanOrEqualTo(1);
        result.Content.Skills.ShouldContain("C#");
        result.Content.Languages.ShouldContain("Svenska");
    }

    [Fact]
    public void Segment_EnglishCv_DetectsEnglishLanguage()
    {
        var result = _sut.Segment(EnglishCv);

        result.DetectedLanguage.ShouldBe(ResumeLanguage.En);
    }

    [Fact]
    public void Segment_BareTextNoHeadings_DegradedWithNoSectionsDetected()
    {
        const string bare =
            "Jag är en utvecklare som har jobbat med olika projekt under många år.";

        var result = _sut.Segment(bare);

        result.Confidence.Overall.ShouldBe(OverallConfidenceLevel.Degraded);
        result.Confidence.Fallback.ShouldBe(ParseFallbackReason.NoSectionsDetected);
    }

    [Fact]
    public void Segment_HeadingPresentButEmptyBlock_SectionDegraded()
    {
        // "Kompetenser" heading is present with no entries under it (next heading
        // follows immediately) ⇒ that section is Degraded (heading found, no content).
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Kompetenser

            Språk
            Svenska
            """;

        var result = _sut.Segment(cv);

        LevelOf(result, ParsedSectionKind.Skills).ShouldBe(SectionConfidenceLevel.Degraded);
    }

    [Fact]
    public void Segment_IsDeterministic_SameInputEqualVerdict()
    {
        var first = _sut.Segment(SwedishCv);
        var second = _sut.Segment(SwedishCv);

        first.Confidence.Overall.ShouldBe(second.Confidence.Overall);
        first.DetectedLanguage.ShouldBe(second.DetectedLanguage);
        first.Content.Experience.Count.ShouldBe(second.Content.Experience.Count);
        first.Content.Education.Count.ShouldBe(second.Content.Education.Count);
        first.Content.Skills.Count.ShouldBe(second.Content.Skills.Count);

        for (var i = 0; i < first.Confidence.Sections.Count; i++)
        {
            first.Confidence.Sections[i].Kind.ShouldBe(second.Confidence.Sections[i].Kind);
            first.Confidence.Sections[i].Level.ShouldBe(second.Confidence.Sections[i].Level);
        }
    }

    private static SectionConfidenceLevel LevelOf(
        Application.Resumes.Abstractions.ResumeSegmentationResult result, ParsedSectionKind kind)
    {
        foreach (var section in result.Confidence.Sections)
        {
            if (section.Kind == kind)
                return section.Level;
        }

        return SectionConfidenceLevel.NotFound;
    }
}
