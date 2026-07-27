using Jobbliggaren.QA.Corpus.Harness;
using Jobbliggaren.QA.Corpus.Layout;
using Jobbliggaren.QA.Corpus.Reporting;
using Shouldly;

namespace Jobbliggaren.QA.Corpus;

/// <summary>
/// PR K (#1060) — the layout fitness corpus. It authors sixteen CV documents as real PDF and DOCX
/// BYTES and drives each of them through the product's own chain: <c>CvFileSignature</c> →
/// <c>ImportResumeCommandHandler</c> (extract, personnummer scan, segment, <c>ParsedResume.Create</c>)
/// → <c>AutoPromoteParsedResumeCommandHandler</c> (six gates, the internal content mapper, the DQ6
/// guard, <c>Resume.CreateFromParsed</c>). No database server, no container, no network.
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
/// the rule matters exactly as much for the attempt that follows.) THREE asserts do touch production, each argued at
/// the assert block below — crash-safety, kind resolution, and marker visibility. No parsing
/// OUTCOME is asserted anywhere.</para>
///
/// <para>The artifact is written BEFORE any assert runs, so even a breach leaves a complete,
/// readable report on disk.</para>
/// </summary>
public sealed class LayoutCorpusReportTests
{
    /// <summary>The commit this baseline was produced at. It has two homes — here and the
    /// committed baseline's own header — and nothing checks them against each other, so bump it
    /// DELIBERATELY when regenerating, never as a side effect. A stale value would make the
    /// provenance string F3 exists for untrustworthy.</summary>
    private const string BaseCommit = "7a5496fe";

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

        // Three of the asserts below ARE reachable by production behaviour, and each has its own
        // argument. Stated plainly rather than claimed away, because an earlier revision of this
        // comment said "none of these can be reddened by a change to the product" and that was
        // simply false.
        //
        //   (a) crash-safety — the probe catches everything the real extractor, segmenter and both
        //       handlers throw, so a throw anywhere in the chain reddens this suite. That is
        //       correct: a corpus that swallows a product crash is not measuring anything.
        //   (b) kind resolution — the subject is production Domain (CvFileSignature). Correct for
        //       the same reason: a case never fed to the handler as the kind it claims measures
        //       nothing.
        //   (c) marker visibility — argued at its own assert below.
        //
        // Every OTHER production output (blank lines, entry counts, confidence, gate verdicts,
        // promote booleans, the content-loss delta) is recorded, never asserted.
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

        observations.Where(o => !GateLadder.IsWellFormed(o.Gates)).ShouldBeEmpty(
            "INSTRUMENT: a gate ladder reports a rung as passed after one that was never "
            + "evaluated, which is impossible — the ladder derivation is wrong.");

        observations.Where(o => !o.KindResolved).ShouldBeEmpty(
            "INSTRUMENT: a case's bytes failed CvFileSignature.TryResolve, so it was never fed to "
            + "the handler as the kind it claims to be.");

        // The ONE deliberate production-touching assert, argued rather than smuggled in.
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
