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

    [Fact]
    public void Segment_ExperienceHeaderWithInlinePeriod_DoesNotBleedDateIntoFields()
    {
        // Regression (reported layout-split bug): a header that packs the period on the same
        // line as the role/company ("Plasman — Operatör 2005 – nu") previously put the trailing
        // date into the organization slot ("Operatör 2005 – nu"). The date must be stripped from
        // the title/organization fields and recovered as the Period instead. The slot ORDER
        // (role vs company) is intentionally NOT corrected — the user edits it in the gap-fill.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Plasman — Operatör 2005 – nu
            Körde maskiner.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        // ShouldBe pins the exact values, proving no date bled into either field.
        exp.Title.ShouldBe("Plasman");
        exp.Organization.ShouldBe("Operatör");
        // "nu" is now a recognised present-token, so the whole range is captured as the period.
        exp.Period.ShouldNotBeNull();
        exp.Period.ShouldContain("2005");
        exp.Period.ShouldContain("nu");
    }

    [Fact]
    public void Segment_ExperienceHeaderWithTrailingYear_StripsYearFromFields()
    {
        // A single trailing year ("… Utvecklare 2019") is also stripped from the split fields
        // (and recovered as the period). A leading/internal year is left alone (it is likely
        // part of a name), so only the trailing run is removed.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Acme AB — Utvecklare 2019
            Byggde saker.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        // ShouldBe pins the exact value, proving the trailing year was stripped from the field.
        exp.Organization.ShouldBe("Utvecklare");
        exp.Period.ShouldBe("2019");
    }

    [Fact]
    public void Segment_EducationHeaderWithInlinePeriod_DoesNotBleedDateIntoFields()
    {
        // EDUCATION symmetry: ParseEducations runs the SAME SplitTitleOrganization as
        // experience, so the trailing-date strip must apply identically — an education entry
        // that packs the period on the header line ("KTH — Civilingenjör 2005 – nu") must not
        // bleed the date into degree/institution. Guards against a future refactor that special-
        // cases only the experience path. Mapping: title slot → Degree, org slot → Institution.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Utbildning
            KTH — Civilingenjör 2005 – nu
            Läste teknik.
            """;

        var result = _sut.Segment(cv);

        var edu = result.Content.Education.ShouldHaveSingleItem();
        // ShouldBe pins the exact values, proving no date bled into either field.
        edu.Degree.ShouldBe("KTH");
        edu.Institution.ShouldBe("Civilingenjör");
        edu.Period.ShouldNotBeNull();
        edu.Period.ShouldContain("2005");
        edu.Period.ShouldContain("nu");
    }

    [Fact]
    public void Segment_ExperienceHeaderThatIsOnlyADate_FallsBackToSecondLineForOrganization()
    {
        // Degenerate edge of the new strip: a header line that is ONLY a date range would be
        // consumed entirely by StripTrailingPeriod, leaving an empty title. The split must then
        // degrade gracefully — no empty/garbage field — falling back to the second line as the
        // organization (the existing "Title / Company / Dates" fallback path). Proves the strip
        // does not produce a stray empty field when it over-consumes the whole line.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            2005 – 2010
            Acme AB
            Körde maskiner.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        // Title is null (the date-only first line collapsed to empty → NullIfEmpty), and the
        // organization falls back to the second line. ShouldBe pins both, proving no date bled in.
        exp.Title.ShouldBeNull();
        exp.Organization.ShouldBe("Acme AB");
        exp.Period.ShouldNotBeNull();
        exp.Period.ShouldContain("2005");
        exp.Period.ShouldContain("2010");
    }

    [Fact]
    public void Segment_ExperienceHeaderWithPeriodOnSeparateLine_KeepsFieldsClean()
    {
        // No regression: "Role — Company\nYYYY-YYYY" (period on its own line) has no date on the
        // header line, so the trailing-period strip is a no-op and the fields stay clean.
        var result = _sut.Segment(SwedishCv);

        foreach (var exp in result.Content.Experience)
        {
            exp.Organization?.ShouldNotContain("2021");
            exp.Organization?.ShouldNotContain("2024");
            exp.Title?.ShouldNotContain("2021");
        }

        result.Content.Experience.Count.ShouldBeGreaterThanOrEqualTo(2);
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
