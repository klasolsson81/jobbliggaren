using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.QA.Corpus.Generation;
using Jobbliggaren.QA.Corpus.Harness;
using Jobbliggaren.QA.Corpus.Layout;
using Jobbliggaren.QA.Corpus.Reporting;
using Shouldly;

namespace Jobbliggaren.QA.Corpus;

/// <summary>
/// Guards on the corpus's OWN machinery, not on the product: the report emitter, and — since
/// 2026-07-28 — the harness derivations behind it (<see cref="Harness.GateLadder"/>,
/// <see cref="Harness.LayoutChainRunner"/>'s period count). Legitimate hard asserts under the
/// corpus's assert rule, category (b)/(c): nothing in <c>src/</c> can move any of them.
///
/// <para><b>ONE assert in this file deliberately falls outside those categories</b> —
/// <see cref="ReachableGateStates_CoversEveryDeclaredBlockReason"/>, whose subject is the
/// production enum <c>AutoPromoteBlockReason</c>. It is argued at its own docblock rather than
/// waved through here, which is the convention <c>LayoutCorpusReportTests</c> already uses for
/// its four production-touching asserts. Stated at the class level because the earlier version
/// of this paragraph claimed nothing here could be moved by <c>src/</c>, and that stopped being
/// true the moment the assert landed.</para>
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

    /// <summary>Every BLOCK this corpus can observe must produce a ladder that names exactly one
    /// blocked rung and is well-formed. Subject is <see cref="GateLadder"/> — the corpus's own
    /// derivation (assert-rule category (b)), never a product outcome.
    ///
    /// <para>Scope, stated because the earlier wording said "handler state" and overreached:
    /// <c>Resolve</c> has three branches and this covers the third. The fault branch (all
    /// <see cref="GateState.NoVerdict"/>) and the promoted branch (all
    /// <see cref="GateState.Passed"/>) name no blocked rung by construction and are pinned
    /// separately below. And one row here — the label-scan arm — is reachable in PRODUCTION (a
    /// user-typed <c>NameOverride</c>) but not in this corpus, which always resolves the label
    /// from a generated default that carries no personnummer.</para>
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
    /// fails HERE, at the enum, before anyone has to notice one wrong cell in an ~800-line
    /// artifact. No count is written down — the same reason
    /// <c>AutoPromoteBlockReason_IsTheLockedFourMemberSet</c> writes none.
    ///
    /// <para>Its subject is <c>AutoPromoteBlockReason</c>, a PRODUCTION type, so it sits outside the
    /// assert rule's three categories and is argued rather than assumed — the convention
    /// <c>LayoutCorpusReportTests</c> already uses for its four production-touching asserts. The
    /// subject is the type's DECLARED surface, not anything the chain produced from a document; it
    /// cannot be moved by a parsing change, only by someone adding a gate. Red then is the correct
    /// answer, and the remedy is one arm in <c>GateLadder</c>.</para></summary>
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

            // TABLE ROWS only. Filtering on the case id alone was sound but for the wrong reason:
            // five prose lines DO carry a case id (§0's id lists, §1's free-text mechanics, §4b,
            // §6), so the comment "only table rows carry its id" was false. The leading pipe makes
            // the filter true by construction instead of by luck.
            return string.Join("\n", markdown.Split('\n')
                .Where(l => l.StartsWith('|') && l.Contains("`case-ladder`")));
        }
    }

    /// <summary>An unresolved rung is an INTEGRITY failure, not a quieter third colour — so it
    /// reaches §0's list and <c>LayoutCorpusReportTests</c>'s existing instrument assert. Without
    /// this, an all-unresolved ladder passes exactly as the all-`NoVerdict` one did: nothing is
    /// "passed after a stop".</summary>
    [Fact]
    public void IsWellFormed_RejectsAnUnresolvedRung()
    {
        // Built through From(...), not hand-assembled: the rule and the production of the state it
        // rejects are then pinned by the same test, and neither can drift away from the other.
        GateLadder.IsWellFormed(GateLadder.From(null, false, false, false, false)).ShouldBeFalse();

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

    /// <summary>The UNMAPPED-token path — the one line this PR's whole claim rests on, and the one
    /// nothing was running.
    ///
    /// <para>The earlier mutation round looked like it covered this and did not: deleting the DQ6
    /// arm kills a test by ROUTING THROUGH the catch-all, which proves a fall-through exists and
    /// says nothing about what the catch-all returns. Flipping <c>_ =&gt;</c> back to
    /// <see cref="GateState.NoVerdict"/> stayed green. That is the repo's own recorded lesson —
    /// pin the CALL SITE, not only the rule — reproduced inside the PR that cites it.</para>
    ///
    /// <para><b>The path is a DEFENSIVE arm behind a closed union, and calling it "reachable" would
    /// be true of its evidence and false of its subject.</b> <c>CvChainProbe</c>'s outcome switch
    /// does carry <c>_ =&gt; (block: null, promoted: false, faulted: false)</c> — but
    /// <c>AutoPromoteOutcome</c> is a CLOSED discriminated union (private constructor, exactly
    /// <c>Promoted</c> and <c>LeftPending</c>, "nothing outside this file can add a case"), so that
    /// arm cannot be entered today, for the same reason the ladder's own <c>_</c> cannot.</para>
    ///
    /// <para><b>There is a SECOND ingress, and it is not the union opening.</b>
    /// <c>PersonnummerPresent</c> with both discriminators false also lands here — the handler said
    /// the gate fired and neither observable the corpus recomputes agrees. That is a DIVERGENCE
    /// between product and instrument, and it is exactly the case where a confident "DQ6 blocked"
    /// used to be printed. Naming one ingress and implying it is the only one would be this PR's
    /// own defect class.</para>
    ///
    /// <para>Pinning it anyway is the point: it fixes what the catch-all ANSWERS before either
    /// ingress opens, and PR C is this corpus's measured proof that closure expires. The mutation is
    /// the evidence — flipping the arm to <see cref="GateState.NoVerdict"/> was green until this
    /// test existed.</para></summary>
    [Fact]
    public void Ladder_ForAnUnmappedOutcome_IsUnresolvedAndNeverAFault()
    {
        var ladder = GateLadder.From(
            block: null, promoted: false, promoteFaulted: false,
            pnrFoundOnParse: false, pnrInResolvedLabel: false);

        // Exact sequence, not ShouldAllBe: that predicate is vacuously true on an empty list, so it
        // would pass a From that returned nothing — the same vacuity the fault-branch test was
        // corrected for. Killed by polarity is not killed by design.
        ladder.Select(c => c.State)
            .ShouldBe(Enumerable.Repeat(GateState.Unresolved, GateLadder.RungHeaders.Count));
        GateLadder.IsWellFormed(ladder).ShouldBeFalse();
    }

    /// <summary>The two branches <see cref="Ladder_ForEveryReachableBlock_NamesOneRungAndIsWellFormed"/>
    /// cannot cover, because neither names a blocked rung. Both were unpinned.
    ///
    /// <para>Asserted as an exact SEQUENCE, not with <c>ShouldAllBe</c>: that predicate is vacuously
    /// true on an empty list and <c>IsWellFormed([])</c> returns true, so a <c>From</c> that returned
    /// nothing would pass both halves while the artifact rendered five silent empty cells. The shape
    /// is not hypothetical — <c>LayoutChainRunner</c> already constructs <c>Gates: []</c> for a
    /// crashed case.</para></summary>
    [Fact]
    public void Ladder_ForAFaultAndForAPromote_AreWellFormedAndDistinct()
    {
        var rungs = GateLadder.From(null, true, false, false, false).Count;
        rungs.ShouldBe(5);

        var faulted = GateLadder.From(null, promoted: false, promoteFaulted: true, false, false);
        faulted.Select(c => c.State).ShouldBe(Enumerable.Repeat(GateState.NoVerdict, rungs));
        GateLadder.IsWellFormed(faulted).ShouldBeTrue();

        var promoted = GateLadder.From(null, promoted: true, promoteFaulted: false, false, false);
        promoted.Select(c => c.State).ShouldBe(Enumerable.Repeat(GateState.Passed, rungs));
        GateLadder.IsWellFormed(promoted).ShouldBeTrue();
    }

    /// <summary>Every <see cref="GateState"/> must render as its OWN word. The renderer is where
    /// this instrument speaks to a reader, and every surviving mutation this lane has found across
    /// three PRs was on a read or render surface — so the mapping is pinned wholesale rather than
    /// one arm at a time.</summary>
    [Fact]
    public void Report_RendersEveryGateStateAsADistinctWord()
    {
        var words = Enum.GetValues<GateState>().ToDictionary(s => s, RenderedCell);

        words[GateState.Passed].ShouldBe("passed");
        words[GateState.Blocked].ShouldBe("**BLOCKED**");
        words[GateState.NotEvaluated].ShouldBe("not evaluated");
        words[GateState.NoVerdict].ShouldBe("no verdict");
        words[GateState.Unresolved].ShouldBe("unresolved");

        words.Values.Distinct().Count().ShouldBe(words.Count,
            "two GateStates render as the same word, so the artifact cannot say which one it means.");
    }

    /// <summary>Under GFM a delimiter row whose cell count differs from its header's means the block
    /// is not a table at all — the whole section renders as raw pipes. This emitter has shipped that
    /// once already (§5's hardcoded delimiter, one cell short).
    ///
    /// <para>Written over the DOCUMENT, not over one section, and that is the point: §5's delimiter
    /// is DERIVED and therefore the least likely of the ten to drift, while the other nine pair two
    /// hand-written literals — the exact shape that shipped the bug. An earlier revision of this test
    /// guarded only §5, which left §2, "the headline", open to the same failure.</para></summary>
    [Fact]
    public void Report_EveryTableDelimiterMatchesItsHeaderCellCount()
    {
        var lines = LayoutCorpusReport.Build(Data()).Split('\n');
        var tablesChecked = 0;

        for (var i = 1; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("|---", StringComparison.Ordinal))
                continue;

            tablesChecked++;
            Cells(lines[i]).ShouldBe(Cells(lines[i - 1]),
                $"line {i + 1}: the delimiter does not match its header, so GFM renders this block "
                + $"as raw pipes.\n  header: {lines[i - 1]}\n  delim:  {lines[i]}");

            // The THIRD invariant, and the one the other two cannot see. A row shorter than its
            // header is still a table — GFM pads it, at the END — so every value after the missing
            // cell shifts one column LEFT and lands under a heading that is not its own. Dropping
            // one gate cell would move each row's verdict under the previous rung's name on every
            // row of the real 21-case baseline, with header and delimiter both agreeing.
            if (i + 1 < lines.Length && lines[i + 1].StartsWith('|'))
            {
                Cells(lines[i + 1]).ShouldBe(Cells(lines[i - 1]),
                    $"line {i + 2}: the first data row has a different cell count from its header, "
                    + $"so its values render under the wrong headings.\n  header: {lines[i - 1]}"
                    + $"\n  row:    {lines[i + 1]}");
            }
        }

        // Non-vacuity: an emitter that stopped printing tables would otherwise pass in silence.
        // Eight, not ten — §7's table is inside a conditional and §8's P5 table needs both the
        // Swedish and English single-column cases, neither of which this fixture carries. The
        // floor makes that shortfall explicit instead of letting it read as full coverage.
        tablesChecked.ShouldBeGreaterThanOrEqualTo(8);
    }

    /// <summary>§2 is the headline table and its cells are POSITIONAL. Every number below is one the
    /// fixture supplied, so nothing about the product is asserted — the subject is whether the
    /// emitter puts each value under its own heading.
    ///
    /// <para>All seven counts differ deliberately. The default observation carries repeated 1s, so a
    /// transposition of two adjacent columns is invisible to any test built on it — and a column
    /// wearing another column's name is precisely the defect this PR renamed
    /// <c>WellFormedPromotedExperience</c> to remove. The rename closed it by naming; this closes it
    /// by position.</para></summary>
    [Fact]
    public void Report_FidelityVerdictRow_PutsEachCountUnderItsOwnHeading()
    {
        var markdown = LayoutCorpusReport.Build(Data() with
        {
            Cases =
            [
                Observation("case-alpha") with
                {
                    GroundTruthExperience = 5, ParsedExperience = 4, PromotedExperience = 3,
                    PromotedExperienceWithRawPeriod = 2,
                    GroundTruthEducation = 9, ParsedEducation = 8, PromotedEducation = 7,
                },
            ],
        });

        var (header, row) = TableRow(markdown, "| # | Case | Verdict | ");

        Cell(header, row, "GT emp").ShouldBe("5");
        Cell(header, row, "Parsed exp").ShouldBe("4");
        Cell(header, row, "Promoted exp").ShouldBe("3");
        Cell(header, row, "With period").ShouldBe("2");
        Cell(header, row, "GT edu").ShouldBe("9");
        Cell(header, row, "Parsed edu").ShouldBe("8");
        Cell(header, row, "Promoted edu").ShouldBe("7");
    }

    /// <summary>A CRASHED case — the one shape whose ladder is empty by construction
    /// (<c>LayoutChainRunner</c> builds <c>Gates: []</c> for it) — must not take §5's table down
    /// with it, and must appear in §0's three instrument-health lists.
    ///
    /// <para>It is one fixture closing four separate holes, because all four need the same case that
    /// no previous fixture carried: §5's headings used to be read off <c>Cases[0]</c>, so a crashed
    /// FIRST case gave a six-cell header over a five-cell delimiter and GFM dropped the entire
    /// section — invisible to the document-wide delimiter guard, which only ever saw a fixture whose
    /// single case had a full ladder. And §0's `crashed` and `fixture invalid` lines could be
    /// inverted green for the same reason: nothing ever handed the emitter a case that was
    /// either.</para>
    ///
    /// <para>The hand-set <c>Gates: []</c> is the shape <c>LayoutChainRunner.Crashed</c> genuinely
    /// produces, and that producer is pinned elsewhere by
    /// <see cref="Crashed_CarriesTheByteProofFailureItWasGiven"/> — named here because §5's worked
    /// form is that the seam names the pin when it lives in another file.</para></summary>
    [Fact]
    public void Report_WithACrashedFirstCase_StillRendersTheGateLadderAndNamesItInSection0()
    {
        var markdown = LayoutCorpusReport.Build(Data() with
        {
            Cases =
            [
                Observation("case-crashed") with
                {
                    CrashedWithExceptionType = "InvalidOperationException",
                    Gates = [],
                    FixtureProblems = ["the model cannot carry the marker oracle"],
                },
                Observation("case-alpha"),
            ],
        });

        var lines = markdown.Split('\n');

        // §5 is still a table: the delimiter matches the header (the actual GFM constraint —
        // asserted as the invariant rather than against a hand-computed column count, which is how
        // the previous delimiter guard came to be one cell short of its own header), and the header
        // carries EVERY canonical rung rather than whatever the first case happened to hold.
        var (header, row) = TableRow(markdown, "| # | Case | G1 ");
        Cells(lines[Array.IndexOf(lines, header) + 1]).ShouldBe(Cells(header));

        foreach (var rung in GateLadder.RungHeaders)
            header.ShouldContain(rung);

        // ...and the crashed row occupies every gate column without claiming a verdict in any.
        // The WIDTH is asserted too, not only one cell: the em-dash branch derives its count from
        // the same variable as the header, but "derived" is not a guard — emitting one dash fewer
        // survives a single-cell read, and the document-wide row check never sees this branch
        // because its fixture takes the other one.
        Cells(row).ShouldBe(Cells(header));
        Cell(header, row, GateLadder.RungHeaders[3]).ShouldBe("—");
        row.ShouldNotContain("no verdict");

        markdown.ShouldContain("**crashed:** `case-crashed`");
        markdown.ShouldContain("**fixture invalid:** `case-crashed`");
        markdown.ShouldContain("**byte proofs held:** `case-crashed`, `case-alpha`");
    }

    /// <summary>The `(false, true)` arm of the authored-personnummer column: a CLEAN body whose
    /// ACCOUNT name carries one. Unexercised until now, and the row it would have mislabelled is
    /// `pdf-clean-body-pnr-in-account-name` — the only case that reaches the DQ6 rung. "pnr
    /// authored: none" beside a blocked DQ6 cell is this PR's own incident #2, on its own case.
    /// The value itself is never printed; the arm says only that one was authored and where.</summary>
    [Fact]
    public void Report_ForACleanBodyWithAPersonnummerInTheAccountName_SaysWhereItWasAuthored()
    {
        var clean = Observation("case-account-pnr");
        var markdown = LayoutCorpusReport.Build(Data() with
        {
            Cases =
            [
                clean with
                {
                    Case = clean.Case with
                    {
                        Model = CvModel.Swedish with { SyntheticPersonnummer = null },
                        AccountDisplayName = "Konto Kontosson "
                            + SwedishCorpusLexicon.FakePersonnummer[1],
                    },
                },
            ],
        });

        var (header, row) = TableRow(markdown, "| Case | Confidence overall | ");
        Cell(header, row, "pnr authored (body / account)")
            .ShouldBe("account name (synthetic, not printed)");

        foreach (var pnr in SwedishCorpusLexicon.FakePersonnummer)
            markdown.ShouldNotContain(pnr);
    }

    private static int Cells(string row) => row.Split('|').Length;

    /// <summary>A table's header and its first data row, located by the header's opening text. The
    /// row is <c>header + 2</c> because the delimiter sits between them.</summary>
    private static (string Header, string Row) TableRow(string markdown, string headerPrefix)
    {
        var lines = markdown.Split('\n');
        var i = Array.FindIndex(lines, l => l.StartsWith(headerPrefix, StringComparison.Ordinal));
        i.ShouldBeGreaterThan(-1, $"no table header starting '{headerPrefix}' — the emitter moved.");

        return (lines[i], lines[i + 2]);
    }

    /// <summary>Reads a cell BY ITS HEADING rather than by index, which is what makes the assertion
    /// mean "under its own heading" instead of "at position six".</summary>
    private static string Cell(string header, string row, string heading)
    {
        var columns = header.Split('|').Select(c => c.Trim()).ToList();
        var index = columns.IndexOf(heading);
        index.ShouldBeGreaterThan(-1, $"no column headed '{heading}'. Headers: {header}");

        return row.Split('|')[index].Trim();
    }

    /// <summary>The rung ORDER is the gate order, and §5's cells are positional — a swap would
    /// publish one gate's verdict under another's heading while every index-based assertion in this
    /// file stayed green.</summary>
    [Fact]
    public void Ladder_RungsAreInGateOrder()
    {
        GateLadder.From(null, true, false, false, false).Select(c => c.GateId).ShouldBe(
            ["G1 pnr(parse)", "G2 confidence", "G2b pnr(label)", "G3a pnr(DQ6)", "G3b buildability"]);
    }

    /// <summary>The corpus's own period-presence predicate (assert-rule category (b)). Inline in
    /// <c>RunAsync</c> it was unreachable by any test, because the suite asserts no promoted count
    /// and the artifact is the only surface it reaches — so a mutation of it survived everything.
    /// Asymmetric on BOTH axes, and the second one is why the first fixture was not enough: it
    /// varied only <c>RawPeriod</c>, the axis that was never wrong, while holding Company and Role
    /// non-blank on every entry — so re-adding the over-conjunction that actually shipped still
    /// produced the expected count and survived. Over-conjunction is the regression; the blank
    /// entry is what catches it.</summary>
    [Fact]
    public void CountWithRawPeriod_CountsOnlyEntriesCarryingAPeriod()
    {
        LayoutChainRunner.CountWithRawPeriod(
        [
            // Blank Company and Role ON PURPOSE. `ValidateContent` refuses this entry, so it is not
            // a promotable one — and it does not need to be. The subject is the corpus's own
            // counting predicate (assert-rule category (b)), never a promoted CV, and nothing here
            // asserts anything about promotion. Without this entry, re-adding the
            // `!IsNullOrWhiteSpace(Role) && !IsNullOrWhiteSpace(Company)` conjuncts is invisible.
            new Experience("", "", null, null, null, "2019 - 2021"),
            new Experience("Acme", "Utvecklare", null, null, null, "2021 - 2026"),
            new Experience("Klarna", "Utvecklare", null, null, null, null),
        ]).ShouldBe(2);
    }

    private static string RenderedCell(GateState state)
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

        // Anchored on §5's HEADER, not on the row prefix: four sections render a row starting
        // `| 1 | \`case-ladder\``, so a prefix match reads whichever one comes first and would
        // silently measure §1's CTO-class cell instead of a gate cell.
        var (header, row) = TableRow(markdown, "| # | Case | G1 ");
        return Cell(header, row, GateLadder.RungHeaders[0]);
    }

    /// <summary>The ORDER half of <see cref="GateLadder.IsWellFormed"/> — a rung reported as passed
    /// after one that was never evaluated, which no run of the gate can produce.
    ///
    /// <para>It was pinned by NOTHING, and the way that surfaced is worth recording: a mutation of
    /// that arm did not compile (removing its only reader leaves <c>stopped</c> assigned and never
    /// used, and this repo treats that warning as an error), so the harness reported UNMEASURED
    /// rather than a verdict. An unmeasurable arm reads exactly like a covered one in a table that
    /// only lists KILLED and SURVIVED.</para>
    ///
    /// <para>The ladders are hand-built, and that is the FAITHFUL input class here rather than a
    /// convenient one: this arm's declared purpose is to catch a future hand-edited ladder, so
    /// shapes <c>Resolve</c> cannot emit are exactly what it exists for. A test driven from
    /// <c>Resolve</c>'s output would be vacuous by the same docblock's next sentence.</para></summary>
    [Fact]
    public void IsWellFormed_RejectsAPassedRungAfterAnUnevaluatedOne()
    {
        GateLadder.IsWellFormed(
        [
            new GateCell(GateLadder.G1, "pnr on parse", GateState.Passed),
            new GateCell(GateLadder.G2, "confidence", GateState.NotEvaluated),
            new GateCell(GateLadder.G2b, "pnr in label", GateState.Passed),
        ]).ShouldBeFalse();

        // The control: the same shape WITHOUT the impossible resumption is well-formed, so the
        // assertion above cannot be satisfied by a guard that simply rejects everything.
        GateLadder.IsWellFormed(
        [
            new GateCell(GateLadder.G1, "pnr on parse", GateState.Passed),
            new GateCell(GateLadder.G2, "confidence", GateState.NotEvaluated),
            new GateCell(GateLadder.G2b, "pnr in label", GateState.NotEvaluated),
        ]).ShouldBeTrue();
    }

    /// <summary>A crashed case must carry the byte-proof failure it was given, not a hardcoded null.
    ///
    /// <para>The second crash exit runs AFTER the byte proof, so a case whose authored bytes were
    /// already wrong and which then crashed was published under "byte proofs held" with its message
    /// discarded. Pinned HERE rather than through the emitter, because the emitter's own fixture can
    /// set both fields directly and would stay green with the parameter removed — measured: that
    /// mutation survived a full sweep. The subject is the corpus's own factory, category (b).</para>
    /// </summary>
    [Fact]
    public void Crashed_CarriesTheByteProofFailureItWasGiven()
    {
        var observation = LayoutChainRunner.Crashed(
            Observation("case-crashed").Case, [], "InvalidOperationException",
            byteProofFailure: "expected two columns");

        observation.ByteProofFailure.ShouldBe("expected two columns");
        observation.CrashedWithExceptionType.ShouldBe("InvalidOperationException");
        observation.Gates.ShouldBeEmpty();
    }

    // ===============================================================
    // The Domain code column (#1060 D3(β) PR 2). Subject: the EMITTER — assert-rule
    // category (c). Nothing here asserts what the product decided.
    // ===============================================================

    /// <summary>
    /// The code reaches §5 and reaches §5 ONLY (CTO-bind D.3). The negative half is the load-
    /// bearing one: §2 is the headline verdict table with eleven columns already, and a twelfth
    /// diagnostic column would give the detail two homes in one document. A `Contains` over the
    /// whole report would pass with the column in both places, so this splits the sections first.
    /// </summary>
    [Fact]
    public void Report_ForABuildabilityBlock_PublishesTheDomainCodeInSectionFiveOnly()
    {
        var report = LayoutCorpusReport.Build(new LayoutCorpusReportData(
            "abc1234",
            [Observation("case-unbuildable",
                blockReason: AutoPromoteBlockReason.IncompleteContent, promoted: false,
                domainErrorCode: "Resume.ExperienceCompanyRequired")],
            [], []));

        Section(report, "## 5. Gate ladder").ShouldContain("Resume.ExperienceCompanyRequired");
        Section(report, "## 2. Fidelity verdict").ShouldNotContain("Resume.ExperienceCompanyRequired");
        Section(report, "## 5. Gate ladder").ShouldContain("| Domain code |");
    }

    /// <summary>
    /// An em-dash means "no Domain refusal produced a code", and it is what a POLICY block and a
    /// PROMOTE both print — neither asked the Domain the question. Pinned because the column
    /// would otherwise be free to invent a value for a row where none exists.
    /// </summary>
    [Fact]
    public void Report_ForAPolicyBlockAndAPromote_RendersTheDomainCodeAsAnEmDash()
    {
        var report = LayoutCorpusReport.Build(new LayoutCorpusReportData(
            "abc1234",
            [
                Observation("case-pnr",
                    blockReason: AutoPromoteBlockReason.PersonnummerPresent, promoted: false),
                Observation("case-promoted"),
            ],
            [], []));

        var ladder = Section(report, "## 5. Gate ladder");
        foreach (var id in new[] { "case-pnr", "case-promoted" })
        {
            // The ROW, not the section. Two things forced this and both were measured by this
            // test failing: §5's own glossary contains the string "INSTRUMENT: unreadable" by
            // design (it is the paragraph that DEFINES it), and §5 holds TWO tables — the case
            // id also appears in the Observed-Domain-state rows below the ladder.
            var row = LadderRow(ladder, id);
            row.ShouldNotContain("INSTRUMENT");

            // Cell-level: the Domain-code column is the one immediately after FIRST BLOCK, and
            // the row's trailing cells are `— | — | yes/no`. Counting the em-dashes in the row
            // would also pass on a row that lost the column, so read the cell by position.
            var cells = row.Split('|').Select(x => x.Trim()).ToList();
            var promoteFaultIndex = cells.Count - 3;
            cells[promoteFaultIndex - 1].ShouldBe("—");
        }
    }

    /// <summary>
    /// A reading failure is an INSTRUMENT fact and must never wear the em-dash that means "no
    /// code". This is the same argument <c>GateState.Unresolved</c> was created on: before it,
    /// a gap in this file was narrated as something the product did. §0 names the case, so the
    /// artifact's own health block carries it rather than a reader having to spot the cell.
    /// </summary>
    [Fact]
    public void Report_WhenTheBlockDetailCouldNotBeRead_SaysSoAndNamesTheCaseInSectionZero()
    {
        var report = LayoutCorpusReport.Build(new LayoutCorpusReportData(
            "abc1234",
            [Observation("case-unreadable",
                blockReason: AutoPromoteBlockReason.IncompleteContent, promoted: false,
                blockDetailUnreadable: true)],
            [], []));

        Section(report, "## 5. Gate ladder").ShouldContain("**INSTRUMENT: unreadable**");
        Section(report, "## 0. Instrument integrity")
            .ShouldContain("**block detail unreadable:** `case-unreadable`");
    }

    /// <summary>The healthy counterpart: with nothing unreadable, §0 says so in the same
    /// `none` form the other four health lines use. Without this, the line above could be
    /// satisfied by an emitter that prints the case id unconditionally.</summary>
    [Fact]
    public void Report_WithEveryBlockDetailReadable_ReportsNoneOnThatHealthLine()
    {
        var report = LayoutCorpusReport.Build(Data());

        Section(report, "## 0. Instrument integrity")
            .ShouldContain("**block detail unreadable:** none");
    }

    /// <summary>One case's row from §5's LADDER table specifically. The section carries a second
    /// table (Observed Domain state) keyed by the same case id, so matching on the id alone
    /// returns two lines; the ladder is the one whose first cell is the row number.</summary>
    private static string LadderRow(string ladderSection, string caseId)
    {
        var rows = ladderSection.Split('\n')
            .Where(l =>
            {
                var cells = l.Split('|');
                return cells.Length > 3
                    && int.TryParse(cells[1].Trim(), out _)
                    && string.Equals(cells[2].Trim(), $"`{caseId}`", StringComparison.Ordinal);
            })
            .ToList();

        rows.Count.ShouldBe(1, $"expected exactly one ladder row for '{caseId}'");
        return rows[0];
    }

    /// <summary>The text between one `## ` heading and the next. Used by the tests above so a
    /// claim about §5 cannot be satisfied by a string that only appears in §2.</summary>
    private static string Section(string report, string heading)
    {
        var start = report.IndexOf(heading, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"the report has no '{heading}' section");

        var next = report.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return next < 0 ? report[start..] : report[start..next];
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

    private static LayoutCaseObservation Observation(
        string id,
        string? byteProofFailure = null,
        AutoPromoteBlockReason? blockReason = null,
        bool promoted = true,
        string? domainErrorCode = null,
        bool blockDetailUnreadable = false) =>
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
            BlockReason: blockReason,
            DomainErrorCode: domainErrorCode,
            BlockDetailUnreadable: blockDetailUnreadable,
            Promoted: promoted,
            Gates: GateLadder.From(blockReason, promoted, false, false, false),
            Markers: [],
            CrossSectionContamination: [],
            SummaryContainsRenderedProjectHeading: null,
            RenderedProjectHeadingIsOwnSection: null,
            PromotedSummaryChars: null,
            PromoteFailureCode: null,
            CrashedWithExceptionType: null,
            Verdict: FidelityVerdict.PromotedLossy);
}
