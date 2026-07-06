using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes.Parsing.Events;

namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// A CV imported from a PDF/DOCX and parsed deterministically (F4-8, ADR 0074 —
/// NO AI/LLM). A standalone <b>staging</b> aggregate (CTO Decision 1 = Variant A):
/// its looser invariant is "a parse always materialises, even a degraded or
/// personnummer-flagged one" — deliberately separate from the strict canonical
/// <c>Resume</c> (whose <c>ValidateContent</c> would reject a degraded parse).
/// Promotion to a canonical <c>Resume</c> is a later, user-confirmed step
/// (F4-9/F4-10); F4-8 only produces and persists the artifact.
///
/// <para><b>Invariants enforced here (ADR 0074):</b> CV-PII content (<see cref="Content"/>,
/// <see cref="RawText"/>) is persisted only via the field-encryption pipeline
/// (Invariant 3); the personnummer scan ran on the raw text before this aggregate was
/// constructed and its PII-safe outcome rides on <see cref="Personnummer"/> (Invariant 1);
/// promotion is blocked while a personnummer is flagged
/// (<see cref="EnsureReadyForPromotion"/>).</para>
/// </summary>
public sealed class ParsedResume : AggregateRoot<ParsedResumeId>
{
    public JobSeekerId JobSeekerId { get; private set; }

    /// <summary>The uploaded file's name — plaintext metadata (never CV body content). Any
    /// personnummer-shaped span is masked at <see cref="Create"/> (#465, GDPR Art. 5(1)(c)/25
    /// minimisation) so this unencrypted column and the owner-scoped read DTOs never carry a
    /// plaintext personnummer.</summary>
    public string SourceFileName { get; private set; } = null!;

    public string SourceContentType { get; private set; } = null!;

    public ResumeLanguage DetectedLanguage { get; private set; } = ResumeLanguage.Sv;

    public ParsedResumeStatus Status { get; private set; } = ParsedResumeStatus.PendingReview;

    /// <summary>Structured parsed content. CV-PII — EF-Ignore'd and persisted as an
    /// encrypted JSON shadow (Form B); the interceptor pair owns the transform.</summary>
    public ParsedResumeContent Content { get; private set; } = null!;

    /// <summary>The raw normalized extracted text, retained for F4-9 span citation
    /// (Invariant 2). CV-PII — persisted as an encrypted string column (Form A).</summary>
    public string RawText { get; private set; } = null!;

    /// <summary>Parse confidence (OQ5). Non-PII metadata.</summary>
    public ParseConfidence Confidence { get; private set; } = null!;

    /// <summary>PII-safe personnummer-scan outcome (Invariant 1). Masked metadata only.</summary>
    public PersonnummerScanOutcome Personnummer { get; private set; } = PersonnummerScanOutcome.None;

    /// <summary>Non-PII structural layout metrics (Fas 4b PR-6b, ADR 0093 §D4) — page count,
    /// file size and the tightest page margin, read from the source PDF at import (the only
    /// place the bytes exist; the review pipeline never sees the file). Null for a pre-PR-6b
    /// import or when the analyzer produced nothing. Parity <see cref="Confidence"/>: a plain
    /// non-PII column, NEVER the DEK shadow (it carries no CV text).</summary>
    public CvLayoutMetrics? LayoutMetrics { get; private set; }

    private readonly List<ProposedOccupation> _occupationProposals = [];

    /// <summary>Unconfirmed SSYK proposals (F4-3 call-site, ADR 0040 Beslut 4 — the
    /// user confirms later; never auto-selected). Non-PII.</summary>
    public IReadOnlyList<ProposedOccupation> OccupationProposals => _occupationProposals.AsReadOnly();

    private readonly List<ProposedSkill> _skillProposals = [];

    /// <summary>Unconfirmed JobTech skill proposals resolved from the CV at import
    /// (ADR 0079 STEG 3, ADR 0040 Beslut 4 — the user confirms later via the FE
    /// full-replace save; never auto-selected). Non-PII (taxonomy id + label).</summary>
    public IReadOnlyList<ProposedSkill> SkillProposals => _skillProposals.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    // EF Core constructor
    private ParsedResume() { }

    private ParsedResume(
        ParsedResumeId id,
        JobSeekerId jobSeekerId,
        string sourceFileName,
        string sourceContentType,
        ResumeLanguage detectedLanguage,
        ParsedResumeContent content,
        string rawText,
        ParseConfidence confidence,
        PersonnummerScanOutcome personnummer,
        CvLayoutMetrics? layoutMetrics,
        DateTimeOffset now) : base(id)
    {
        JobSeekerId = jobSeekerId;
        SourceFileName = sourceFileName;
        SourceContentType = sourceContentType;
        DetectedLanguage = detectedLanguage;
        Status = ParsedResumeStatus.PendingReview;
        Content = content;
        RawText = rawText;
        Confidence = confidence;
        Personnummer = personnummer;
        LayoutMetrics = layoutMetrics;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Creates a parsed-CV staging artifact. Validates only structural preconditions
    /// (owner, source metadata, non-null parse outputs) — deliberately NOT content
    /// completeness: a degraded/incomplete parse is a first-class, persistable state
    /// (OQ5 / CTO Decision 1). The caller has already run the personnummer guard on the
    /// raw text (Invariant 1) and passes the PII-safe outcome in
    /// <paramref name="personnummer"/>.
    /// </summary>
    public static Result<ParsedResume> Create(
        JobSeekerId jobSeekerId,
        string? sourceFileName,
        string? sourceContentType,
        ResumeLanguage detectedLanguage,
        ParsedResumeContent content,
        string rawText,
        ParseConfidence confidence,
        PersonnummerScanOutcome personnummer,
        IEnumerable<ProposedOccupation> occupationProposals,
        IDateTimeProvider clock,
        // ADR 0079 STEG 3 — skill proposals resolved from the CV at import. Optional
        // trailing param (default null → empty) so the ~30 existing Create callers stay
        // unchanged (additive, like the occupation proposals before it); only
        // ImportResumeCommandHandler passes a non-null set.
        IEnumerable<ProposedSkill>? skillProposals = null,
        // Fas 4b PR-6b — non-PII layout metrics read from the source PDF at import. Optional
        // trailing param (default null) so existing Create callers stay unchanged (additive,
        // parity skillProposals); only ImportResumeCommandHandler passes it (it has the bytes).
        CvLayoutMetrics? layoutMetrics = null)
    {
        if (jobSeekerId == default)
            return Fail("ParsedResume.JobSeekerIdRequired", "JobSeekerId krävs.");

        if (string.IsNullOrWhiteSpace(sourceFileName))
            return Fail("ParsedResume.SourceFileNameRequired", "Källfilens namn krävs.");

        if (sourceFileName.Length > 400)
            return Fail("ParsedResume.SourceFileNameTooLong", "Källfilens namn får vara max 400 tecken.");

        if (string.IsNullOrWhiteSpace(sourceContentType))
            return Fail("ParsedResume.SourceContentTypeRequired", "Innehållstyp krävs.");

        if (detectedLanguage is null)
            return Fail("ParsedResume.LanguageRequired", "Språk krävs.");

        if (content is null)
            return Fail("ParsedResume.ContentRequired", "Innehåll krävs.");

        if (rawText is null)
            return Fail("ParsedResume.RawTextRequired", "Råtext krävs.");

        if (confidence is null)
            return Fail("ParsedResume.ConfidenceRequired", "Parse-confidence krävs.");

        if (personnummer is null)
            return Fail("ParsedResume.PersonnummerOutcomeRequired", "Personnummer-utfall krävs.");

        // #465 (GDPR Art. 5(1)(c)/25 minimisation): a personnummer can ride in on the source
        // filename (e.g. "CV_811218-9876.pdf"); left as-is it would persist as plaintext in the
        // unencrypted source_file_name column and be surfaced verbatim to the owner-scoped read
        // DTOs. Mask it at construction so the aggregate owns the invariant "SourceFileName carries
        // no plaintext personnummer" for every caller (not one handler). Reuses the SAME gap-aware,
        // Luhn+date-gated redactor as the CV-evidence path (#427): only a REAL personnummer span is
        // masked (an arbitrary digit run — a year, a phone number — is untouched), the extension and
        // separators are kept, length is preserved ("CV_811218-9876.pdf" → "CV_******-****.pdf").
        // The SEPARATE #426 FoundInFileName flag is computed from the ORIGINAL filename BEFORE this
        // factory runs, so the B4 "rename your file" warn is unaffected — this only minimises what
        // is stored at rest. Deterministic, no AI (ADR 0071); §5 personnummer guard.
        var redactedFileName = PersonnummerRedactor.Redact(sourceFileName.Trim());

        var now = clock.UtcNow;
        var parsed = new ParsedResume(
            ParsedResumeId.New(),
            jobSeekerId,
            redactedFileName,
            sourceContentType.Trim(),
            detectedLanguage,
            content,
            rawText,
            confidence,
            personnummer,
            layoutMetrics,
            now);

        foreach (var proposal in occupationProposals ?? [])
            parsed._occupationProposals.Add(proposal);

        foreach (var skill in skillProposals ?? [])
            parsed._skillProposals.Add(skill);

        parsed.RaiseDomainEvent(new ParsedResumeImportedDomainEvent(
            parsed.Id, jobSeekerId, confidence.Overall, personnummer.Found, now));

        return Result.Success(parsed);
    }

    /// <summary>
    /// Precondition gate for promotion to a canonical <c>Resume</c> (the promotion
    /// mapping itself is F4-9/F4-10). Promotion is BLOCKED while a personnummer is
    /// flagged (ADR 0074 Invariant 1 / CTO Decision 3(i)); only a
    /// <see cref="ParsedResumeStatus.PendingReview"/> artifact can be promoted. F4-8
    /// owns the invariant here so F4-9 enforces it through the aggregate, not in a
    /// handler.
    /// </summary>
    public Result EnsureReadyForPromotion()
    {
        if (Status != ParsedResumeStatus.PendingReview)
            return Result.Failure(DomainError.Conflict(
                "ParsedResume.NotPendingReview", "Endast en import under granskning kan befordras."));

        if (Personnummer.Found)
            return Result.Failure(DomainError.Validation(
                "ParsedResume.PersonnummerMustBeRemoved",
                "Ta bort personnummer ur CV:t innan det kan användas."));

        return Result.Success();
    }

    /// <summary>
    /// Promotes this staging artifact to a canonical <c>Resume</c> (Fas 4 STEG A). The
    /// promotion gate (<see cref="EnsureReadyForPromotion"/>) runs INSIDE the aggregate
    /// (F4-8 design intent — never in a handler): only a <see cref="ParsedResumeStatus.PendingReview"/>
    /// artifact with no flagged personnummer can be promoted. On success the artifact is
    /// marked <see cref="ParsedResumeStatus.Promoted"/> and soft-deleted at the same time
    /// (CTO DQ7 — retained for audit until the staging-retention sweep), mirroring
    /// <see cref="Discard"/>. The actual <c>Resume</c> construction is the caller's
    /// responsibility (the gap-filled content comes from the user-approved payload, not
    /// from this artifact — CTO DQ1 Variant A, CLAUDE.md §5 "never synthesise").
    /// Not idempotent: a second call returns <c>ParsedResume.NotPendingReview</c>.
    /// </summary>
    public Result Promote(IDateTimeProvider clock)
    {
        var gate = EnsureReadyForPromotion();
        if (gate.IsFailure)
            return gate;

        Status = ParsedResumeStatus.Promoted;
        var now = clock.UtcNow;
        UpdatedAt = now;
        DeletedAt = now;
        RaiseDomainEvent(new ParsedResumePromotedDomainEvent(Id, JobSeekerId, now));
        return Result.Success();
    }

    /// <summary>The user rejected the import. Soft-deleted and marked
    /// <see cref="ParsedResumeStatus.Discarded"/>; retained for audit until the
    /// staging-retention sweep prunes it (retention TD).</summary>
    public void Discard(IDateTimeProvider clock)
    {
        if (Status == ParsedResumeStatus.Discarded)
            return;

        Status = ParsedResumeStatus.Discarded;
        var now = clock.UtcNow;
        UpdatedAt = now;
        DeletedAt = now;
    }

    private static Result<ParsedResume> Fail(string code, string message) =>
        Result.Failure<ParsedResume>(DomainError.Validation(code, message));
}
