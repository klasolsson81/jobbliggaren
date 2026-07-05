using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Queries.VerifyCredentials;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// After PR2c (C5, epik #481) the credential-check body moved into the shared
/// <see cref="IReauthenticationService"/> — the SAME check <c>ReauthenticationBehavior</c> runs for
/// <c>IReauthenticatingRequest</c> commands — so the re-auth policy lives in exactly ONE place
/// (pinned by <c>ReauthenticationServiceTests</c>). The handler backing <c>POST /auth/verify</c> is
/// now a thin pass-through, so these tests pin ONLY the delegation contract: the query's password is
/// forwarded unchanged and the service's <see cref="Result"/> flows straight back (both success and
/// failure). The re-auth LOGIC (no userId / empty email / wrong password / TOCTOU / soft-delete gate)
/// is asserted in <c>ReauthenticationServiceTests</c>, not here.
/// </summary>
public class VerifyCredentialsQueryHandlerTests
{
    private readonly IReauthenticationService _reauthentication = Substitute.For<IReauthenticationService>();

    [Fact]
    public async Task Handle_ForwardsPasswordToService_AndReturnsSuccess_WhenServiceSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        _reauthentication.VerifyCurrentUserPasswordAsync("S3kret!pass", Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var handler = new VerifyCredentialsQueryHandler(_reauthentication);

        var result = await handler.Handle(new VerifyCredentialsQuery("S3kret!pass"), ct);

        result.IsSuccess.ShouldBeTrue();
        // The exact password from the query is what reaches the shared service (no re-derivation).
        await _reauthentication.Received(1)
            .VerifyCurrentUserPasswordAsync("S3kret!pass", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenServiceReturnsFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        _reauthentication.VerifyCurrentUserPasswordAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(
                DomainError.Validation(AuthErrorCodes.InvalidCredentials, "E-post eller lösenord är felaktigt.")));
        var handler = new VerifyCredentialsQueryHandler(_reauthentication);

        var result = await handler.Handle(new VerifyCredentialsQuery("wrong-password"), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
    }
}
