using Jobbliggaren.Application.Admin.Feedback.Commands.ChangeFeedbackStatus;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Commands.ChangeFeedbackStatus;

/// <summary>
/// #1979 — the status change is admin-only and audited under a stable event type, keyed to the submission (the row
/// itself carries no actor). The audit row written through the real pipeline is pinned in <c>AdminFeedbackTests</c>.
/// </summary>
public sealed class ChangeFeedbackStatusCommandTests
{
    [Fact]
    public void Command_IsAnAdminRequestAuditedAsAFeedbackStatusChangeOfThatSubmission()
    {
        var id = Guid.NewGuid();
        var command = new ChangeFeedbackStatusCommand(id, FeedbackStatus.Resolved);

        command.ShouldBeAssignableTo<IAdminRequest>();
        var audited = command.ShouldBeAssignableTo<IAuditableCommand<Result>>();
        audited.EventType.ShouldBe("Admin.FeedbackStatusChanged");
        audited.AggregateType.ShouldBe("Feedback");
        audited.ExtractAggregateId(Result.Success()).ShouldBe(id);
        audited.AuditFailures.ShouldBeFalse();
    }
}
