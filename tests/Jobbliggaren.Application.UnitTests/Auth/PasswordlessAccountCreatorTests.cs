using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1737 — what <see cref="IPasswordlessAccountCreator"/> hands Identity (ADR 0142 D10). The user manager is
/// the mocked one <c>UserAccountServiceTests</c> uses; what Identity then stores is measured against a real
/// database in <c>LoginChallengeCompleteTests</c>.
/// </summary>
public sealed class PasswordlessAccountCreatorTests
{
    private const string Email = "new.person@example.com";

    private readonly UserManager<ApplicationUser> _userManager =
        Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private UserAccountService Sut(IAccountAccessCoordinator coordinator) => new(
        _userManager, Substitute.For<ILogger<UserAccountService>>(), Substitute.For<IDbExceptionInspector>(),
        coordinator, AccountAccessTestKit.Reader(_ => null));

    [Fact]
    public async Task CreatePasswordlessUserAsync_ShouldCreateAConfirmedUserNamedByItsAddress_WithNoPassword()
    {
        ApplicationUser? handed = null;
        _userManager.CreateAsync(Arg.Do<ApplicationUser>(u => handed = u)).Returns(IdentityResult.Success);
        var userId = Guid.NewGuid();
        await using var access = AccountAccessTestKit.Coordinator();
        await using var transaction = await access.BeginAsync([userId], lifecycle: false, Ct);

        var result = await Sut(access).CreatePasswordlessUserAsync(userId, Email, Ct);

        result.IsSuccess.ShouldBeTrue();
        handed.ShouldNotBeNull();
        result.Value.ShouldBe(handed.Id);
        handed.Id.ShouldBe(userId);
        handed.Email.ShouldBe(Email);

        // The unique index is on the user name, not on the address, so the address has to be the user name.
        handed.UserName.ShouldBe(Email);
        handed.EmailConfirmed.ShouldBeTrue();
        handed.PasswordHash.ShouldBeNull();
        handed.IsSuspended.ShouldBeFalse();
        handed.AccessRevision.ShouldBe(0);
        handed.CredentialCutoff.ShouldBe(0);
        handed.CreatedAt.ShouldBe(default, "the database stamps it");
        await _userManager.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData("DuplicateUserName")]
    [InlineData("DuplicateEmail")]
    public async Task CreatePasswordlessUserAsync_ShouldCollapseADuplicate_WhenIdentityRefusesTheAddress(string code)
    {
        _userManager.CreateAsync(Arg.Any<ApplicationUser>())
            .Returns(IdentityResult.Failed(new IdentityError { Code = code, Description = $"'{Email}' is taken." }));
        var userId = Guid.NewGuid();
        await using var access = AccountAccessTestKit.Coordinator();
        await using var transaction = await access.BeginAsync([userId], lifecycle: false, Ct);

        var result = await Sut(access).CreatePasswordlessUserAsync(userId, Email, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.DuplicateAccount);
        result.Error.Message.ShouldNotContain(Email);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatePasswordlessUserAsync_ShouldWriteNothing_WhenTheCallerDoesNotHoldItsAccount(bool anotherAccountIsHeld)
    {
        var userId = Guid.NewGuid();
        await using var access = AccountAccessTestKit.Coordinator();
        await using var other = anotherAccountIsHeld
            ? await access.BeginAsync([Guid.NewGuid()], lifecycle: false, Ct)
            : null;

        await Should.ThrowAsync<InvalidOperationException>(() => Sut(access).CreatePasswordlessUserAsync(userId, Email, Ct));

        await _userManager.DidNotReceiveWithAnyArgs().CreateAsync(default!);
    }
}
