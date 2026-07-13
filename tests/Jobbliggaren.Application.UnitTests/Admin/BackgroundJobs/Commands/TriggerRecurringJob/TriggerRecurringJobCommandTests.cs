using Jobbliggaren.Application.Admin.BackgroundJobs.Commands.TriggerRecurringJob;
using Jobbliggaren.Application.BackgroundJobs;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.BackgroundJobs.Commands.TriggerRecurringJob;

/// <summary>
/// #204 / TD-83 PR2 — audit-marker + authorization contract for the trigger command. Mirrors
/// <c>RequeueFailedJobCommandTests</c> (the audit aggregate-id contract): the per-request
/// <c>RequestId</c> is the audit AggregateId, STABLE across the command lifetime and DISTINCT
/// per instance (these system mutations have no aggregate-root Guid; <c>Guid.Empty</c> is
/// forbidden by <c>AuditLogEntry.Create</c>). EventType/AggregateType values are part of the
/// stable audit contract (audit queries depend on them) and are asserted by value.
/// </summary>
public class TriggerRecurringJobCommandTests
{
    private static TriggerRecurringJobCommand NewCommand() =>
        new(RecurringJobIds.RefreshLandingStats);

    [Fact]
    public void Command_IsAuditable_WithCorrectEventTypeAndAggregateType()
    {
        var command = NewCommand();

        ((IAuditableCommand)command).EventType.ShouldBe("Admin.RecurringJobTriggered");
        ((IAuditableCommand)command).AggregateType.ShouldBe("System.BackgroundJob");
    }

    [Fact]
    public void Command_IsAdminRequest()
    {
        NewCommand().ShouldBeAssignableTo<IAdminRequest>();
    }

    [Fact]
    public void ExtractAggregateId_ReturnsRequestId_StableAcrossCalls()
    {
        var command = NewCommand();
        var response = Result.Success(RecurringJobIds.RefreshLandingStats);

        var first = ((IAuditableCommand<Result<string>>)command).ExtractAggregateId(response);
        var second = ((IAuditableCommand<Result<string>>)command).ExtractAggregateId(response);

        first.ShouldNotBe(Guid.Empty);   // Guid.Empty is forbidden by AuditLogEntry.Create
        first.ShouldBe(second);          // stable across the command lifetime
        first.ShouldBe(command.RequestId);
    }

    [Fact]
    public void RequestId_DistinctPerCommandInstance()
    {
        var a = NewCommand();
        var b = NewCommand();

        a.RequestId.ShouldNotBe(b.RequestId);
    }
}
