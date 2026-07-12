using Jobbliggaren.Application.Dev.Abstractions;
using Jobbliggaren.Application.Dev.Commands.ConfirmEmail;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Dev.Commands.ConfirmEmail;

/// <summary>
/// DEV-ONLY throwaway handler (REMOVE BEFORE LAUNCH). The handler is a thin pass-
/// through to the <see cref="IDevEmailConfirmer"/> port (the risky force-confirm
/// primitive lives in Infrastructure). These tests pin the two behaviours that
/// matter: the email is forwarded verbatim, and the port's outcome is returned
/// unchanged (so the endpoint maps Confirmed→204 / NotFound→404 correctly).
/// </summary>
public class ConfirmEmailDevCommandHandlerTests
{
    [Fact]
    public async Task ConfirmEmailDevCommandHandler_ForwardsEmailAndReturnsConfirmed()
    {
        var confirmer = Substitute.For<IDevEmailConfirmer>();
        confirmer
            .ForceConfirmByEmailAsync("test-e2e-1@e2e.jobbliggaren.test", Arg.Any<CancellationToken>())
            .Returns(DevEmailConfirmOutcome.Confirmed);

        var handler = new ConfirmEmailDevCommandHandler(confirmer);

        var outcome = await handler.Handle(
            new ConfirmEmailDevCommand("test-e2e-1@e2e.jobbliggaren.test"),
            CancellationToken.None);

        outcome.ShouldBe(DevEmailConfirmOutcome.Confirmed);
        await confirmer.Received(1).ForceConfirmByEmailAsync(
            "test-e2e-1@e2e.jobbliggaren.test", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmEmailDevCommandHandler_WhenNoAccount_ReturnsNotFound()
    {
        var confirmer = Substitute.For<IDevEmailConfirmer>();
        confirmer
            .ForceConfirmByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(DevEmailConfirmOutcome.NotFound);

        var handler = new ConfirmEmailDevCommandHandler(confirmer);

        var outcome = await handler.Handle(
            new ConfirmEmailDevCommand("nobody@e2e.jobbliggaren.test"),
            CancellationToken.None);

        outcome.ShouldBe(DevEmailConfirmOutcome.NotFound);
    }
}
