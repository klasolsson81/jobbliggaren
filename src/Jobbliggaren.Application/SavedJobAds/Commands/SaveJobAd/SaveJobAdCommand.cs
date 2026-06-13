using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.SavedJobAds.Commands.SaveJobAd;

/// <summary>
/// F6 P5 Punkt 2 Del A — bokmärk en annons för aktuell JobSeeker.
/// Idempotent: redan-sparad → Success utan ny domain-event (handler guard).
/// </summary>
public sealed record SaveJobAdCommand(Guid JobAdId)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>
{
    public string EventType => "SavedJobAd.Saved";
    public string AggregateType => "SavedJobAd";
    public Guid ExtractAggregateId(Result response) => JobAdId;
}
