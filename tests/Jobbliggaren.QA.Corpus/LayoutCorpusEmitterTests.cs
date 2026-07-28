using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.QA.Corpus.Generation;
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

    /// <summary>
    /// The percent guard has one hole an emitter-only test cannot see: a byte-proof failure message
    /// is composed at the proof site and rendered VERBATIM into the artifact. So the guard is
    /// re-run over the real messages the real proof helpers produce, by forcing each one to fail.
    /// A guard that does not cover its own call site is this repo's own recorded defect class.
    /// </summary>
    [Fact]
    public void ByteProofFailureMessages_NeverCarryAPercentSign()
    {
        // One single-column document (no gutter) and one two-column document (a gutter), so every
        // helper can be driven to its own failing branch.
        var single = new ByteProofContext(
            "guard-single", Layout.Generation.QuestPdfCvRenderer.SingleColumn(CvModel.Swedish));
        var twoColumn = new ByteProofContext(
            "guard-2col", Layout.Generation.QuestPdfCvRenderer.SidebarEmittedFirst(CvModel.Swedish));

        // Each of these is authored to FAIL, so the message it composes is the one a real run
        // would render into the artifact.
        var provocations = new (string What, Action Provoke)[]
        {
            ("gutter required, none present", () => single.RequireVerticalGutter(10_000)),
            ("no gutter required, one present", () => twoColumn.RequireNoVerticalGutter(0.0001)),
            ("shared baselines required", () => single.RequireSharedBaselines(200, 10_000)),
            ("fused word required", () => single.RequireDigitLetterFusedWord()),
            // A word authored at the BOTTOM of the page, so the top-of-page requirement genuinely
            // fails. Using the person's name here would PASS: it really is at the top.
            ("word not near top", () => single.RequireWordNearPageTop("Bokhyllan", 0.25)),
            ("word absent entirely", () => single.RequireWordNearPageTop("NoSuchWord", 0.25)),
            ("plain requirement", () => single.Require(false, "a plain requirement")),
        };

        var messages = new List<string>();
        foreach (var (what, provoke) in provocations)
        {
            var thrown = Record.Exception(provoke);
            thrown.ShouldBeOfType<ByteProofException>(
                $"the provocation '{what}' did not fail, so its message was never composed and "
                + "this guard would silently cover one fewer call site");
            messages.Add(thrown.Message);
        }

        messages.ShouldNotBeEmpty();
        foreach (var message in messages)
            message.ShouldNotContain("%", customMessage: $"byte-proof message: {message}");
    }

    /// <summary>
    /// The highest-priority PII control in this PR, measured rather than promised. One case authors
    /// a synthetic personnummer in the CV body and one in the account display name, precisely so
    /// the personnummer gates fire; the report must report that they fired without ever carrying
    /// the value. Asserted over the WHOLE lexicon list, because that list is what every existing
    /// leak sweep in this project enumerates — a value added there is covered here for free.
    /// </summary>
    [Fact]
    public void Report_NeverRendersASynthethicPersonnummer()
    {
        var pnrCase = Observation("pdf-pnr-bearing") with
        {
            Case = Observation("pdf-pnr-bearing").Case with
            {
                Model = CvModel.Swedish with
                {
                    SyntheticPersonnummer = SwedishCorpusLexicon.FakePersonnummer[0],
                },
                AccountDisplayName = "Konto Kontosson " + SwedishCorpusLexicon.FakePersonnummer[1],
            },
        };

        var markdown = LayoutCorpusReport.Build(Data() with { Cases = [pnrCase] });

        foreach (var pnr in SwedishCorpusLexicon.FakePersonnummer)
        {
            markdown.ShouldNotContain(pnr,
                customMessage: "a personnummer reached the artifact. The report must record that "
                    + "the guard fired, never the value that made it fire (CLAUDE.md §5).");
        }

        // ...and it must still say the gate was exercised, or the assertion above is satisfied by
        // a report that simply omits the case.
        markdown.ShouldContain("synthetic, not printed");
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

    /// <summary>Every reachable handler state must produce a ladder that names exactly ONE blocked
    /// rung and is well-formed. Subject is <see cref="GateLadder"/> — the corpus's own derivation
    /// (assert-rule category (b)), never a product outcome.
    ///
    /// <para>This is the pin the defect proved was missing. #1060 PR C added
    /// <c>PersonnummerInAccountName</c> and this file had nothing that noticed; the token fell to a
    /// catch-all that printed five `no verdict` cells, and the suite stayed green because
    /// <c>IsWellFormed</c> accepted them.</para></summary>
    [Theory]
    [MemberData(nameof(ReachableGateStates))]
    public void Ladder_ForEveryReachableBlock_NamesOneRungAndIsWellFormed(
        AutoPromoteBlockReason reason, bool pnrOnParse, bool pnrInLabel, int expectedBlockedRung)
    {
        var ladder = GateLadder.From(reason, promoted: false, promoteFaulted: false,
            pnrFoundOnParse: pnrOnParse, pnrInResolvedLabel: pnrInLabel);

        GateLadder.IsWellFormed(ladder).ShouldBeTrue(
            $"the ladder for {reason} is malformed — most likely GateLadder has no arm for it, "
            + "which would publish an instrument gap as a statement about the handler.");

        ladder.Select(c => c.State).ShouldContain(GateState.Blocked);
        ladder.Count(c => c.State == GateState.Blocked).ShouldBe(1);
        ladder[expectedBlockedRung].State.ShouldBe(GateState.Blocked);
        ladder.ShouldNotContain(c => c.State == GateState.NoVerdict);
    }

    /// <summary>The exhaustiveness half, and it is what makes the theory above a MECHANISM rather
    /// than four instances: every declared enum member must appear in the case list. A fifth member
    /// fails HERE, at the enum, before anyone has to notice a wrong cell in a 962-line artifact.
    /// No count is written down — the same reason
    /// <c>AutoPromoteBlockReason_IsTheLockedFourMemberSet</c> writes none.</summary>
    [Fact]
    public void ReachableGateStates_CoversEveryDeclaredBlockReason()
    {
        var covered = ReachableGateStates()
            .Select(row => row.Data.Item1)
            .ToHashSet();

        covered.ShouldBe(Enum.GetValues<AutoPromoteBlockReason>().ToHashSet(), ignoreOrder: true);
    }

    /// <summary>`unresolved` and `no verdict` must not render as the same word. They are different
    /// claims — one is about THIS FILE, the other about the handler — and printing the second for
    /// the first is the whole defect. Asserted on the case's own ROW, never on the document: §5's
    /// prose now explains both words, so a document-level ShouldContain would pass on the
    /// explanation while the cell said something else.</summary>
    [Fact]
    public void Report_RendersUnresolvedDistinctlyFromNoVerdict()
    {
        RowWords(GateState.Unresolved).ShouldContain("unresolved");
        RowWords(GateState.Unresolved).ShouldNotContain("no verdict");
        RowWords(GateState.NoVerdict).ShouldContain("no verdict");
        RowWords(GateState.NoVerdict).ShouldNotContain("unresolved");

        static string RowWords(GateState state)
        {
            var markdown = LayoutCorpusReport.Build(Data() with
            {
                Cases =
                [
                    Observation("case-ladder") with
                    {
                        Gates = [new GateCell("G1 pnr(parse)", "pnr on parse", state)],
                    },
                ],
            });

            // Only the case's own table rows carry its id; the prose paragraphs do not.
            return string.Join("\n", markdown.Split('\n').Where(l => l.Contains("`case-ladder`")));
        }
    }

    /// <summary>An unresolved rung is an INTEGRITY failure, not a quieter third colour — so it
    /// reaches §0's list and <c>LayoutCorpusReportTests</c>'s existing instrument assert. Without
    /// this, an all-unresolved ladder passes exactly as the all-`NoVerdict` one did: nothing is
    /// "passed after a stop".</summary>
    [Fact]
    public void IsWellFormed_RejectsAnUnresolvedRung()
    {
        GateLadder.IsWellFormed([new GateCell("G1", "pnr on parse", GateState.Unresolved)])
            .ShouldBeFalse();

        LayoutCorpusReport.Build(Data() with
        {
            Cases =
            [
                Observation("case-ladder") with
                {
                    Gates = [new GateCell("G1", "pnr on parse", GateState.Unresolved)],
                },
            ],
        }).ShouldContain("**gate ladder malformed:** `case-ladder`");
    }

    public static TheoryData<AutoPromoteBlockReason, bool, bool, int> ReachableGateStates() =>
        new()
        {
            // reason, pnr on parse, pnr in resolved label, index of the rung that must be BLOCKED.
            { AutoPromoteBlockReason.PersonnummerPresent, true, false, 0 },   // G1  parse flag
            { AutoPromoteBlockReason.ParseNotConfident, false, false, 1 },    // G2  confidence
            { AutoPromoteBlockReason.PersonnummerPresent, false, true, 2 },   // G2b label scan
            { AutoPromoteBlockReason.PersonnummerInAccountName, false, false, 3 }, // G3a DQ6
            { AutoPromoteBlockReason.IncompleteContent, false, false, 4 },    // G3b buildability
        };

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
            PersonnummerFoundOnParse: false,
            FirstExtractedLine: "Anna Andersson",
            ContainsFusedPeriodRole: false,
            AnyLineCarriesBothColumns: false,
            ExtractedTextDigest: "ABCDEF012345",
            ParsedFreeSectionHeadings: [],
            ParsedExperience: 1, ParsedEducation: 1,
            GroundTruthExperience: 5, GroundTruthEducation: 3,
            PromotedExperience: 1, PromotedEducation: 1, PromotedExperienceWithRawPeriod: 1,
            PromotedPreambleChars: null,
            BlockReason: null, Promoted: true,
            Gates: GateLadder.From(null, true, false, false, false),
            Markers: [],
            CrossSectionContamination: [],
            SummaryContainsRenderedProjectHeading: null,
            RenderedProjectHeadingIsOwnSection: null,
            PromotedSummaryChars: null,
            PromoteFailureCode: null,
            CrashedWithExceptionType: null,
            Verdict: FidelityVerdict.PromotedLossy);
}
