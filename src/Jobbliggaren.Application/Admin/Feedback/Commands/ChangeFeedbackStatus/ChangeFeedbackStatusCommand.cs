using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Mediator;

namespace Jobbliggaren.Application.Admin.Feedback.Commands.ChangeFeedbackStatus;

/// <summary>Moves a submission to Ny, Pågår, Åtgärdad or Avstår (#1979).</summary>
public sealed record ChangeFeedbackStatusCommand(Guid Id, FeedbackStatus Status)
    : ICommand<Result>, IAdminRequest, IAuditableCommand<Result>
{
    public string EventType => "Admin.FeedbackStatusChanged";
    public string AggregateType => "Feedback";
    public Guid ExtractAggregateId(Result response) => Id;
}
