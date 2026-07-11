using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Domain.Resumes.Files;

/// <summary>
/// A durably-stored original CV file (the exact uploaded PDF/DOCX bytes), retained so the
/// user's file is never lost — "filen är helig" (P1, ADR 0093 §D5). A standalone aggregate,
/// owner-scoped by <see cref="JobSeekerId"/> and FK-less by convention (ADR 0011), coupled to
/// its <see cref="ParsedResumeId"/> for retention (an original never outlives its parsed
/// sibling — DPIA M-F3) and registered in the Art. 17 erasure CascadeMap (DPIA M-F1).
///
/// <para><b>Invariant (aggregate honesty, CTO Q2):</b> the aggregate holds only
/// <see cref="SealedContent"/> — the OPAQUE Form C ciphertext (AES-256-GCM under the owner
/// DEK, sealed in the write-path before this aggregate is constructed). It never holds or
/// exposes the plaintext bytes: encryption is not a persistence concern that leaks into the
/// model, it IS what a stored original is. There is no plaintext-bytes member (pinned by an
/// architecture test), so multi-MB CV plaintext is never change-tracked (§5 minimisation).</para>
///
/// <para><b>pnr posture (DPIA M-F5 / §7 Beslut 2(c)):</b> a pnr-flagged original is stored
/// only after an explicit user acknowledge. PR-9a captures ONLY non-flagged originals at
/// import; <see cref="PnrFlagged"/> therefore rides as <c>false</c> here and exists so the
/// deferred acknowledge-store follow-up (paired with the 9b download UX) can carry the flag as
/// metadata. The flag never surfaces publicly and never bypasses the promote-block.</para>
/// </summary>
public sealed class ResumeFile : AggregateRoot<ResumeFileId>
{
    public JobSeekerId JobSeekerId { get; private set; }

    /// <summary>The parsed-artifact this original was captured alongside — the retention
    /// coupling key (M-F3). FK-less by convention (ADR 0011); the cascade is explicit.</summary>
    public ParsedResumeId ParsedResumeId { get; private set; }

    /// <summary>The Form C envelope: <c>[version(1)] || nonce(12) || ciphertext || tag(16)</c>,
    /// AES-256-GCM under the owner DEK. OPAQUE bytes — the aggregate does not know they are
    /// ciphertext and never decrypts (the 9b streaming download owns the read path).</summary>
    public byte[] SealedContent { get; private set; } = null!;

    /// <summary>Server-derived canonical MIME (from the resolved file kind, never the
    /// client-declared content-type). Fixed at download (M-F2). Non-PII.</summary>
    public string ContentType { get; private set; } = null!;

    /// <summary>The uploaded file's name — plaintext metadata, minimised. Any personnummer-shaped
    /// span is masked at <see cref="CaptureOriginal"/> (M-F1, GDPR Art. 5(1)(c)/25), mirroring
    /// <c>ParsedResume.SourceFileName</c>, so this unencrypted column never carries a plaintext
    /// personnummer. Row-deleted on Art. 17 cascade (survives crypto-erasure, so must be removed).</summary>
    public string FileName { get; private set; } = null!;

    /// <summary>Plaintext byte length of the original (non-PII) — for retention accounting and the
    /// future D9 export-cap. Never the ciphertext length.</summary>
    public long ByteSize { get; private set; }

    /// <summary>Whether the import scanner flagged a personnummer in the file body (M-F5 metadata).
    /// Always <c>false</c> in PR-9a (flagged originals are not captured); carried for the deferred
    /// acknowledge-store path. Never surfaced publicly.</summary>
    public bool PnrFlagged { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core constructor
    private ResumeFile() { }

    // Key-only deletion-intent stub ctor — NOT a general constructor. Reached only via
    // DeleteHandle; carries no sealed content and is only ever Removed, never Added.
    private ResumeFile(ResumeFileId id) : base(id) { }

    private ResumeFile(
        ResumeFileId id,
        JobSeekerId jobSeekerId,
        ParsedResumeId parsedResumeId,
        byte[] sealedContent,
        string contentType,
        string fileName,
        long byteSize,
        bool pnrFlagged,
        DateTimeOffset now) : base(id)
    {
        JobSeekerId = jobSeekerId;
        ParsedResumeId = parsedResumeId;
        SealedContent = sealedContent;
        ContentType = contentType;
        FileName = fileName;
        ByteSize = byteSize;
        PnrFlagged = pnrFlagged;
        CreatedAt = now;
    }

    /// <summary>
    /// Captures an uploaded original as a durable Form C blob. The caller has already SEALED the
    /// bytes (owner-DEK AES-256-GCM) and passes the opaque ciphertext in
    /// <paramref name="sealedContent"/> — this factory never sees plaintext. Validates structural
    /// preconditions (owner, parsed link, non-empty ciphertext, source metadata, non-negative
    /// size) and masks any personnummer-shaped span in the filename at rest (M-F1), owning the
    /// invariant "FileName carries no plaintext personnummer" for every caller.
    /// </summary>
    public static Result<ResumeFile> CaptureOriginal(
        JobSeekerId jobSeekerId,
        ParsedResumeId parsedResumeId,
        byte[] sealedContent,
        string? contentType,
        string? fileName,
        long byteSize,
        bool pnrFlagged,
        IDateTimeProvider clock)
    {
        if (jobSeekerId == default)
            return Fail("ResumeFile.JobSeekerIdRequired", "JobSeekerId krävs.");

        if (parsedResumeId == default)
            return Fail("ResumeFile.ParsedResumeIdRequired", "ParsedResumeId krävs.");

        if (sealedContent is null || sealedContent.Length == 0)
            return Fail("ResumeFile.SealedContentRequired", "Krypterat filinnehåll krävs.");

        if (string.IsNullOrWhiteSpace(contentType))
            return Fail("ResumeFile.ContentTypeRequired", "Innehållstyp krävs.");

        if (string.IsNullOrWhiteSpace(fileName))
            return Fail("ResumeFile.FileNameRequired", "Filnamn krävs.");

        if (fileName.Length > 400)
            return Fail("ResumeFile.FileNameTooLong", "Filnamn får vara max 400 tecken.");

        if (byteSize <= 0)
            return Fail("ResumeFile.ByteSizeInvalid", "Filstorlek måste vara större än noll.");

        // M-F1 / #465 precedent: a personnummer can ride in on the filename ("CV_811218-9876.pdf").
        // Mask it at rest with the SAME gap-aware, Luhn+date-gated redactor as the parsed sibling
        // (#427) so this unencrypted column never carries a plaintext personnummer. Deterministic,
        // no AI (ADR 0071); §5 personnummer guard.
        var redactedFileName = PersonnummerRedactor.Redact(fileName.Trim());

        var file = new ResumeFile(
            ResumeFileId.New(),
            jobSeekerId,
            parsedResumeId,
            sealedContent,
            contentType.Trim(),
            redactedFileName,
            byteSize,
            pnrFlagged,
            clock.UtcNow);

        return Result.Success(file);
    }

    private static Result<ResumeFile> Fail(string code, string message) =>
        Result.Failure<ResumeFile>(DomainError.Validation(code, message));

    /// <summary>
    /// A key-only <see cref="ResumeFile"/> stub for a set-based, content-free DELETE by primary
    /// key — its sole purpose is the PR-9c resume-lifecycle cascade (ADR 0100 §D5 / ADR 0103,
    /// CTO-bind F3-ii). When a user deletes a CV, its coupled original is erased via
    /// <c>db.ResumeFiles.Remove(DeleteHandle(id))</c> so the DELETE rides the SAME UnitOfWork
    /// <c>SaveChanges</c> as the resume soft-delete — one implicit EF transaction, atomic in
    /// both directions (import capture-symmetry, ADR 0100 D4). The stub never materialises
    /// <see cref="SealedContent"/> (never decrypted, never change-tracked as plaintext — §5
    /// minimisation, DEK-free). Only ever passed to <c>Remove</c>; never <c>Add</c>ed, never a
    /// general constructor.
    /// </summary>
    public static ResumeFile DeleteHandle(ResumeFileId id) => new(id);
}
