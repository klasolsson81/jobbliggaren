using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Commands.SetFindingStatus;

// Fas 4b PR-4 (#653, ADR 0093 §D2(e); handoff §5.3): the user's decision on one review
// finding — "jag fixar det själv, markera som klar" (Resolved), "ignorera regeln"
// (Ignored) or a revert (Open). Writes ONLY a status enum + server-derived fingerprint
// into the DEK-free ledger — no canonical free text is mutated, so this command sits
// outside the personnummer-tripwire subject set by design (CTO-bind PR-4 Q7).
//
// IRequiresFieldEncryptionKey: the handler recomputes the review (compute-on-demand,
// ADR 0074) to derive the fingerprint from the engine's CURRENT finding — that reads the
// Master version's decrypted content. IAuditableCommand: a manual status change is a
// user action with an audit row (D2 security MUST — the marker is opt-in, and omitting
// it would silently skip the row).
public sealed record SetFindingStatusCommand(
    Guid ResumeId,
    string CriterionId,
    string Status)
    : ICommand<Result>, IAuthenticatedRequest, IRequiresFieldEncryptionKey,
      IAuditableCommand<Result>
{
    public string EventType => "Resume.FindingStatusSet";
    public string AggregateType => "Resume";
    public Guid ExtractAggregateId(Result response) => ResumeId;
}
