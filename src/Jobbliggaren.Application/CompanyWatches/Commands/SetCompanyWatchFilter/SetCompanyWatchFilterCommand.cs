using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Commands.SetCompanyWatchFilter;

/// <summary>
/// Bevakning F4a (#803, CTO 2026-07-12 Q1=A) — replaces ONE watch's notification filter with the
/// user's current selection. Owner-scoped by <c>UserId</c>.
///
/// <para>
/// <b>Full-replace, and an empty selection means "clear".</b> The user's intent is one thing —
/// "make this watch's filter be what I just selected" — and an empty selection is a VALUE of that
/// intent, not a second intent (SRP is about change-reasons, not branch count). This mirrors the
/// shipped settings idiom (<c>UpdateFollowedCompanyNotificationConsentCommand</c>, and
/// BackgroundMatchCard's idempotent full-replace). The domain's "a present spec always narrows"
/// invariant survives because the HANDLER maps transport to domain (empty selection →
/// <c>ClearFilter()</c>, i.e. the canonical NULL) — the model does not bend to the form's shape.
/// One endpoint, so the FE never has to decide which one an empty form calls (which would create
/// an ordering hazard on a rapid clear→set).
/// </para>
///
/// <para>
/// <b>Auditable (CTO Q2).</b> The ort axis SUPPRESSES hit creation (8A), so this write changes the
/// scope of downstream personal-data processing — exactly what an Art. 5(2)/30 accountability trail
/// exists for, consent or not. (Part E C-E6 settles the LEGAL BASIS — the filter is a setting under
/// 6(1)(b), not a new consent — which is a different question from auditability.) ONE event type for
/// both directions: the direction is recoverable from the stored filter, and <c>audit_log</c> has no
/// payload column, so encoding values into event NAMES is a trap we do not walk into.
/// </para>
///
/// <para>
/// <b>The two geo axes are a union, not a hierarchy</b> — <c>Municipalities</c> and <c>Regions</c>
/// are disjoint JobTech namespaces, and a whole-län pick is carried as a län concept-id (never
/// expanded), so län-only ads still notify. See <c>WatchFilterSpec.AdmitsLocation</c>.
/// </para>
/// </summary>
public sealed record SetCompanyWatchFilterCommand(
    Guid CompanyWatchId,
    IReadOnlyList<string> Municipalities,
    IReadOnlyList<string> Regions,
    bool OnlyMatched)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>
{
    public string EventType => "CompanyWatch.FilterChanged";

    public string AggregateType => "CompanyWatch";

    public Guid ExtractAggregateId(Result response) => CompanyWatchId;
}
