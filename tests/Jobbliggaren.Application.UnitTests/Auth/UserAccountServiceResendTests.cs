using System.Buffers.Text;
using System.Text;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #733 — UNIT cover for the resend eligibility+mint seam <c>TryPrepareEmailConfirmationResendAsync</c>.
/// Carries the load-bearing gates: the FLAG-gate FIRST (flag-OFF => null, uniform no-op WITHOUT a DB
/// lookup, preserving #714's prod-safe default OFF — the code-reviewer Major), then an unconfirmed account
/// is eligible and a confirmed OR non-existent one is not (indistinguishable null). Mints the token
/// Api-side. <see cref="UserManager{TUser}"/> is faked via the canonical 9-arg NSubstitute constructor
/// (parity <c>UserAccountServiceTests</c>); every method exercised here is virtual.
/// </summary>
public class UserAccountServiceResendTests
{
    private readonly UserManager<ApplicationUser> _userManager =
        Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(),
            null, null, null, null, null, null, null, null);

    private const string Email = "klas@example.com";

    private UserAccountService CreateSut(bool requireEmailConfirmation = true) =>
        new(_userManager, Substitute.For<ILoginTimingEqualizer>(),
            Options.Create(new AuthOptions { RequireEmailConfirmation = requireEmailConfirmation }),
            Substitute.For<ILogger<UserAccountService>>());

    [Fact]
    public async Task TryPrepare_FlagOff_ReturnsNull_WithoutAnyLookup()
    {
        // Prod-safe default OFF: the gate is FIRST, so no DB lookup runs and no account is ever eligible —
        // a POST to the public endpoint in flag-OFF prod mails nobody (code-reviewer Major).
        var ct = TestContext.Current.CancellationToken;

        (await CreateSut(requireEmailConfirmation: false)
            .TryPrepareEmailConfirmationResendAsync(Email, ct)).ShouldBeNull();

        await _userManager.DidNotReceive().FindByEmailAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task TryPrepare_FlagOn_UnconfirmedAccount_MintsUrlSafeTokenAndReturnsDelivery()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, Email = Email, EmailConfirmed = false };
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns(user);
        _userManager.GenerateEmailConfirmationTokenAsync(user).Returns("raw-token");

        var delivery = await CreateSut().TryPrepareEmailConfirmationResendAsync(Email, ct);

        delivery.ShouldNotBeNull();
        delivery!.UserId.ShouldBe(userId);
        delivery.Email.ShouldBe(Email);
        // Base64Url-encoded so it survives the email link -> query -> POST round-trip (decodes 1:1).
        Encoding.UTF8.GetString(Base64Url.DecodeFromChars(delivery.UrlSafeToken)).ShouldBe("raw-token");
    }

    [Fact]
    public async Task TryPrepare_FlagOn_ConfirmedAccount_ReturnsNull_NoTokenMinted()
    {
        var ct = TestContext.Current.CancellationToken;
        _userManager.FindByEmailAsync(Arg.Any<string>())
            .Returns(new ApplicationUser { Id = Guid.NewGuid(), Email = Email, EmailConfirmed = true });

        (await CreateSut().TryPrepareEmailConfirmationResendAsync(Email, ct)).ShouldBeNull();
        await _userManager.DidNotReceive().GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task TryPrepare_FlagOn_NonexistentAccount_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);

        (await CreateSut().TryPrepareEmailConfirmationResendAsync(Email, ct)).ShouldBeNull();
    }
}
