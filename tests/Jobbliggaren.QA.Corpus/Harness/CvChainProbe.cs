using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;
using Jobbliggaren.Application.Resumes.Commands.ImportResume;
using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.QA.Corpus.Generation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
    string? PromoteFailureCode,
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
        string fileName, string contentType, byte[] bytes, string accountDisplayName,
        CancellationToken ct)
    {
        try
        {
            return await RunCoreAsync(fileName, contentType, bytes, accountDisplayName, ct);
        }
        catch (Exception ex)
        {
            // A crash must FAIL LOUDLY as a report row, never abort the run and lose the
            // artifact for every other case. The exception TYPE only — never the message,
            // which could carry CV text (parity Harness/CrashSweep.cs, CLAUDE.md §5).
            return new CvChainObservation(
                false, null, string.Empty, 0, 0, false, null, null, null, false, null, null,
                ex.GetType().Name);
        }
    }

    private static async Task<CvChainObservation> RunCoreAsync(
        string fileName, string contentType, byte[] bytes, string accountDisplayName,
        CancellationToken ct)
    {
        var userId = Guid.NewGuid();
        var clock = FixedClock.Default;
        await using var db = CorpusAppDbContextFactory.Create();

        // The account display name is a CASE input, not a fixed constant. The auto-promote
        // handler feeds it into the composed DTO, so it is the ONLY text the DQ6 guard sees that
        // the import scan did not already cover. One case therefore authors a personnummer HERE
        // rather than in the CV body: that is the only route to the DQ6 rung which a parse-level
        // personnummer does not pre-empt, and without it, deleting the guard call would leave the
        // whole report byte-identical.
        var seeker = JobSeeker.Register(userId, accountDisplayName, clock).Value;
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
                null, false, null, null, null);
        }

        await db.SaveChangesAsync(ct);

        // Read the artifact back through the SAME context instance. Re-materialising it in a
        // fresh context would return a null content shadow: ParsedResume.Content is populated by
        // the production FieldDecryptionMaterializationInterceptor, which is not wired here.
        var parsed = await db.ParsedResumes
            .FirstOrDefaultAsync(p => p.Id == new ParsedResumeId(import.Value.ParsedResumeId), ct);

        // RawText is a MAPPED property on the aggregate, so the text the handler actually
        // consumed is readable off the tracked instance. The marker trace and the one
        // production-touching assert therefore measure exactly the text that was parsed, not a
        // second extraction that merely ought to match it. Only the STATUS is unavailable from the
        // aggregate (the handler consumes and discards it), so that one value costs a second call
        // over the same bytes.
        var rawText = parsed?.RawText ?? string.Empty;
        var status = CvFileSignature.TryResolve(contentType, bytes.AsSpan(), out var kind)
            ? RealExtractor.Extract(bytes, kind, ct).Status
            : (CvExtractionStatus?)null;
        var lines = rawText.Split('\n');

        var promote = await RunAutoPromoteAsync(db, currentUser, clock, import.Value.ParsedResumeId, ct);

        return new CvChainObservation(
            KindResolved: kindResolved,
            ExtractionStatus: status,
            RawText: rawText,
            BlankLineCount: lines.Count(l => l.Trim().Length == 0),
            LineCount: lines.Length,
            SegmentRan: status == CvExtractionStatus.Extracted
                        && !string.IsNullOrWhiteSpace(rawText),
            Parsed: parsed,
            ImportFailureCode: null,
            BlockReason: promote.Block,
            Promoted: promote.Promoted,
            PromotedResume: promote.Resume,
            PromoteFailureCode: promote.FailureCode,
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

    private static async Task<(AutoPromoteBlockReason? Block, bool Promoted, Resume? Resume,
        string? FailureCode)> RunAutoPromoteAsync(
            AppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock,
            Guid parsedResumeId, CancellationToken ct)
    {
        var reconciler = Substitute.For<IResumeReviewReconciler>();
        var handler = new AutoPromoteParsedResumeCommandHandler(
            db, currentUser, clock, Substitute.For<IFailedAccessLogger>(), reconciler,
            Substitute.For<ICorrelationIdProvider>(), Substitute.For<IRequestContextProvider>(),
            NullLogger<AutoPromoteParsedResumeCommandHandler>.Instance);

        var result = await handler.Handle(new AutoPromoteParsedResumeCommand(parsedResumeId), ct);

        // A Failure on THIS command is never a gate verdict: the handler reserves it for a
        // genuine fault (unknown or foreign artifact, infrastructure). Carried as its own field so
        // a fault can never be rendered as an honest block.
        if (result.IsFailure)
            return (null, false, null, result.Error.Code);

        return result.Value switch
        {
            AutoPromoteOutcome.LeftPending pending => (pending.Reason, false, null, null),

            // Read the promoted aggregate off the change tracker, NEVER via a re-query. Two
            // independent reasons, and the STRONGER one is the second: (1) ResumeVersion.Content is
            // EF-Ignored and only the production materialization interceptor fills it, so a fresh
            // context yields null content and every case would report a false IncompleteContent;
            // (2) no SaveChanges runs after promote, so the aggregate is still Added and a re-query
            // would not find it AT ALL. Anyone who later adds a save must still not switch to a
            // re-query, because reason (1) survives it.
            AutoPromoteOutcome.Promoted => (null, true, db.Resumes.Local.SingleOrDefault(), null),
            _ => (null, false, null, null),
        };
    }
}
