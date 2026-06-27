using Jobbliggaren.Application.Admin.BackgroundJobs;
using Jobbliggaren.Application.Admin.BackgroundJobs.Commands.RequeueFailedJob;
using Jobbliggaren.Domain.Common;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.BackgroundJobs.Commands.RequeueFailedJob;

/// <summary>
/// #204 / TD-83 PR2 — handler maps the port's BCL <see cref="RequeueOutcome"/> to a domain-correct
/// <see cref="Result"/>: Requeued → Success(true); JobNotFound → NotFound/404; NotInFailedState →
/// Conflict/409. The ErrorKind (not the message text) is the status discriminator (TD-63 kind-union),
/// so the failure tests assert <c>DomainError.Kind</c>. A failure also means AuditBehavior writes no
/// row — proven end-to-end in the integration tests; here the unit contract is the mapping.
/// </summary>
public class RequeueFailedJobCommandHandlerTests
{
    private const string JobId = "server:1:job:42";
    private readonly IBackgroundJobController _controller = Substitute.For<IBackgroundJobController>();

    private RequeueFailedJobCommandHandler CreateHandler(RequeueOutcome outcome)
    {
        _controller.RequeueAsync(JobId, Arg.Any<CancellationToken>()).Returns(outcome);
        return new RequeueFailedJobCommandHandler(_controller);
    }

    [Fact]
    public async Task Handle_Requeued_ReturnsSuccessTrue()
    {
        var handler = CreateHandler(RequeueOutcome.Requeued);

        var result = await handler.Handle(new RequeueFailedJobCommand(JobId), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_JobNotFound_ReturnsNotFoundFailure()
    {
        var handler = CreateHandler(RequeueOutcome.JobNotFound);

        var result = await handler.Handle(new RequeueFailedJobCommand(JobId), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        result.Error.Code.ShouldBe("RequeueFailedJob.NotFound");
    }

    [Fact]
    public async Task Handle_NotInFailedState_ReturnsConflictFailure()
    {
        var handler = CreateHandler(RequeueOutcome.NotInFailedState);

        var result = await handler.Handle(new RequeueFailedJobCommand(JobId), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Code.ShouldBe("RequeueFailedJob.NotFailed");
    }

    [Fact]
    public async Task Handle_AnyOutcome_CallsPortExactlyOnceWithJobId()
    {
        var handler = CreateHandler(RequeueOutcome.Requeued);

        await handler.Handle(new RequeueFailedJobCommand(JobId), TestContext.Current.CancellationToken);

        await _controller.Received(1).RequeueAsync(JobId, Arg.Any<CancellationToken>());
    }
}
