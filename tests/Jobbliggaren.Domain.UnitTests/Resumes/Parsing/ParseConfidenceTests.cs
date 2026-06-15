using Jobbliggaren.Domain.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Resumes.Parsing;

// Fas 4 STEG 8 (F4-8, OQ5) — ParseConfidence.FromSections is a PURE, deterministic
// function of the per-section verdicts (no weighted float; the section verdicts ARE
// the confidence, mirroring MatchScore's no-opaque-total guard). SPEC-DRIVEN tests
// of the documented rule: Confident only when Contact is Confident AND at least one
// of Experience/Education is Confident; no section found ⇒ Degraded +
// NoSectionsDetected; extraction failure is modelled separately via Failed.
public class ParseConfidenceTests
{
    private static SectionConfidence Section(
        ParsedSectionKind kind, SectionConfidenceLevel level) =>
        new(kind, level, []);

    [Fact]
    public void FromSections_AllNotFound_DegradedWithNoSectionsDetected()
    {
        var sections = new[]
        {
            Section(ParsedSectionKind.Contact, SectionConfidenceLevel.NotFound),
            Section(ParsedSectionKind.Experience, SectionConfidenceLevel.NotFound),
            Section(ParsedSectionKind.Education, SectionConfidenceLevel.NotFound),
        };

        var confidence = ParseConfidence.FromSections(sections);

        confidence.Overall.ShouldBe(OverallConfidenceLevel.Degraded);
        confidence.Fallback.ShouldBe(ParseFallbackReason.NoSectionsDetected);
        confidence.RequiresManualReview.ShouldBeTrue();
    }

    [Fact]
    public void FromSections_ContactAndExperienceConfident_OverallConfident()
    {
        var sections = new[]
        {
            Section(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident),
            Section(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident),
        };

        var confidence = ParseConfidence.FromSections(sections);

        confidence.Overall.ShouldBe(OverallConfidenceLevel.Confident);
        confidence.Fallback.ShouldBe(ParseFallbackReason.None);
        confidence.RequiresManualReview.ShouldBeFalse();
    }

    [Fact]
    public void FromSections_ContactAndEducationConfident_OverallConfident()
    {
        // Education satisfies the "at least one history section" clause too.
        var sections = new[]
        {
            Section(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident),
            Section(ParsedSectionKind.Experience, SectionConfidenceLevel.NotFound),
            Section(ParsedSectionKind.Education, SectionConfidenceLevel.Confident),
        };

        var confidence = ParseConfidence.FromSections(sections);

        confidence.Overall.ShouldBe(OverallConfidenceLevel.Confident);
    }

    [Fact]
    public void FromSections_ContactDegradedButExperienceConfident_OverallDegraded()
    {
        // Contact NOT confident ⇒ the document is not a confident parse, even with
        // a confident history section.
        var sections = new[]
        {
            Section(ParsedSectionKind.Contact, SectionConfidenceLevel.Degraded),
            Section(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident),
        };

        var confidence = ParseConfidence.FromSections(sections);

        confidence.Overall.ShouldBe(OverallConfidenceLevel.Degraded);
        confidence.Fallback.ShouldBe(ParseFallbackReason.None);
        confidence.RequiresManualReview.ShouldBeTrue();
    }

    [Fact]
    public void FromSections_ContactConfidentButNoHistoryConfident_OverallDegraded()
    {
        // Contact confident but neither Experience nor Education confident ⇒ Degraded.
        var sections = new[]
        {
            Section(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident),
            Section(ParsedSectionKind.Experience, SectionConfidenceLevel.NotFound),
            Section(ParsedSectionKind.Education, SectionConfidenceLevel.Degraded),
        };

        var confidence = ParseConfidence.FromSections(sections);

        confidence.Overall.ShouldBe(OverallConfidenceLevel.Degraded);
    }

    [Fact]
    public void Failed_ProducesFailedOverall_GivenReason_EmptySections_RequiresReview()
    {
        var confidence = ParseConfidence.Failed(ParseFallbackReason.ExtractionFailed);

        confidence.Overall.ShouldBe(OverallConfidenceLevel.Failed);
        confidence.Fallback.ShouldBe(ParseFallbackReason.ExtractionFailed);
        confidence.Sections.ShouldBeEmpty();
        confidence.RequiresManualReview.ShouldBeTrue();
    }

    [Fact]
    public void Failed_CarriesScannedImageReason_WhenProvided()
    {
        var confidence = ParseConfidence.Failed(ParseFallbackReason.ScannedImageNoText);

        confidence.Overall.ShouldBe(OverallConfidenceLevel.Failed);
        confidence.Fallback.ShouldBe(ParseFallbackReason.ScannedImageNoText);
    }

    [Fact]
    public void RequiresManualReview_IsTrue_IffOverallIsNotConfident()
    {
        var confident = ParseConfidence.FromSections(
        [
            Section(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident),
            Section(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident),
        ]);
        var degraded = ParseConfidence.FromSections(
        [
            Section(ParsedSectionKind.Contact, SectionConfidenceLevel.Degraded),
        ]);
        var failed = ParseConfidence.Failed(ParseFallbackReason.ExtractionFailed);

        confident.RequiresManualReview.ShouldBeFalse();
        degraded.RequiresManualReview.ShouldBeTrue();
        failed.RequiresManualReview.ShouldBeTrue();
    }

    [Fact]
    public void FromSections_IsDeterministic_SameInputSameVerdict()
    {
        SectionConfidence[] Build() =>
        [
            Section(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident),
            Section(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident),
        ];

        var first = ParseConfidence.FromSections(Build());
        var second = ParseConfidence.FromSections(Build());

        first.Overall.ShouldBe(second.Overall);
        first.Fallback.ShouldBe(second.Fallback);
    }
}
