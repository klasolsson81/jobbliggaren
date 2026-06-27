using Jobbliggaren.Application.Admin.BackgroundJobs.Commands.RequeueFailedJob;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.BackgroundJobs.Commands.RequeueFailedJob;

/// <summary>
/// #204 / TD-83 PR2 — audit-marker + authorization contract for the requeue command. Mirrors
/// <c>RedactRecruiterPiiCommandTests</c>: the per-request <c>RequestId</c> is the audit AggregateId,
/// STABLE across the command lifetime and DISTINCT per instance (no aggregate-root Guid;
/// <c>Guid.Empty</c> is forbidden by <c>AuditLogEntry.Create</c>). EventType/AggregateType asserted
/// by value (stable audit contract).
/// </summary>
public class RequeueFailedJobCommandTests
{
    private static RequeueFailedJobCommand NewCommand() => new("server:1:job:42");

    [Fact]
    public void Command_IsAuditable_WithCorrectEventTypeAndAggregateType()
    {
        var command = NewCommand();

        ((IAuditableCommand)command).EventType.ShouldBe("Admin.FailedJobRequeued");
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
        var response = Result.Success(true);

        var first = ((IAuditableCommand<Result<bool>>)command).ExtractAggregateId(response);
        var second = ((IAuditableCommand<Result<bool>>)command).ExtractAggregateId(response);

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
