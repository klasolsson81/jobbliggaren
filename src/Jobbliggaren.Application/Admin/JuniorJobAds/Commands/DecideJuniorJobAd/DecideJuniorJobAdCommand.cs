using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Application.Admin.JuniorJobAds.Commands.DecideJuniorJobAd;

/// <summary>
/// #2264 (ADR 0187) — an admin decides whether one listed ad is a junior job, or hands it back to the rules
/// (<see langword="null"/>). Audited by type and ad id alone: no title, employer or external id in the row.
/// </summary>
public sealed record DecideJuniorJobAdCommand(Guid JobAdId, JuniorDecision? Decision)
    : ICommand<Result>, IAdminRequest, IAuditableCommand<Result>
{
    public string EventType => "Admin.JuniorDecisionRecorded";
    public string AggregateType => "JobAd";
    public Guid ExtractAggregateId(Result response) => JobAdId;
}
