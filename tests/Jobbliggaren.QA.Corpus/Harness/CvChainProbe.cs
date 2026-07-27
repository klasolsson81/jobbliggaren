using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;
using Jobbliggaren.Application.Resumes.Commands.ImportResume;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.QA.Corpus.Generation;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

// CA2012: stubbing the ValueTask-returning deriver ports is the known NSubstitute analyzer
// false positive (same suppression as AutoPromoteParsedResumeCommandHandlerTests).
#pragma warning disable CA2012

namespace Jobbliggaren.QA.Corpus.Harness;

/// <summary>What one case's run through the whole chain produced. Every field is RECORDED for the
/// report; none is asserted (see the observe-only rule on <c>LayoutCorpusReportTests</c>).</summary>
internal sealed record CvChainObservation(
    bool KindResolved,
    CvExtractionStatus? ExtractionStatus,
    string RawText,
    int BlankLineCount,
    int LineCount,
    bool SegmentRan,
    ParsedResume? Parsed,
    string? ImportFailureCode,
    AutoPromoteBlockReason? BlockReason,
    bool Promoted,
    Resume? PromotedResume,
    string? CrashedWithExceptionType);

/// <summary>
/// Drives ONE authored CV byte-buffer through the product's own chain, from bytes:
/// <c>CvFileSignature.TryResolve</c> → <c>ImportResumeCommandHandler</c> (which owns the extract
/// call, the personnummer scan, the conditional segment and the <c>ParsedResume.Create</c>
/// argument list) → <c>AutoPromoteParsedResumeCommandHandler</c> (which owns the six gates, the
/// internal content mapper, the DQ6 guard and <c>Resume.CreateFromParsed</c>).
///
/// <para><b>Why the real handlers rather than a stitched sequence.</b> Three links in the promote
/// chain — <c>AutoPromoteContentMapper</c>, <c>ResumeContentPersonnummerGuard</c> and
/// <c>ResumeContentMapper</c> — are <c>internal</c> to <c>Jobbliggaren.Application</c>, which does
/// not grant this assembly <c>InternalsVisibleTo</c>. A harness that stitched the ladder by hand
/// would have to RE-TYPE all three, including the guard's field families; that copy goes stale the
/// next time a field is added to <c>ResumeContentDto</c>, and the corpus would then report a clean
/// PASS on a CV the real guard blocks — a false clean on the highest-priority PII control,
/// produced by the very instrument meant to justify PR B and PR E. Running the handlers unmodified
/// also makes the GATE ORDER a measured output instead of this file's opinion, and makes a future
/// constructor change a compile error instead of a silent divergence.</para>
///
/// <para><b>What is substituted, and why none of it reaches a gate.</b> The taxonomy deriver, the
/// occupation-experience deriver and the skill resolver produce PROPOSALS only; the sealer seals
/// the original-file capture. No auto-promote gate reads any of them. Every substitution is
/// disclosed in the report's divergence block rather than left for a reader to discover.</para>
/// </summary>
internal static class CvChainProbe
{
    /// <summary>The real segmenter over the real embedded lexicon — loaded once, immutable
    /// reference data (parity with the production singleton registration in
    /// <c>AddCvParsing</c>). Resolving through a <c>ServiceCollection</c> would drag in the whole
    /// CV-review composition for no gain.</summary>
    private static readonly IResumeSegmenter RealSegmenter =
        new HeadingDrivenResumeSegmenter(CvParsingLexiconLoader.Load());

    private static readonly PdfPigOpenXmlCvTextExtractor RealExtractor = new();
    private static readonly PdfPigCvLayoutAnalyzer RealLayoutAnalyzer = new();

    /// <summary>The ports this probe substitutes, for the report's divergence disclosure.</summary>
    internal static IReadOnlyList<string> SubstitutedPorts { get; } =
    [
        "IOccupationCodeDeriver (empty candidates)",
        "IOccupationExperienceDeriver (empty years)",
        "ISkillResolver (empty proposals)",
        "IBinaryFieldSealer (identity passthrough)",
        "ICurrentUser / ICorrelationIdProvider / IRequestContextProvider / IFailedAccessLogger",
        "IResumeReviewReconciler (no-op)",
    ];

    internal static async Task<CvChainObservation> RunAsync(
        string fileName, string contentType, byte[] bytes, CancellationToken ct)
    {
        try
        {
            return await RunCoreAsync(fileName, contentType, bytes, ct);
        }
        catch (Exception ex)
        {
            // A crash must FAIL LOUDLY as a report row, never abort the run and lose the
            // artifact for every other case. The exception TYPE only — never the message,
            // which could carry CV text (parity Harness/CrashSweep.cs, CLAUDE.md §5).
            return new CvChainObservation(
                false, null, string.Empty, 0, 0, false, null, null, null, false, null,
                ex.GetType().Name);
        }
    }

    private static async Task<CvChainObservation> RunCoreAsync(
        string fileName, string contentType, byte[] bytes, CancellationToken ct)
    {
        var userId = Guid.NewGuid();
        var clock = FixedClock.Default;
        await using var db = CorpusAppDbContextFactory.Create();

        var seeker = JobSeeker.Register(userId, "Konto Kontosson", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        // The kind gate the handler runs first; recorded so a case whose bytes fail it is
        // reported as such rather than force-fed a CvFileKind it never resolved to.
        var kindResolved = CvFileSignature.TryResolve(contentType, bytes.AsSpan(), out _);

        var import = await RunImportAsync(db, currentUser, clock, fileName, contentType, bytes, ct);
        if (import.IsFailure)
        {
            return new CvChainObservation(
                kindResolved, null, string.Empty, 0, 0, false, null, import.Error.Code,
                null, false, null, null);
        }

        await db.SaveChangesAsync(ct);

        // Read the artifact back through the SAME context instance. Re-materialising it in a
        // fresh context would return a null content shadow: ParsedResume.Content is populated by
        // the production FieldDecryptionMaterializationInterceptor, which is not wired here.
        var parsed = await db.ParsedResumes
            .FirstOrDefaultAsync(p => p.Id == new ParsedResumeId(import.Value.ParsedResumeId), ct);

        // The extraction is re-run over the same bytes purely to RECORD the raw-text shape
        // (blank lines, line count) that the handler consumed but does not return. It is the
        // same call the handler makes, on the same bytes, and it is deterministic.
        var extraction = CvFileSignature.TryResolve(contentType, bytes.AsSpan(), out var kind)
            ? RealExtractor.Extract(bytes, kind, ct)
            : null;
        var rawText = extraction?.RawText ?? string.Empty;
        var lines = rawText.Split('\n');

        var promote = await RunAutoPromoteAsync(db, currentUser, clock, import.Value.ParsedResumeId, ct);

        return new CvChainObservation(
            KindResolved: kindResolved,
            ExtractionStatus: extraction?.Status,
            RawText: rawText,
            BlankLineCount: lines.Count(l => l.Trim().Length == 0),
            LineCount: lines.Length,
            SegmentRan: extraction?.Status == CvExtractionStatus.Extracted
                        && !string.IsNullOrWhiteSpace(rawText),
            Parsed: parsed,
            ImportFailureCode: null,
            BlockReason: promote.Block,
            Promoted: promote.Promoted,
            PromotedResume: promote.Resume,
            CrashedWithExceptionType: null);
    }

    private static async Task<Result<ImportResumeResponse>> RunImportAsync(
        AppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock,
        string fileName, string contentType, byte[] bytes, CancellationToken ct)
    {
        var occupationDeriver = Substitute.For<IOccupationCodeDeriver>();
        occupationDeriver
            .DeriveManyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<OccupationDerivationResult>(
                new OccupationDerivationResult(string.Empty, [])));

        var experienceDeriver = Substitute.For<IOccupationExperienceDeriver>();
        experienceDeriver
            .DeriveApproximateYearsAsync(
                Arg.Any<IReadOnlyList<ParsedExperience>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyDictionary<string, int>>(
                new Dictionary<string, int>()));

        var skillResolver = Substitute.For<ISkillResolver>();
        skillResolver
            .ResolveDetailed(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var sealer = Substitute.For<IBinaryFieldSealer>();
        sealer.Seal(Arg.Any<ReadOnlyMemory<byte>>()).Returns(ci => ci.Arg<ReadOnlyMemory<byte>>().ToArray());

        var handler = new ImportResumeCommandHandler(
            db, currentUser, clock, RealExtractor, RealLayoutAnalyzer, RealSegmenter,
            occupationDeriver, experienceDeriver, skillResolver, sealer,
            Substitute.For<ICorrelationIdProvider>(), Substitute.For<IRequestContextProvider>());

        return await handler.Handle(new ImportResumeCommand(fileName, contentType, bytes), ct);
    }

    private static async Task<(AutoPromoteBlockReason? Block, bool Promoted, Resume? Resume)>
        RunAutoPromoteAsync(
            AppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock,
            Guid parsedResumeId, CancellationToken ct)
    {
        var reconciler = Substitute.For<IResumeReviewReconciler>();
        var handler = new AutoPromoteParsedResumeCommandHandler(
            db, currentUser, clock, Substitute.For<IFailedAccessLogger>(), reconciler,
            Substitute.For<ICorrelationIdProvider>(), Substitute.For<IRequestContextProvider>());

        var result = await handler.Handle(new AutoPromoteParsedResumeCommand(parsedResumeId), ct);

        if (result.IsFailure)
            return (null, false, null);

        return result.Value switch
        {
            AutoPromoteOutcome.LeftPending pending => (pending.Reason, false, null),
            // Read the promoted aggregate off the change tracker, NEVER via a re-query: the
            // content shadow only exists on the instance the handler built.
            AutoPromoteOutcome.Promoted => (null, true, db.Resumes.Local.SingleOrDefault()),
            _ => (null, false, null),
        };
    }
}
