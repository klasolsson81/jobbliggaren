using Jobbliggaren.Application.Auth.Commands.ChangePassword;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

public class ChangePasswordCommandHandlerTests
{
    private const string CurrentPassword = "Current123456";
    private const string NewPassword = "NewSecret12345";

    private static ChangePasswordCommandHandler CreateHandler(
        ICurrentUser currentUser,
        IUserAccountService userAccountService)
        => new(currentUser, userAccountService);

    private static ICurrentUser AuthenticatedUser(Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    [Fact]
    public async Task Handle_WithValidChange_ReturnsUserIdForAudit()
    {
        var userId = Guid.NewGuid();
        var service = Substitute.For<IUserAccountService>();
        service.ChangePasswordAsync(userId, CurrentPassword, NewPassword, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var handler = CreateHandler(AuthenticatedUser(userId), service);

        var result = await handler.Handle(
            new ChangePasswordCommand(CurrentPassword, NewPassword), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        // The audit aggregate id (User.PasswordChanged) AND the id the endpoint re-issues for.
        result.Value.ShouldBe(userId);
    }

    [Fact]
    public async Task Handle_WhenIdentityChangeFails_PropagatesError()
    {
        var userId = Guid.NewGuid();
        var service = Substitute.For<IUserAccountService>();
        service.ChangePasswordAsync(userId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DomainError.Validation("Auth.PasswordTooShort", "För kort.")));
        var handler = CreateHandler(AuthenticatedUser(userId), service);

        var result = await handler.Handle(
            new ChangePasswordCommand(CurrentPassword, NewPassword), CancellationToken.None);

        // The port failure propagates. (That AuditBehavior skips failed commands is proven
        // generically by AuditBehaviorTests, not re-asserted here.)
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.PasswordTooShort");
    }

    [Fact]
    public async Task Handle_WhenUnauthenticated_ReturnsFailureWithoutCallingService()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var service = Substitute.For<IUserAccountService>();
        var handler = CreateHandler(currentUser, service);

        var result = await handler.Handle(
            new ChangePasswordCommand(CurrentPassword, NewPassword), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.NotAuthenticated");
        await service.DidNotReceive().ChangePasswordAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, NewPassword)]
    [InlineData("", NewPassword)]
    [InlineData(CurrentPassword, null)]
    [InlineData(CurrentPassword, "")]
    public async Task Handle_WithMissingPassword_ReturnsFailureWithoutCallingService(string? current, string? updated)
    {
        var userId = Guid.NewGuid();
        var service = Substitute.For<IUserAccountService>();
        var handler = CreateHandler(AuthenticatedUser(userId), service);

        var result = await handler.Handle(new ChangePasswordCommand(current, updated), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await service.DidNotReceive().ChangePasswordAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
