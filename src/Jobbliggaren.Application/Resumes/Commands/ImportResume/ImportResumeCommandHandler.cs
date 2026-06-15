using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Commands.ImportResume;

/// <summary>
/// F4-8 import/parse orchestration (thin handler — the Infrastructure ports do the
/// heavy lifting). Flow (ADR 0074): resolve file kind (MIME + magic bytes) → extract →
/// normalize a transient scan-copy → run the personnummer guard on the RAW text BEFORE
/// persist (Invariant 1) → segment → derive an SSYK proposal (F4-3, user confirms
/// later) → construct the aggregate → persist (the SaveChanges interceptor encrypts the
/// CV-PII shadows, Invariant 3). CV text is never logged.
/// </summary>
public sealed class ImportResumeCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ICvTextExtractor extractor,
    IResumeSegmenter segmenter,
    IOccupationCodeDeriver occupationDeriver)
    : ICommandHandler<ImportResumeCommand, Result<ImportResumeResponse>>
{
    public async ValueTask<Result<ImportResumeResponse>> Handle(
        ImportResumeCommand command, CancellationToken cancellationToken)
    {
        // AuthorizationBehavior has already thrown if !currentUser.IsAuthenticated.
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var jobSeekerId = await db.JobSeekers
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Result.Failure<ImportResumeResponse>(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        // Magic-byte + MIME gate (a renamed payload is rejected regardless of declared
        // content-type; a declared/sniffed mismatch is rejected).
        if (!CvFileSignature.TryResolve(command.ContentType, command.FileBytes.Span, out var kind))
            return Result.Failure<ImportResumeResponse>(DomainError.Validation(
                "Resume.UnsupportedFileFormat",
                "Filformatet stöds inte. Ladda upp en PDF- eller Word-fil (DOCX)."));

        // 1. Extract raw text (never throws — degraded files return a fallback status).
        var extraction = extractor.Extract(command.FileBytes, kind);

        // 2. Personnummer guard on the RAW text BEFORE persist (Invariant 1). The
        //    normalizer bridges spaced/OCR-gapped forms on a transient scan-copy only;
        //    the persisted raw text is the original, un-normalized extraction.
        var scanCopy = PersonnummerTextNormalizer.Normalize(extraction.RawText);
        var personnummerMatches = PersonnummerScanner.Scan(scanCopy);
        var personnummer = PersonnummerScanOutcome.FromMatches(personnummerMatches);

        // 3. Segment (on the original text) → content + confidence + detected language,
        //    or a Failed confidence when extraction yielded no usable text (OQ5).
        ParsedResumeContent content;
        ResumeLanguage language;
        ParseConfidence confidence;

        if (extraction.Status == CvExtractionStatus.Extracted
            && !string.IsNullOrWhiteSpace(extraction.RawText))
        {
            var segmentation = segmenter.Segment(extraction.RawText);
            content = segmentation.Content;
            language = segmentation.DetectedLanguage;
            confidence = segmentation.Confidence;
        }
        else
        {
            content = ParsedResumeContent.Empty;
            language = ResumeLanguage.Sv;
            confidence = ParseConfidence.Failed(
                extraction.Status == CvExtractionStatus.NoTextLayer
                    ? ParseFallbackReason.ScannedImageNoText
                    : ParseFallbackReason.ExtractionFailed);
        }

        // 4. SSYK derivation (F4-3 call-site): propose from the most-recent role; the
        //    user confirms later (ADR 0040 Beslut 4 — never auto-selected/persisted as
        //    confirmed). An empty list ⇒ manual selection.
        var derivedTitle = content.Experience.Count > 0 ? content.Experience[0].Title : null;
        IReadOnlyList<OccupationCandidate> candidates = [];
        if (!string.IsNullOrWhiteSpace(derivedTitle))
        {
            var derivation = await occupationDeriver.DeriveAsync(derivedTitle, cancellationToken);
            candidates = derivation.Candidates;
        }

        var proposals = candidates
            .Select(c => new ProposedOccupation(
                c.OccupationGroupConceptId, c.OccupationGroupLabel, c.MatchedOn))
            .ToList();

        // 5. Construct the aggregate (accepts a degraded parse) and persist. The
        //    SaveChanges interceptor encrypts Content (Form B) + RawText (Form A) using
        //    the warmed owner DEK (IRequiresFieldEncryptionKey).
        var created = ParsedResume.Create(
            jobSeekerId,
            command.FileName,
            command.ContentType,
            language,
            content,
            extraction.RawText,
            confidence,
            personnummer,
            proposals,
            clock);

        if (created.IsFailure)
            return Result.Failure<ImportResumeResponse>(created.Error);

        var parsed = created.Value;
        db.ParsedResumes.Add(parsed);

        return Result.Success(MapResponse(parsed, candidates));
    }

    private static ImportResumeResponse MapResponse(
        ParsedResume parsed, IReadOnlyList<OccupationCandidate> candidates)
    {
        var sections = parsed.Confidence.Sections
            .Select(s => new SectionConfidenceDto(
                s.Kind.ToString(), s.Level.ToString(), s.Evidence))
            .ToList();

        var confidence = new ParseConfidenceDto(
            parsed.Confidence.Overall.ToString(),
            parsed.Confidence.RequiresManualReview,
            parsed.Confidence.Fallback.ToString(),
            sections);

        var personnummer = new PersonnummerScanDto(
            parsed.Personnummer.Found,
            parsed.Personnummer.Count,
            parsed.Personnummer.Kinds.Select(k => k.ToString()).ToList());

        return new ImportResumeResponse(
            parsed.Id.Value,
            parsed.DetectedLanguage.Name,
            confidence,
            personnummer,
            candidates);
    }
}
