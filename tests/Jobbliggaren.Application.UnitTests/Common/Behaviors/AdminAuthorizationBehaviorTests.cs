using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Common.Behaviors;
using Jobbliggaren.Application.Common.Exceptions;
using Mediator;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Behaviors;

/// <summary>
/// Defense-in-depth-tester för AdminAuthorizationBehavior. Verifierar att:
///  - icke-IAdminRequest-messages pipas igenom oavsett roll
///  - IAdminRequest utan Admin-roll kastar ForbiddenException
///  - IAdminRequest med Admin-roll pipas igenom
///
/// Behavior är pipeline-component som körs i Mediator-pipen — fångar Worker-/CLI-/
/// test-fixture-anrop som inte går via HTTP-policyn. (CTO 2026-05-11 M4)
/// </summary>
public class AdminAuthorizationBehaviorTests
{
    private static MessageHandlerDelegate<TMessage, string> NextReturning<TMessage>(string value)
        where TMessage : IMessage =>
        (_, _) => ValueTask.FromResult(value);

    [Fact]
    public async Task Handle_NonAdminRequest_PassesThroughEvenWhenNotInAdminRole()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsInRole(Roles.Admin).Returns(false);

        var behavior = new AdminAuthorizationBehavior<TestCommand, string>(currentUser);

        var result = await behavior.Handle(
            new TestCommand("x"),
            NextReturning<TestCommand>("ok"),
            CancellationToken.None);

        result.ShouldBe("ok");
        currentUser.DidNotReceive().IsInRole(Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_AdminRequest_WithoutAdminRole_ThrowsForbiddenException()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsInRole(Roles.Admin).Returns(false);

        var behavior = new AdminAuthorizationBehavior<TestAdminCommand, string>(currentUser);

        await Should.ThrowAsync<ForbiddenException>(async () =>
            await behavior.Handle(
                new TestAdminCommand("x"),
                NextReturning<TestAdminCommand>("ok"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AdminRequest_WithAdminRole_PassesThrough()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsInRole(Roles.Admin).Returns(true);

        var behavior = new AdminAuthorizationBehavior<TestAdminCommand, string>(currentUser);

        var result = await behavior.Handle(
            new TestAdminCommand("x"),
            NextReturning<TestAdminCommand>("ok"),
            CancellationToken.None);

        result.ShouldBe("ok");
    }
}
