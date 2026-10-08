using Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackAvailability;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Feedback;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Queries.GetFeedbackAvailability;

/// <summary>#1979 — the admin page's status line reads the same gate submit and the dispatch read.</summary>
public sealed class GetFeedbackAvailabilityQueryHandlerTests
{
    [Theory]
    [InlineData(true, "feedback-operator@example.test", true, FeedbackAvailability.Open)]
    [InlineData(false, "feedback-operator@example.test", true, FeedbackAvailability.Disabled)]
    [InlineData(true, null, true, FeedbackAvailability.NoRecipient)]
    [InlineData(true, "feedback-operator@example.test", false, FeedbackAvailability.CannotDeliver)]
    public async Task Handle_AnswersTheGatesAvailability(
        bool enabled, string? recipient, bool canDeliver, FeedbackAvailability expected)
    {
        var sender = Substitute.For<IEmailSender>();
        sender.CanDeliver.Returns(canDeliver);
        var gate = new FeedbackGate(
            Options.Create(new FeedbackOptions { Enabled = enabled, NotificationRecipient = recipient }), sender);

        var availability = await new GetFeedbackAvailabilityQueryHandler(gate)
            .Handle(new GetFeedbackAvailabilityQuery(), TestContext.Current.CancellationToken);

        availability.ShouldBe(expected);
    }
}
