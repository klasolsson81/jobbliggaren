using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// #478 Low — every parse-diagnostic enum value surfaced in CV-review evidence must carry a
/// Swedish label (§10), and no English enum token may leak into that user-facing text (§5). The
/// exhaustiveness loops are the drift-guard: a new enum value without a label fails here (and the
/// map's own switch throws at runtime), so the English-token leak the fix removed cannot silently
/// reappear. Parity the crossref-badge label pattern (UI label pinned to a backend enum by test).
/// </summary>
public class ReviewEvidenceLabelsTests
{
    [Fact]
    public void Section_ShouldGiveANonEmptySwedishLabelWithoutLeakingTheEnumToken_ForEveryKind()
    {
        foreach (var kind in Enum.GetValues<ParsedSectionKind>())
        {
            var label = ReviewEvidenceLabels.Section(kind);

            label.ShouldNotBeNullOrWhiteSpace();
            label.Contains(kind.ToString(), StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
                $"the Swedish label for {kind} must not leak the English enum token ('{label}').");
        }
    }

    [Fact]
    public void Fallback_ShouldGiveANonEmptySwedishLabelWithoutLeakingTheEnumToken_ForEveryReason()
    {
        foreach (var reason in Enum.GetValues<ParseFallbackReason>())
        {
            var label = ReviewEvidenceLabels.Fallback(reason);

            label.ShouldNotBeNullOrWhiteSpace();
            label.Contains(reason.ToString(), StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
                $"the Swedish label for {reason} must not leak the English enum token ('{label}').");
        }
    }

    [Fact]
    public void Section_ShouldUseTheParsedSectionKindProductVocabulary()
    {
        ReviewEvidenceLabels.Section(ParsedSectionKind.Experience).ShouldBe("arbetslivserfarenhet");
        ReviewEvidenceLabels.Section(ParsedSectionKind.Education).ShouldBe("utbildning");
        ReviewEvidenceLabels.Section(ParsedSectionKind.Skills).ShouldBe("kompetenser");
        ReviewEvidenceLabels.Section(ParsedSectionKind.Languages).ShouldBe("språk");
    }

    [Fact]
    public void Fallback_ShouldExplainTheDegradationInSwedish()
    {
        ReviewEvidenceLabels.Fallback(ParseFallbackReason.ExtractionFailed).ShouldBe("extraktionen misslyckades");
        ReviewEvidenceLabels.Fallback(ParseFallbackReason.EncodingSuspect).ShouldBe("teckenkodningen ser felaktig ut");
    }

    [Fact]
    public void Section_ShouldMapContactAndProfileToTheirSwedishVocabulary()
    {
        // Completes the exact-value coverage the vocabulary test above leaves (it pins Experience/
        // Education/Skills/Languages). A typo like "kontkat" leaks no enum token and is non-empty, so
        // only an exact expectation catches it — this closes the last two ParsedSectionKind labels.
        ReviewEvidenceLabels.Section(ParsedSectionKind.Contact).ShouldBe("kontakt");
        ReviewEvidenceLabels.Section(ParsedSectionKind.Profile).ShouldBe("profil");
    }

    [Fact]
    public void Fallback_ShouldMapTheRemainingReasonsToSwedish()
    {
        // Completes the exact-value coverage for the three ParseFallbackReason values the
        // degradation test above does not pin (None / NoSectionsDetected / ScannedImageNoText).
        ReviewEvidenceLabels.Fallback(ParseFallbackReason.None).ShouldBe("ingen avvikelse");
        ReviewEvidenceLabels.Fallback(ParseFallbackReason.NoSectionsDetected)
            .ShouldBe("inga sektioner kunde identifieras");
        ReviewEvidenceLabels.Fallback(ParseFallbackReason.ScannedImageNoText)
            .ShouldBe("inscannad bild utan textlager");
    }

    [Fact]
    public void Section_ShouldThrowArgumentOutOfRange_WhenTheSectionKindHasNoLabel()
    {
        // The drift-guard's runtime backstop: the throwing default arm. A ParsedSectionKind value
        // with no mapping (here an undefined cast simulating a NEW enum member shipped without a
        // label) must throw rather than silently render an empty or English string. This is the
        // mechanism the exhaustiveness loop relies on — proven directly here.
        var unmapped = (ParsedSectionKind)999;

        Should.Throw<ArgumentOutOfRangeException>(() => ReviewEvidenceLabels.Section(unmapped));
    }

    [Fact]
    public void Fallback_ShouldThrowArgumentOutOfRange_WhenTheReasonHasNoLabel()
    {
        var unmapped = (ParseFallbackReason)999;

        Should.Throw<ArgumentOutOfRangeException>(() => ReviewEvidenceLabels.Fallback(unmapped));
    }
}
