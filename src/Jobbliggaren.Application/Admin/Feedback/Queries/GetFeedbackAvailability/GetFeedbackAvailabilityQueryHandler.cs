using Jobbliggaren.Application.Feedback;
using Mediator;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackAvailability;

public sealed class GetFeedbackAvailabilityQueryHandler(FeedbackGate gate)
    : IQueryHandler<GetFeedbackAvailabilityQuery, FeedbackAvailability>
{
    public ValueTask<FeedbackAvailability> Handle(GetFeedbackAvailabilityQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult(gate.Availability);
}
