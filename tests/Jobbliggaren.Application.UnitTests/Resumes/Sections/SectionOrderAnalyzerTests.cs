using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Sections;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Sections;

/// <summary>
/// Fas 4b 8b.4b (ADR 0108) — <see cref="SectionOrderAnalyzer"/>, the ONE definition of "what is this
/// CV's section order and does it deviate" that BOTH engines read (B1 judges against it,
/// <c>SectionReorderTransform</c> proposes against it).
///
/// <para><b>Why this file exists.</b> The first draft had no tests of its own — it was exercised
/// only indirectly, through two consumers, over a handful of whole-line fixtures. That is exactly
/// how its worst defect hid: it re-implemented heading DETECTION as "normalise the whole line, look
/// it up", which is only HALF of what the segmenter recognises (it also parses the boundary-gated
/// INLINE form, "Kompetenser: C#, PostgreSQL" — #421). A CV writing a section inline had that
/// section parsed and invisible to the order — so a genuinely deviating CV came back "in
/// recommended order" and the transform stayed silent. The analyzer now runs the segmenter's own
/// detector, and the first test below is the one that would have caught it.</para>
/// </summary>
public class SectionOrderAnalyzerTests
{
    private static SectionOrderAssessment Analyze(string rawText) =>
        SectionOrderAnalyzer.Analyze(rawText, RealParsingLexicon(), RealCvConventionsProvider().GetConventions());

    // ===============================================================
    // (a) The detector is SHARED — the inline form is not a second dialect
    // ===============================================================

    [Fact]
    public void Analyze_ShouldObserveAnInlineHeading_ExactlyAsTheSegmenterDoes()
    {
        // "Kompetenser: C#, SQL" IS a section to the segmenter (#421, boundary-gated, typed-only).
        // An analyzer that only matched whole lines would not see it — and the CV below, which
        // genuinely puts Kompetenser before Arbetslivserfarenhet, would have been reported as
        // correctly ordered. This test is the reason the detector is shared.
        var order = Analyze("Kompetenser: C#, SQL\n\nArbetslivserfarenhet\nBackend-utvecklare 2021–2024");

        order.Observed.Select(o => o.TypedKind)
            .ShouldBe([ParsedSectionKind.Skills, ParsedSectionKind.Experience]);
        order.Deviates.ShouldBeTrue("Kompetenser står före Arbetslivserfarenhet — det avviker.");
    }

    [Fact]
    public void Analyze_ShouldNotObserveAProseLineThatMerelyStartsWithAHeadingWord()
    {
        // The mirror of the test above, and the reason the segmenter gates the inline split on a
        // section boundary: "Erfarenhet: över 10 år inom IT." sitting directly under a heading is
        // that heading's CONTENT, not a new section. An analyzer that hijacked it would invent
        // structure the user did not write (ADR 0071).
        var order = Analyze("Profil\nErfarenhet: över 10 år inom IT.");

        order.Observed.Count.ShouldBe(1);
        order.Observed[0].TypedKind.ShouldBe(ParsedSectionKind.Profile);
    }

    // ===============================================================
    // (b) The dedupe guard — without it, a repeated heading fabricates a reorder
    // ===============================================================

    [Fact]
    public void Analyze_ShouldCountOnlyTheFirstPosition_WhenATypedHeadingRepeats()
    {
        // The segmenter CONCATENATES two blocks under the same typed heading — they are one section.
        // Counting both would give ranks [2, 3, 2], whose stable sort is NOT the observed order, so
        // a perfectly ordered CV would earn a phantom Warn and a phantom reorder proposal.
        var order = Analyze(
            "Arbetslivserfarenhet\nDev, Acme\nUtbildning\nKTH\nArbetslivserfarenhet\nDev, Initech");

        order.Observed.Count.ShouldBe(2);
        order.Deviates.ShouldBeFalse("En upprepad rubrik är EN sektion — ingen spöklik omordning.");
    }

    [Fact]
    public void Analyze_ShouldKeepTwoFreeSectionsThatShareAnId_BecauseTheSegmenterKeepsThemToo()
    {
        // #815: two same-id free sections stay TWO sections in the parse. The evidence quotes the
        // user a list of her own headings — silently collapsing one of them would make that list a
        // lie about her own document.
        var order = Analyze("Arbetslivserfarenhet\nDev\nProjekt\nA\nEgna projekt\nB");

        order.ObservedHeadings.ShouldBe("Arbetslivserfarenhet, Projekt, Egna projekt");
    }

    // ===============================================================
    // (c) Stability + the free-section rank arm
    // ===============================================================

    [Fact]
    public void Analyze_ShouldSortUnnamedSectionsAfterTheNamedOnes_KeepingTheirObservedOrder()
    {
        // The rubric's trailing "→ Övrigt" is the STABILITY of the sort, not a data field.
        var order = Analyze("Projekt\nA\nReferenser\nB\nArbetslivserfarenhet\nDev");

        order.RecommendedHeadings.ShouldBe("Arbetslivserfarenhet, Projekt, Referenser");
    }

    [Fact]
    public void Analyze_ShouldRankAFreeSectionTheConventionNAMES_NotJustTypedOnes()
    {
        // RankOf's free-section arm is DEAD against the shipped asset (which orders only typed ids),
        // so nothing proved it works. Driven with a SYNTHETIC convention that orders `projekt`
        // between experience and education — the same seam the cross-asset pin already uses.
        // Mutation: `return int.MaxValue` straight away in the free arm → this test goes red.
        var conventions = new CvConventions("test",
        [
            new CvSectionOrderEntry("experience", ParsedSectionKind.Experience),
            new CvSectionOrderEntry("projekt", TypedKind: null),
            new CvSectionOrderEntry("education", ParsedSectionKind.Education),
        ]);

        var order = SectionOrderAnalyzer.Analyze(
            "Utbildning\nKTH\nProjekt\nJobbliggaren\nArbetslivserfarenhet\nDev",
            RealParsingLexicon(), conventions);

        order.RecommendedHeadings.ShouldBe("Arbetslivserfarenhet, Projekt, Utbildning",
            "Projekt är NAMNGIVEN i konventionen och ska rankas där, inte skjutas sist som en okänd.");
    }

    // ===============================================================
    // (d) OrderObserved — "we did not look" is not "it is correct"
    // ===============================================================

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    [InlineData("En text helt utan igenkännbara rubriker")]
    [InlineData("Arbetslivserfarenhet\nBackend-utvecklare 2021–2024")]
    public void Analyze_ShouldReportTheOrderAsUnobserved_WhenFewerThanTwoSectionsAreRecognised(string rawText)
    {
        var order = Analyze(rawText);

        order.OrderObserved.ShouldBeFalse();
        order.Deviates.ShouldBeFalse(
            "Färre än två sektioner KAN inte stå i fel ordning — men det är inte samma sak som att "
            + "ordningen är rätt, och OrderObserved är det som bär skillnaden.");
    }

    [Fact]
    public void Analyze_ShouldReportTheOrderAsObserved_WhenTwoSectionsAreRecognised()
    {
        // The mirror. Without it, "OrderObserved is false" would be indistinguishable from
        // "OrderObserved is never true".
        Analyze("Arbetslivserfarenhet\nDev\nUtbildning\nKTH").OrderObserved.ShouldBeTrue();
    }

    // ===============================================================
    // (e) Text-shape edges — the forms real extraction actually emits
    // ===============================================================

    [Fact]
    public void Analyze_ShouldReadCrLfText_IdenticallyToLfText()
    {
        // PdfPig/OpenXml extraction emits CRLF on Windows. Split('\n') leaves a trailing '\r' on
        // every line, and only the lexicon's normaliser (Trim) saves us — so pin it, because the
        // next person to "tidy" the normaliser will not know this depended on it.
        const string body = "Utbildning\nKTH\nArbetslivserfarenhet\nDev";

        var lf = Analyze(body);
        var crlf = Analyze(body.Replace("\n", "\r\n", StringComparison.Ordinal));

        crlf.ObservedHeadings.ShouldBe(lf.ObservedHeadings);
        crlf.ObservedHeadings.ShouldBe("Utbildning, Arbetslivserfarenhet",
            "Ingen '\\r' får läcka in i den citerade rubriken.");
        crlf.Deviates.ShouldBeTrue();
    }

    [Fact]
    public void Analyze_ShouldQuoteTheHeadingWithoutItsTrailingColon()
    {
        // "Kompetenser:" on its own line is a heading. The evidence shows the user her own words —
        // but not a dangling colon in the middle of a comma-separated list.
        Analyze("Kompetenser:\nC#\nArbetslivserfarenhet\nDev")
            .ObservedHeadings.ShouldBe("Kompetenser, Arbetslivserfarenhet");
    }

    [Fact]
    public void Analyze_ShouldPreserveTheUsersOwnCasing_InTheCitedHeadings()
    {
        // The engine cites her words, it does not invent vocabulary (and it does not silently
        // normalise them either — that is D6's job, and only as a PROPOSAL).
        Analyze("UTBILDNING\nKTH\nArbetslivserfarenhet\nDev")
            .ObservedHeadings.ShouldBe("UTBILDNING, Arbetslivserfarenhet");
    }

    [Fact]
    public void Analyze_ShouldIgnoreAPunctuationOnlyLine()
    {
        Analyze("...\n---\nArbetslivserfarenhet\nDev\nUtbildning\nKTH")
            .Observed.Count.ShouldBe(2);
    }
}
