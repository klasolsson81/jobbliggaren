using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Improvement.Queries.PreviewCvImprovement;

/// <summary>
/// The EFTER-preview of one frame application (Fas 4b PR-7, #656; handoff §6.2 — "Visa
/// alltid EFTER-preview innan Åtgärda direkt aktiveras"). A pure READ: the server
/// recomputes the review, composes the After through the SAME composer + <c>FromFrame</c>
/// the apply command uses (the preview IS the real apply, minus persistence), and MINTS
/// the finding fingerprint the client echoes back to apply (ADR 0074 Invariant 2 — the
/// token is server-derived, never client-built). Surfaces failures as a typed
/// <see cref="Result{T}"/> (an ungroundable input is a Validation failure, never a
/// fabricated preview). IRequiresFieldEncryptionKey: reads the Master version's
/// decrypted content.
/// </summary>
public sealed record PreviewCvImprovementQuery(
    Guid ResumeId,
    string CriterionId,
    string FrameId,
    IReadOnlyDictionary<string, string> SlotInputs)
    : IQuery<Result<FramePreviewDto>>, IAuthenticatedRequest, IRequiresFieldEncryptionKey;

/// <summary>
/// The preview payload: the located Before line, the frame-built After, the full
/// post-apply linear text (personnummer REDACTED for display — the transient composed
/// text is a transmit surface, ADR 0074 Invariant 1), and the minted fingerprint +
/// rubric version the apply command verifies against.
/// </summary>
public sealed record FramePreviewDto(
    string CriterionId,
    string FrameId,
    string Before,
    string After,
    string PostApplyLinearText,
    string FindingFingerprint,
    string RubricVersion);
