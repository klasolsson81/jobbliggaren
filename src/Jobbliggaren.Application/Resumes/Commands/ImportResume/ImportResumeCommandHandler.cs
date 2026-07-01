using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
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
    IOccupationCodeDeriver occupationDeriver,
    IOccupationExperienceDeriver occupationExperienceDeriver,
    ISkillResolver skillResolver)
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

        // 1. Extract raw text (never throws on a degraded file — returns a fallback
        //    status; honours the request ct cooperatively, #272 SEC-2).
        var extraction = extractor.Extract(command.FileBytes, kind, cancellationToken);

        // 2. Personnummer guard on the RAW text BEFORE persist (Invariant 1). The
        //    normalizer bridges spaced/OCR-gapped forms on a transient scan-copy only;
        //    the persisted raw text is the original, un-normalized extraction.
        var scanCopy = PersonnummerTextNormalizer.Normalize(extraction.RawText);
        var personnummerMatches = PersonnummerScanner.Scan(scanCopy);

        // 2a. Defense-in-depth (#426, ADR 0074 Invariant 1): the CV FILENAME is a second
        //     surface a personnummer can ride in on (e.g. "CV_811218-9876.pdf"); a body-only
        //     scan leaves a clean-body CV whose filename carries one unflagged (B4 falsely
        //     clean). Run the SAME Normalize→Scan path over the filename — a filename can
        //     carry NBSP/zero-width noise too, so this reuses the #427 guard rather than a
        //     weaker matcher — and carry the result as the SEPARATE FoundInFileName flag. It
        //     does NOT fold into Found/Count/Kinds (those stay body-exclusive), so a
        //     filename-only hit does NOT block promotion (the filename never reaches the
        //     canonical Resume); B4 surfaces it as a Warn prompting a rename. The filename is
        //     never logged and the outcome stays PII-safe (count + kinds + a location bool).
        var fileNameScanCopy = PersonnummerTextNormalizer.Normalize(command.FileName);
        var foundInFileName = PersonnummerScanner.Scan(fileNameScanCopy).Count > 0;

        var personnummer = PersonnummerScanOutcome.FromMatches(personnummerMatches, foundInFileName);

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

        // 4. SSYK derivation (F4-3 call-site, Tier 1 multi-signal — Klas 2026-06-21): propose
        //    from a UNION of the CV's occupation-bearing strings, EDUCATION FIRST (what you
        //    study now is the likely desired occupation — the career-changer signal a
        //    work-history-only derivation misses), then work history. Non-occupation strings
        //    (a company, a school) self-filter to no match. The user confirms later (ADR 0040
        //    Beslut 4 — never auto-selected/persisted as confirmed). An empty list ⇒ manual
        //    selection. English CVs are out of scope here (Swedish-only NLP/taxonomy) — Tier 2.
        var sourceTitles = BuildDerivationSources(content);
        IReadOnlyList<OccupationCandidate> candidates = [];
        if (sourceTitles.Count > 0)
        {
            var derivation = await occupationDeriver.DeriveManyAsync(sourceTitles, cancellationToken);
            candidates = derivation.Candidates;
        }

        // 4a. Per-occupation experience attribution (ADR 0079-amendment, exp-per-occ PR-2): a
        //     SEPARATE pass (DeriveManyAsync above untouched — OCP) re-derives each EXPERIENCE
        //     entry's group(s) and parses its period, aggregating per group as the merged-interval
        //     union of contributing spans (Klas-val "lifetime in the field"). Education periods are
        //     study years, not work experience → excluded (only content.Experience is passed), so
        //     an education-sourced group has no entry here → honest null. Only the non-PII int +
        //     concept-id are projected; the raw periods stay DEK-encrypted (#159 precedent).
        var experienceYearsByGroup = await occupationExperienceDeriver
            .DeriveApproximateYearsAsync(content.Experience, cancellationToken);

        // The join on the UNION candidates is the authoritative filter: only a group the union
        // pass actually proposed can carry years, so the attributor re-deriving every entry
        // (uncapped, unlike the union source-builder's MaxDerivationSources) can never produce an
        // orphan year. Keep this join here — a future cap on the attributor must NOT silently drop
        // a legitimately-proposed group's years.
        var proposals = candidates
            .Select(c => new ProposedOccupation(
                c.OccupationGroupConceptId, c.OccupationGroupLabel, c.MatchedOn,
                experienceYearsByGroup.TryGetValue(c.OccupationGroupConceptId, out var years)
                    ? years
                    : null))
            .ToList();

        // 4b. Skill resolution (ADR 0079 STEG 3): resolve the CV's claimed skill names to
        //     JobTech skill concept-ids via the SAME shared SkillTaxonomyIndex the ads are
        //     extracted against (so the overlap is meaningful) and carry them as PROPOSALS
        //     (concept-id + canonical label, non-PII). Unresolvable names drop silently
        //     (fail-closed, normal). The user confirms later via the full-replace
        //     MatchPreferences save — never auto-confirmed (ADR 0040 Beslut 4). A degraded
        //     parse has empty content.Skills → no proposals.
        var skillProposals = skillResolver
            .ResolveDetailed(content.Skills, cancellationToken)
            .Select(s => new ProposedSkill(s.ConceptId, s.Label))
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
            clock,
            skillProposals);

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

    // Tier 1 multi-signal sources (Klas 2026-06-21): the CV's occupation-bearing strings in
    // PRIORITY order — every education Degree + Institution FIRST (current studies are the
    // desired-occupation signal), then every experience Title + Organization. Title and
    // Organization are BOTH fed because the layout-naive parser may put the role in either slot
    // ("Plasman — Operatör" → the company can land in the Title slot); the deriver matches
    // occupations and self-filters companies/schools to nothing. Trim + distinct (Ordinal
    // ignore-case, preserving priority order), bounded to MaxDerivationSources so a long CV
    // cannot fan out the in-memory taxonomy scan (the DoS/UX bound, CTO Decision 2).
    private const int MaxDerivationSources = 40;

    private static List<string> BuildDerivationSources(ParsedResumeContent content)
    {
        IEnumerable<string?> Ordered()
        {
            foreach (var edu in content.Education)
            {
                yield return edu.Degree;
                yield return edu.Institution;
            }
            foreach (var exp in content.Experience)
            {
                yield return exp.Title;
                yield return exp.Organization;
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in Ordered())
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var trimmed = raw.Trim();
            if (seen.Add(trimmed))
                result.Add(trimmed);
            if (result.Count >= MaxDerivationSources)
                break;
        }
        return result;
    }
}
