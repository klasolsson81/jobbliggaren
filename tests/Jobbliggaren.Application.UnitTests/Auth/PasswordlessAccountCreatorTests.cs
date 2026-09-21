using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    // The concrete type (CA1859). DeleteAsync is an explicit implementation, so that test goes through the port.
    private UserAccountService Sut() => new(
        _userManager, Substitute.For<ILoginTimingEqualizer>(), Options.Create(new AuthOptions()),
        Substitute.For<ILogger<UserAccountService>>(),
        Substitute.For<IDbExceptionInspector>());

    [Fact]
    public async Task CreatePasswordlessUserAsync_ShouldCreateAConfirmedUserNamedByItsAddress_WithNoPassword()
    {
        ApplicationUser? handed = null;
        _userManager.CreateAsync(Arg.Do<ApplicationUser>(u => handed = u)).Returns(IdentityResult.Success);

        var result = await Sut().CreatePasswordlessUserAsync(Email, Ct);

        result.IsSuccess.ShouldBeTrue();
        handed.ShouldNotBeNull();
        result.Value.ShouldBe(handed.Id);
        handed.Email.ShouldBe(Email);

        // The unique index is on the user name, not on the address, so the address has to be the user name.
        handed.UserName.ShouldBe(Email);
        handed.EmailConfirmed.ShouldBeTrue();
        handed.PasswordHash.ShouldBeNull();
        handed.CreatedAt.ShouldBe(default, "the database stamps it, as it does for a password account");
        await _userManager.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData("DuplicateUserName")]
    [InlineData("DuplicateEmail")]
    public async Task CreatePasswordlessUserAsync_ShouldCollapseADuplicate_WhenIdentityRefusesTheAddress(string code)
    {
        _userManager.CreateAsync(Arg.Any<ApplicationUser>())
            .Returns(IdentityResult.Failed(new IdentityError { Code = code, Description = $"'{Email}' is taken." }));

        var result = await Sut().CreatePasswordlessUserAsync(Email, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.DuplicateAccount);
        result.Error.Message.ShouldNotContain(Email);
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteTheUser()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = Email };
        _userManager.FindByIdAsync(user.Id.ToString()).Returns(user);
        _userManager.DeleteAsync(user).Returns(IdentityResult.Success);

        IPasswordlessAccountCreator creator = Sut();
        await creator.DeleteAsync(user.Id, Ct);

        await _userManager.Received(1).DeleteAsync(user);
    }
}
