using Jobbliggaren.Application.Admin.BackgroundJobs;
using Jobbliggaren.Application.Admin.BackgroundJobs.Commands.TriggerRecurringJob;
using Jobbliggaren.Application.BackgroundJobs;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.BackgroundJobs.Commands.TriggerRecurringJob;

/// <summary>
/// #204 / TD-83 PR2 — handler for the admin "trigger recurring job now" action. The handler
/// only delegates to the <see cref="IBackgroundJobController"/> port and echoes the id
/// (allowlist membership is enforced upstream by the validator), so the contract under test is:
/// delegate exactly once + return Success carrying the echoed id.
/// </summary>
public class TriggerRecurringJobCommandHandlerTests
{
    private readonly IBackgroundJobController _controller = Substitute.For<IBackgroundJobController>();

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithEchoedJobId()
    {
        _controller
            .TriggerRecurringAsync(RecurringJobIds.BackgroundMatching, Arg.Any<CancellationToken>())
            .Returns(RecurringJobIds.BackgroundMatching);
        var handler = new TriggerRecurringJobCommandHandler(_controller);
        var command = new TriggerRecurringJobCommand(RecurringJobIds.BackgroundMatching);

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(RecurringJobIds.BackgroundMatching);
    }

    [Fact]
    public async Task Handle_ValidCommand_CallsPortExactlyOnceWithThatId()
    {
        _controller
            .TriggerRecurringAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RecurringJobIds.ExpireJobAds);
        var handler = new TriggerRecurringJobCommandHandler(_controller);
        var command = new TriggerRecurringJobCommand(RecurringJobIds.ExpireJobAds);

        await handler.Handle(command, TestContext.Current.CancellationToken);

        await _controller.Received(1)
            .TriggerRecurringAsync(RecurringJobIds.ExpireJobAds, Arg.Any<CancellationToken>());
    }
}
