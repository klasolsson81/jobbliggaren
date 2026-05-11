using JobbPilot.Application.Auth.Queries.VerifyCredentials;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Auth;

public class VerifyCredentialsQueryHandlerTests
{
    private const string TestEmail = "klas@example.com";

    private static (ICurrentUser CurrentUser, IUserAccountService UserAccount) Defaults(
        Guid? userId = null, string? email = TestEmail)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId ?? Guid.NewGuid());
        currentUser.IsAuthenticated.Returns(true);
        var userAccount = Substitute.For<IUserAccountService>();
        userAccount.GetEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(email);
        return (currentUser, userAccount);
    }

    [Fact]
    public async Task Handle_WithValidPassword_ReturnsSuccess()
    {
        var userId = Guid.NewGuid();
        var (currentUser, userAccount) = Defaults(userId);
        userAccount.ValidateCredentialsAsync(TestEmail, "S3kret!pass", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserCredentials(userId, new List<string>())));

        var handler = new VerifyCredentialsQueryHandler(currentUser, userAccount);

        var result = await handler.Handle(new VerifyCredentialsQuery("S3kret!pass"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WithInvalidPassword_ReturnsInvalidCredentials()
    {
        var (currentUser, userAccount) = Defaults();
        userAccount.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UserCredentials>(
                DomainError.Validation("Auth.InvalidCredentials", "E-post eller lösenord är felaktigt.")));

        var handler = new VerifyCredentialsQueryHandler(currentUser, userAccount);

        var result = await handler.Handle(new VerifyCredentialsQuery("wrong-password"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidCredentials");
    }

    [Fact]
    public async Task Handle_WhenUserIdMismatch_ReturnsInvalidCredentials()
    {
        // Defense-in-depth: email-uppslagning resolverar till annan userId än
        // ICurrentUser.UserId — t.ex. om email bytts efter session-skapande.
        var sessionUserId = Guid.NewGuid();
        var resolvedUserId = Guid.NewGuid();
        var (currentUser, userAccount) = Defaults(sessionUserId);
        userAccount.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserCredentials(resolvedUserId, new List<string>())));

        var handler = new VerifyCredentialsQueryHandler(currentUser, userAccount);

        var result = await handler.Handle(new VerifyCredentialsQuery("S3kret!pass"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidCredentials");
    }

    [Fact]
    public async Task Handle_WhenNoUserId_ReturnsInvalidCredentials()
    {
        // Failsafe — endpoint kräver RequireAuthorization men om
        // ICurrentUser.UserId saknas (konfig-fel) ska vi inte exponera verify.
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var userAccount = Substitute.For<IUserAccountService>();

        var handler = new VerifyCredentialsQueryHandler(currentUser, userAccount);

        var result = await handler.Handle(new VerifyCredentialsQuery("pwd"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidCredentials");
        await userAccount.DidNotReceive().GetEmailAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await userAccount.DidNotReceive().ValidateCredentialsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenEmailLookupFails_ReturnsInvalidCredentials()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var userAccount = Substitute.For<IUserAccountService>();
        userAccount.GetEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var handler = new VerifyCredentialsQueryHandler(currentUser, userAccount);

        var result = await handler.Handle(new VerifyCredentialsQuery("pwd"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidCredentials");
        await userAccount.DidNotReceive().ValidateCredentialsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
