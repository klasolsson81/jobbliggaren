using Jobbliggaren.QA.Corpus.Harness;
using Jobbliggaren.QA.Corpus.Layout;
using Jobbliggaren.QA.Corpus.Reporting;
using Shouldly;

namespace Jobbliggaren.QA.Corpus;

/// <summary>
/// Guards on the EMITTER, not on the product. These are legitimate hard asserts under the corpus's
/// assert rule: their subject is the corpus's own report builder, which nothing in
/// <c>src/</c> can move.
/// </summary>
public sealed class LayoutCorpusEmitterTests
{
    /// <summary>
    /// The claim-discipline enforcement is STRUCTURAL: <see cref="LayoutCorpusReportData"/> has no
    /// field that aggregates an observed value across cases, so a percentage is unrepresentable
    /// without widening that record — a change visible in review. This test is the cheap
    /// belt-and-braces on the rendered output.
    ///
    /// <para>A token blacklist over words like "most" or "commonly" was considered and rejected: it
    /// fails on the report's own mandated header text, which must quote those very words in order
    /// to forbid them. A name-based guard where a shape-based one exists is a defect class this
    /// repo has already paid for.</para>
    /// </summary>
    [Fact]
    public void Report_NeverRendersAPercentage()
    {
        var markdown = LayoutCorpusReport.Build(Data());

        markdown.ShouldNotContain("%",
            customMessage: "the report rendered a percent sign. ADR 0109 §4 forbids deriving a "
                + "frequency from synthetic data; every number here is a count of authored "
                + "fixtures or of items inside one fixture, never a share of anything real.");
    }

    /// <summary>The three disclaimers are load-bearing text, not decoration: without them a reader
    /// can take a per-case boolean for a population claim, take a mechanic for a vendor, or take a
    /// moved cell for a build failure. Pinned verbatim so a well-meaning edit cannot soften
    /// them.</summary>
    [Fact]
    public void Report_CarriesTheThreeDisclaimersVerbatim()
    {
        var markdown = LayoutCorpusReport.Build(Data());

        markdown.ShouldContain(LayoutCorpusReport.ClaimDiscipline);
        markdown.ShouldContain(LayoutCorpusReport.VendorDiscipline);
        markdown.ShouldContain(LayoutCorpusReport.ObserveOnly);
    }

    /// <summary>Instrument health is rendered as case-id LISTS, never as "n of 16" — that ratio is
    /// itself the N-of-M shape the claim discipline exists to keep out of this file.</summary>
    [Fact]
    public void Report_RendersInstrumentHealthAsCaseIdsNotRatios()
    {
        var markdown = LayoutCorpusReport.Build(Data() with
        {
            Cases = [Observation("case-alpha", byteProofFailure: "expected two columns")],
        });

        markdown.ShouldContain("`case-alpha`");
        markdown.ShouldContain("expected two columns");
        markdown.ShouldContain("**byte proofs held:** none");
    }

    /// <summary>A run with no cases must still render every disclaimer. The emitter is what a
    /// reader trusts when the harness produced nothing.</summary>
    [Fact]
    public void Report_WithNoCases_StillCarriesItsDisclaimers()
    {
        var markdown = LayoutCorpusReport.Build(
            new LayoutCorpusReportData("abc1234", [], [], []));

        markdown.ShouldContain(LayoutCorpusReport.ClaimDiscipline);
        markdown.ShouldContain("**crashed:** none");
    }

    private static LayoutCorpusReportData Data() =>
        new("abc1234", [Observation("case-alpha")], ["ISkillResolver (empty proposals)"], []);

    private static LayoutCaseObservation Observation(string id, string? byteProofFailure = null) =>
        new(
            Case: new LayoutCase(id, "a mechanic", "(b) single-column", "pdf", "cv.pdf",
                "application/pdf", _ => [], CvModel.Swedish, _ => { }, "a byte proof", true),
            ByteProofFailure: byteProofFailure,
            FixtureProblems: [],
            KindResolved: true,
            ExtractionStatus: Application.Resumes.Abstractions.CvExtractionStatus.Extracted,
            CharCount: 100, LineCount: 10, BlankLineCount: 0, SegmentRan: true,
            DetectedLanguage: "Sv", HeadingsDetected: 4, PreambleChars: null,
            ConfidenceOverall: "Confident", SectionEvidence: ["Experience: Confident — 1 entries"],
            ParsedExperience: 1, ParsedEducation: 1,
            GroundTruthExperience: 5, GroundTruthEducation: 3,
            PromotedExperience: 1, PromotedEducation: 1, WellFormedPromotedExperience: 1,
            BlockReason: null, Promoted: true,
            Gates: GateLadder.From(null, true, false, false, false),
            Markers: [],
            CrossSectionContamination: [],
            SummaryContainsUnknownHeading: null, UnknownHeadingIsOwnSection: null,
            PromotedSummaryChars: null, CrashedWithExceptionType: null,
            Verdict: FidelityVerdict.PromotedLossy);
}
