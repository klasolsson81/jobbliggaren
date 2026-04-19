using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Behaviors;
using Mediator;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Common.Behaviors;

public class AuthorizationBehaviorTests
{
    [Fact]
    public async Task Handle_InSteg2_AlwaysPassesThrough()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);
        currentUser.UserId.Returns((Guid?)null);

        var behavior = new AuthorizationBehavior<TestCommand, string>(currentUser);
        MessageHandlerDelegate<TestCommand, string> next =
            (_, _) => ValueTask.FromResult("ok");

        var result = await behavior.Handle(new TestCommand("x"), next, CancellationToken.None);

        result.ShouldBe("ok");
    }
}
