using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Feedback;
using Mediator;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackAvailability;

/// <summary>Why feedback is open or closed, for the admin page's status line (#1979).</summary>
public sealed record GetFeedbackAvailabilityQuery : IQuery<FeedbackAvailability>, IAdminRequest;

public sealed class GetFeedbackAvailabilityQueryHandler(FeedbackGate gate)
    : IQueryHandler<GetFeedbackAvailabilityQuery, FeedbackAvailability>
{
    public ValueTask<FeedbackAvailability> Handle(GetFeedbackAvailabilityQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult(gate.Availability);
}
