using Jobbliggaren.QA.Corpus.Harness;
using Jobbliggaren.QA.Corpus.Layout;
using Jobbliggaren.QA.Corpus.Reporting;
using Shouldly;

namespace Jobbliggaren.QA.Corpus;

/// <summary>
/// PR K (#1060) — the layout fitness corpus. It authors 21 CV documents as real PDF and DOCX
/// BYTES and drives each of them through the product's own chain: <c>CvFileSignature</c> →
/// <c>ImportResumeCommandHandler</c> (extract, personnummer scan, segment, <c>ParsedResume.Create</c>)
/// → <c>AutoPromoteParsedResumeCommandHandler</c> (five gates, DQ6 among them, the internal content
/// mapper, <c>Resume.CreateFromParsed</c>). No database server, no container, no network.
///
/// <para>Both numerals above were wrong and were corrected 2026-07-28. What follows is the
/// MEASUREMENT rather than a characterisation of it, because two reviewers and I each produced a
/// different characterisation from the same history and all three were wrong somewhere:</para>
/// <code>
/// 980a00d4  16 cases  "sixteen"   &lt;- true when written
/// ccda80d0  17 cases  "sixteen"   &lt;- went false inside PR K's OWN review round
/// d9e0af7f  17 cases  "sixteen"   &lt;- shipped false
/// 7a5496fe  21 cases  "sixteen"   &lt;- PR E drifted it further
/// </code>
/// <para>"six gates" is the simpler kind: true until PR B retired the preamble gate. A numeral
/// beside a catalog anyone can count needs no such archaeology, which is why the count is now a
/// digit — and why this paragraph states four measurements instead of one adjective.</para>
///
/// <para><b>The material difference from the existing corpus.</b> <c>CorpusGenerator</c> starts
/// DOWNSTREAM of the segmenter — it calls <c>ParsedResume.Create</c> with pre-built content and
/// never invokes the segmenter, a fact ADR 0109 §4 records. It therefore could never see the
/// defect that motivated this work. This suite starts UPSTREAM of the extractor, from bytes.</para>
///
/// <para><b>Observe-only</b> (CLAUDE.md §2.5, CTO R3). Not a CI gate. The assert rule is narrow
/// and deliberate:</para>
///
/// <para><i>A hard assert may only take as its subject (a) the bytes this corpus authored, read
/// through a NON-PRODUCTION reader, (b) the corpus's own declarations, or (c) its own emitter.
/// Everything the production chain produces — blank-line counts, heading detection, entry counts,
/// confidence, gate verdicts, promote booleans, content-loss deltas — is RECORDED, never
/// asserted.</i></para>
///
/// <para>That rule is what stops this suite from blocking its own remedy: asserting
/// <c>BlankLineCount == 0</c> would turn every PDF case red the day a boundary fix lands, on a
/// suite whose whole purpose is to measure that fix's effect. (PR E built one and withdrew it;
/// the rule matters exactly as much for the attempt that follows.)</para>
///
/// <para><b>The escape hatch, stated once so it can be cited.</b> An assert whose subject falls
/// outside (a)/(b)/(c) is permitted when it is ARGUED where it lives. The ones that do are
/// enumerated (a)-(e) beside the asserts themselves, and deliberately NOT counted here or in
/// <c>LayoutCorpusEmitterTests</c>, which carried the same count twice more. The list sits
/// directly above the code it describes, so a count beside it is pure redundancy: it went wrong
/// once already ("three" after a fourth landed), and every sweep for its remaining homes came
/// back with a different total than the one before. Deleting it is the only move that ends
/// that. The file reached the ADJACENT conclusion for the case count above — "a numeral beside
/// a catalog anyone can count needs no such archaeology" — and settled there for a digit because
/// that catalog lives in another file. This one does not: it lives beside the asserts themselves,
/// in <c>LayoutCorpus_FromBytes_EmitsReport</c>. One more assert lives in
/// <c>LayoutCorpusEmitterTests</c>, whose subject is
/// <c>AutoPromoteBlockReason</c>'s declared member set. The rule's exclusion is
/// <i>everything the production chain PRODUCES</i>; a declared type surface is not that, and no
/// parsing change can move it. No parsing OUTCOME is asserted anywhere.</para>
///
/// <para>The artifact is written BEFORE any assert runs, so even a breach leaves a complete,
/// readable report on disk.</para>
/// </summary>
public sealed class LayoutCorpusReportTests
{
    /// <summary>The commit this baseline's branch is BASED on — its merge-base, not a commit that
    /// can reproduce the file. Stated that way because the looser reading is false and was read as
    /// a defect: on any PR that changes the product, the baseline beside it carries rows the base
    /// commit cannot produce, and this one does (rows 22 and 23 do not exist at <c>d435a9c4</c>).
    /// What the string answers is "what was this measured AGAINST", which is the question a
    /// baseline diff asks. It has two homes — here and the committed baseline's own header — and
    /// nothing checks them against each other, so bump it DELIBERATELY when regenerating, never as
    /// a side effect. A stale value would make the provenance string F3 exists for untrustworthy.
    ///
    /// <para><b>It was stale, and the warning above is why that is worth writing down rather than
    /// quietly overwriting.</b> The value read <c>7a5496fe</c> (PR E's merge) while
    /// <c>git log -- baseline/layout-corpus-report.baseline.md</c> shows the file was last
    /// regenerated in <c>a72c77e7</c> (PR B). So the published numbers were post-B behaviour
    /// carrying a pre-B provenance string — a claim true of the run that first produced the file
    /// and false of the one that last wrote it. Corrected 2026-07-28 to this branch's base.</para>
    /// </summary>
    private const string BaseCommit = "d435a9c4";

    [Fact]
    public async Task LayoutCorpus_FromBytes_EmitsReport()
    {
        var ct = TestContext.Current.CancellationToken;

        var observations = new List<LayoutCaseObservation>();
        foreach (var layoutCase in LayoutCaseCatalog.All)
            observations.Add(await LayoutChainRunner.RunAsync(layoutCase, ct));

        var markdown = LayoutCorpusReport.Build(new LayoutCorpusReportData(
            BaseCommit, observations, CvChainProbe.SubstitutedPorts,
            LayoutCaseCatalog.ValidateModelSymmetry()));

        await WriteArtifactAsync(markdown, ct);

        // ── Instrument asserts only, from here down. Failure means the FIXTURE or the EMITTER is
        // broken; none of these can be reddened by a change to the product's parsing behaviour.

        // The asserts below that ARE reachable by production behaviour are listed here, each
        // with its own argument. Stated plainly rather than claimed away, because an earlier
        // revision of this comment said "none of these can be reddened by a change to the
        // product" and that was simply false. It carries NO COUNT any more: the count went wrong
        // once ("three" after a fourth landed) and stale once ("four", correct when written and
        // stale the moment (e) landed — it then SHIPPED stale in two homes across a commit
        // boundary before a sweep found them), and a list sitting directly above the asserts it
        // describes needs no numeral to be checkable.
        //
        //   (a) crash-safety — the probe catches everything the real extractor, segmenter and both
        //       handlers throw, so a throw anywhere in the chain reddens this suite. That is
        //       correct: a corpus that swallows a product crash is not measuring anything.
        //   (b) kind resolution — the subject is production Domain (CvFileSignature). Correct for
        //       the same reason: a case never fed to the handler as the kind it claims measures
        //       nothing.
        //   (c) marker visibility — argued at its own assert below.
        //   (d) ladder well-formedness — argued at its own assert below. NEW 2026-07-28: it used
        //       to be unreachable by construction and is not any more.
        //   (e) block-detail readability — argued at its own assert below (#1060 D3(β) PR 2).
        //
        // Every OTHER production output (blank lines, entry counts, confidence, WHICH gate blocked,
        // promote booleans, the content-loss delta) is recorded, never asserted. Note the narrowing
        // in that list: the ladder's SHAPE is now asserted while the VERDICT it renders still is
        // not — "gate verdicts" unqualified stopped being true when (d) landed.
        var crashed = observations.Where(o => o.CrashedWithExceptionType is not null).ToList();
        crashed.ShouldBeEmpty(
            "INSTRUMENT: a case threw. Types: "
            + string.Join(", ", crashed.Select(o => $"{o.Case.Id}={o.CrashedWithExceptionType}")));

        var badBytes = observations.Where(o => o.ByteProofFailure is not null).ToList();
        badBytes.ShouldBeEmpty(
            "INSTRUMENT: a case's authored bytes do not have the form it claims. "
            + string.Join(" | ", badBytes.Select(o => o.ByteProofFailure)));

        var badFixture = observations.Where(o => o.FixtureProblems.Count > 0).ToList();
        badFixture.ShouldBeEmpty(
            "INSTRUMENT: a model cannot carry the marker oracle. "
            + string.Join(" | ", badFixture.SelectMany(o => o.FixtureProblems)));

        LayoutCaseCatalog.ValidateModelSymmetry().ShouldBeEmpty(
            "INSTRUMENT: the English model is no longer structurally identical to the Swedish one, "
            + "so pin P5's non-difference claim is noise rather than a measurement.");

        // Production-touching assert (d), argued here. TWO causes, and the FIRST is the one a
        // reader will actually hit: the ladder has no arm for the reason the handler returned, so
        // it cannot place the block. That is reachable by a product change — a new
        // AutoPromoteBlockReason reddens this — and red is the right answer, because the
        // alternative is what shipped before: the artifact narrating an unmapped token as a
        // handler fault while §0 reported the instrument healthy. It does not block its own
        // remedy: the remedy is one arm in GateLadder, in this same suite.
        observations.Where(o => !GateLadder.IsWellFormed(o.Gates)).ShouldBeEmpty(
            "INSTRUMENT: a gate ladder is not well-formed. Either (1) it has no arm for the reason "
            + "the handler returned — most likely a new AutoPromoteBlockReason — or (2) it reports "
            + "a rung as passed after one that was never evaluated, which is impossible.");

        observations.Where(o => !o.KindResolved).ShouldBeEmpty(
            "INSTRUMENT: a case's bytes failed CvFileSignature.TryResolve, so it was never fed to "
            + "the handler as the kind it claims to be.");

        // Production-touching assert (e), argued here. TWO causes, and only one of them is
        // production-reachable: (1) the handler renames or drops the {BlockDetail} placeholder,
        // (2) CvChainProbe's own reader breaks — its key constant, its exactly-one rule, or the
        // record it reads. Cause (2) is the reason this assert has to exist HERE: it lives
        // entirely inside this project and no test outside it can see it.
        //
        // It does not block its own remedy, which is the hatch's real test. No parsing, gate,
        // promote or content change can redden it — only the log CONTRACT can, and the remedy is
        // then one const in this suite or the placeholder restored, which is verbatim assert
        // (d)'s accepted argument.
        //
        // Why recording it was not enough, measured rather than argued: with the reader's key
        // misspelled, the suite stayed green while the artifact printed INSTRUMENT: unreadable on
        // five rows and the real code zero times. That degradation is honest and it is also
        // invisible to CI — the artifact is gitignored, only the baseline is tracked, and no test
        // compares them. "A human will notice if a human happens to regenerate and then happens
        // to read" is not a guard (CTO-bind 2026-08-01, Decision 1).
        //
        // THE OTHER DIRECTION, and it is no longer a forecast: #1060 β-1 was the day. The
        // docx-label-first rows promote now, so no case in the corpus blocks on IncompleteContent
        // and no row carries a Domain code.
        //
        // The prediction was ALMOST right and the difference is worth keeping. (e) did NOT go
        // wholly vacuous: rows 16 and 17 still block on personnummer, so a LeftPending is still
        // logged, ReadBlockDetail still runs, and the property-name contract this assert exists for
        // is still exercised. What went unmeasured is the CODE-BEARING half — no row produces a
        // DomainErrorCode any more, so a regression in that propagation would move nothing here.
        // §0's "none" still reads the same for "everything was readable" and "there was nothing to
        // read", and inventing a floor on how many cases must block would be the §2.5 ratchet this
        // suite may not make for itself. Accepted, and the emitter half stays pinned separately by
        // LayoutCorpusEmitterTests over synthetic observations.
        observations.Where(o => o.BlockDetailUnreadable).ShouldBeEmpty(
            "INSTRUMENT: a LeftPending was logged whose BlockDetail property this harness could "
            + "not read. Either the {BlockDetail} placeholder was renamed or removed in "
            + "AutoPromoteParsedResumeCommandHandler, or CvChainProbe's reader broke. The Domain-"
            + "code column is blind until one of the two is restored — it prints an instrument "
            + "marker rather than an em-dash, but nothing else in CI would have said so.");

        // Production-touching assert (c), argued here. It read "The ONE deliberate
        // production-touching assert" until 2026-08-01 — a FIFTH home of the count, eight lines
        // under (e) and twenty-four under (d), and "deliberate" distinguished nothing: all three
        // are equally deliberate and equally argued.
        //
        // A corpus that cannot see its own content must REFUSE TO REPORT rather than report loss:
        // without this, a renderer that silently dropped a job would print a zero delta as "no
        // loss", and a broken extractor would print a full delta as a finding. It stays an assert
        // because no plausible PR B/C/E change removes an authored employer name from the
        // EXTRACTED TEXT — a change that does is a genuine regression and red is the right answer.
        var invisible = observations
            .Where(o => o.Markers.Any(m => !m.InExtractedBytes))
            .Select(o => $"{o.Case.Id} [{o.ExtractionStatus}]: "
                + string.Join(",", o.Markers.Where(m => !m.InExtractedBytes).Select(m => m.Marker)))
            .ToList();

        invisible.ShouldBeEmpty(
            "EXTRACTION: the corpus cannot see content it authored, so no row below it means "
            + "anything. Verify the extractor before reading any result. " + string.Join(" | ", invisible));
    }

    private static async Task WriteArtifactAsync(string markdown, CancellationToken ct)
    {
        // Deterministic path, no timestamp in the filename (parity CorpusFindingsReportTests):
        // walk up from bin/Debug/net10.0 to the project root, then artifacts/. Gitignored twice
        // (.gitignore:32 and :43) — the EMITTER is the deliverable.
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "artifacts"));
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "layout-corpus-report.md"), markdown, ct);
    }
}
