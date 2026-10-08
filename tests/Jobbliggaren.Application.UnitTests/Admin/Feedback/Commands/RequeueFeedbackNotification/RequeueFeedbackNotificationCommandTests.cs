using Jobbliggaren.Application.Admin.Feedback.Commands.RequeueFeedbackNotification;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Commands.RequeueFeedbackNotification;

/// <summary>
/// The requeue owns the reporter's protected transaction and success audit; an uncertain outcome must not replay.
/// </summary>
public sealed class RequeueFeedbackNotificationCommandTests
{
    [Fact]
    public void Command_ShouldOwnItsAuditedAdminTransaction_AndNeverReplay()
    {
        var id = Guid.NewGuid();
        var command = new RequeueFeedbackNotificationCommand(id, AcknowledgeDuplicateRisk: true);

        command.ShouldBeAssignableTo<IAdminRequest>();
        command.ShouldBeAssignableTo<IOwnsAccountTransaction>();
        ((object)command is IReplayOnConcurrencyConflict).ShouldBeFalse();
        var audited = command.ShouldBeAssignableTo<IAuditableCommand<Result>>();
        audited.EventType.ShouldBe("Admin.FeedbackNotificationRequeued");
        audited.AggregateType.ShouldBe("Feedback");
        audited.ExtractAggregateId(Result.Success()).ShouldBe(id);
    }
}
