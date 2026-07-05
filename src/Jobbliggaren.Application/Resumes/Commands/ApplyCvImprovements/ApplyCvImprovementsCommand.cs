using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Commands.ApplyCvImprovements;

/// <summary>
/// One approved frame application (Fas 4b PR-7, #656; ADR 0093 §D2): the finding's
/// criterion, the chosen frame, the user's raw slot inputs, and the SERVER-minted
/// fingerprint the preview returned (the optimistic guard's echo — a stale echo means the
/// CV changed since the preview and the apply must 409, never rewrite a moved target).
/// Deliberately NO before/after text — the server recomputes everything (ADR 0074
/// Invariant 2: client-submitted content would be forgeable provenance).
/// </summary>
public sealed record FrameApplyInput(
    string CriterionId,
    string FrameId,
    IReadOnlyDictionary<string, string> SlotInputs,
    string FindingFingerprint);

// Fas 4b PR-7 (#656, ADR 0093 §D2): "Åtgärda direkt" — the apply-half of propose-and-
// approve. Ids + frame inputs ONLY; the composed content is server-built, personnummer-
// guarded (the widened #650 tripwire discovers this handler via the UpdateMasterContent
// sink), written through the ONE canonical write path, then verdict-verified for
// auto-resolve (CTO D-D — the engine, not the click, decides a finding is gone).
//
// IRequiresFieldEncryptionKey: the handler reads and rewrites the Master version's
// decrypted content. IAuditableCommand: a user-approved content mutation is an audited
// action (opt-in marker — omitting it would silently skip the row).
public sealed record ApplyCvImprovementsCommand(
    Guid ResumeId,
    IReadOnlyList<FrameApplyInput> Changes)
    : ICommand<Result>, IAuthenticatedRequest, IRequiresFieldEncryptionKey,
      IAuditableCommand<Result>
{
    public string EventType => "Resume.ImprovementsApplied";
    public string AggregateType => "Resume";
    public Guid ExtractAggregateId(Result response) => ResumeId;
}
