using Jobbliggaren.Application.Admin.Feedback.Commands.RequeueFeedbackNotification;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Commands.RequeueFeedbackNotification;

/// <summary>
/// #1979 — the requeue is admin-only, audited under a stable event type keyed to the submission, and replayed by
/// <c>UnitOfWorkBehavior</c> when its save loses a concurrency race on the notice's xmin.
/// </summary>
public sealed class RequeueFeedbackNotificationCommandTests
{
    [Fact]
    public void Command_IsAnAuditedAdminRequestThatReplaysOnAConcurrencyConflict()
    {
        var id = Guid.NewGuid();
        var command = new RequeueFeedbackNotificationCommand(id, AcknowledgeDuplicateRisk: true);

        command.ShouldBeAssignableTo<IAdminRequest>();
        command.ShouldBeAssignableTo<IReplayOnConcurrencyConflict>();
        var audited = command.ShouldBeAssignableTo<IAuditableCommand<Result>>();
        audited.EventType.ShouldBe("Admin.FeedbackNotificationRequeued");
        audited.AggregateType.ShouldBe("Feedback");
        audited.ExtractAggregateId(Result.Success()).ShouldBe(id);
    }
}
