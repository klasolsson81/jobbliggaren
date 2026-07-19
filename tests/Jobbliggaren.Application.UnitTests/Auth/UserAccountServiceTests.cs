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
/// #481 Low — the STRUCTURAL regression guard for the login-timing equalizer. The fix changes
/// TIMING, not the observable response: both the unknown-email and the known-email-wrong-password
/// branches already return a byte-identical <c>Auth.InvalidCredentials</c> 401 (pinned by LoginTests /
/// LockoutTests). So the only test that catches someone deleting the <c>Equalize</c> call is this
/// one — it asserts the branch WIRING directly:
/// <list type="bullet">
/// <item><b>Unknown email</b> (<c>FindByEmailAsync</c> -> null): the equalizer IS invoked, paying the
/// PBKDF2 cost before the failure so response latency does not reveal that the account is absent.</item>
/// <item><b>Known email, wrong password</b> (<c>CheckPasswordAsync</c> -> false): the equalizer is NOT
/// invoked — the REAL hash comparison already paid the cost; a second dummy derivation would be
/// double work and is deliberately skipped.</item>
/// </list>
/// <see cref="UserManager{TUser}"/> is mocked via the canonical 9-argument NSubstitute constructor:
/// a real <c>UserManager</c> needs an <see cref="IUserStore{TUser}"/> plus eight collaborators, but
/// only the store must be non-null and every method exercised here is <c>virtual</c> (so the stubs
/// intercept before any real store / hasher work runs).
/// </summary>
public class UserAccountServiceTests
{
    private readonly UserManager<ApplicationUser> _userManager =
        Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(),
            null, null, null, null, null, null, null, null);
    private readonly ILoginTimingEqualizer _equalizer = Substitute.For<ILoginTimingEqualizer>();
    private readonly UserAccountService _sut;

    // Flag OFF by default (legacy instant-login). Flag-ON gate tests build their own SUT.
    private UserAccountService CreateSut(bool requireEmailConfirmation = false) =>
        new(_userManager, _equalizer,
            Options.Create(new AuthOptions { RequireEmailConfirmation = requireEmailConfirmation }),
            Substitute.For<ILogger<UserAccountService>>());

    public UserAccountServiceTests() => _sut = CreateSut();

    [Fact]
    public async Task ValidateCredentialsAsync_ShouldPayEqualizerCostAndReturnInvalidCredentials_WhenEmailIsUnknown()
    {
        var ct = TestContext.Current.CancellationToken;
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);

        var result = await _sut.ValidateCredentialsAsync("nobody@example.com", "whatever", ct);

        // The regression guard: the equalizer pays the PBKDF2 cost the absent real hash-check skips.
        _equalizer.Received(1).Equalize(Arg.Any<string>());
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_ShouldSkipEqualizerAndReturnInvalidCredentials_WhenPasswordIsWrong()
    {
        var ct = TestContext.Current.CancellationToken;
        const string password = "WrongPwd!";
        var user = new ApplicationUser { Email = "known@example.com", UserName = "known@example.com" };
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns(user);
        _userManager.IsLockedOutAsync(user).Returns(false);
        _userManager.CheckPasswordAsync(user, password).Returns(false);
        _userManager.AccessFailedAsync(user).Returns(IdentityResult.Success);

        var result = await _sut.ValidateCredentialsAsync("known@example.com", password, ct);

        // The REAL hash comparison ran and paid the cost, so the dummy equalizer must NOT also run.
        _equalizer.DidNotReceive().Equalize(Arg.Any<string>());
        await _userManager.Received(1).CheckPasswordAsync(user, password);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_ShouldSkipEqualizerAndHashCheck_WhenAccountLocked()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new ApplicationUser { Email = "locked@example.com", UserName = "locked@example.com" };
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns(user);
        _userManager.IsLockedOutAsync(user).Returns(true);

        var result = await _sut.ValidateCredentialsAsync("locked@example.com", "whatever", ct);

        // #503 anti-DoS regression guard (CTO-bind #1, Verdict A): the locked branch stays cheap — it
        // pays NEITHER a real hash comparison NOR the dummy equalizer, so a hammered locked account can
        // never be forced into PBKDF2 per hit. The residual locked-state timing channel is accepted (it
        // does not aid enumeration — a one-attempt-per-email probe never locks an account).
        _equalizer.DidNotReceive().Equalize(Arg.Any<string>());
        await _userManager.DidNotReceive().CheckPasswordAsync(user, Arg.Any<string>());
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.AccountLocked);
    }

    // #714 — the email-confirmation-first login gate. Placed AFTER a successful password check, so it is
    // reachable ONLY with valid credentials (not an enumeration oracle).
    [Fact]
    public async Task ValidateCredentialsAsync_ShouldReturnEmailNotConfirmed_WhenFlagOnAndUnconfirmedAndPasswordCorrect()
    {
        var ct = TestContext.Current.CancellationToken;
        const string password = "Correct-pass-123456"; // gitleaks:allow — test-only password literal, not a secret
        var user = new ApplicationUser { Email = "u@example.com", UserName = "u@example.com", EmailConfirmed = false };
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns(user);
        _userManager.IsLockedOutAsync(user).Returns(false);
        _userManager.CheckPasswordAsync(user, password).Returns(true);
        _userManager.GetRolesAsync(user).Returns(new List<string>());

        var result = await CreateSut(requireEmailConfirmation: true)
            .ValidateCredentialsAsync("u@example.com", password, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailNotConfirmed);
        // NOT a failed login attempt — the credentials were valid, so no lockout counter increment.
        await _userManager.DidNotReceive().AccessFailedAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task ValidateCredentialsAsync_ShouldSucceed_WhenFlagOnAndEmailConfirmed()
    {
        var ct = TestContext.Current.CancellationToken;
        const string password = "Correct-pass-123456"; // gitleaks:allow — test-only password literal, not a secret
        var user = new ApplicationUser { Email = "c@example.com", UserName = "c@example.com", EmailConfirmed = true };
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns(user);
        _userManager.IsLockedOutAsync(user).Returns(false);
        _userManager.CheckPasswordAsync(user, password).Returns(true);
        _userManager.GetRolesAsync(user).Returns(new List<string>());

        var result = await CreateSut(requireEmailConfirmation: true)
            .ValidateCredentialsAsync("c@example.com", password, ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateCredentialsAsync_ShouldSucceed_WhenFlagOffAndUnconfirmed()
    {
        // Legacy behavior: with the flag OFF the gate is inert, so an unconfirmed account logs in.
        var ct = TestContext.Current.CancellationToken;
        const string password = "Correct-pass-123456"; // gitleaks:allow — test-only password literal, not a secret
        var user = new ApplicationUser { Email = "o@example.com", UserName = "o@example.com", EmailConfirmed = false };
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns(user);
        _userManager.IsLockedOutAsync(user).Returns(false);
        _userManager.CheckPasswordAsync(user, password).Returns(true);
        _userManager.GetRolesAsync(user).Returns(new List<string>());

        var result = await _sut.ValidateCredentialsAsync("o@example.com", password, ct);

        result.IsSuccess.ShouldBeTrue();
    }

    // #828 — /me's address + roles in ONE identity round-trip.

    [Fact]
    public async Task GetAccountSummaryAsync_ShouldResolveEmailAndRoles_InASingleFindByIdRoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, Email = "klas@example.com", UserName = "klas@example.com" };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.GetRolesAsync(user).Returns(new List<string> { "User" });

        var summary = await _sut.GetAccountSummaryAsync(userId, ct);

        summary.ShouldNotBeNull();
        summary!.Email.ShouldBe("klas@example.com");
        summary.Roles.ShouldContain("User");

        // The durable one-round-trip guard: the whole point of #828 is that address + roles cost a SINGLE
        // identity resolve. Rewriting the impl to fetch the row twice (e.g. an AsNoTracking GetEmail path
        // re-added) flips this to Received(2) and fails.
        await _userManager.Received(1).FindByIdAsync(userId.ToString());
    }

    [Fact]
    public async Task GetAccountSummaryAsync_ShouldReturnNull_WhenAccountRowIsGone()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        _userManager.FindByIdAsync(userId.ToString()).Returns((ApplicationUser?)null);

        var summary = await _sut.GetAccountSummaryAsync(userId, ct);

        summary.ShouldBeNull();
        // No roles lookup on a missing row (nothing to resolve them against).
        await _userManager.DidNotReceive().GetRolesAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task GetAccountSummaryAsync_ShouldSurfaceNullEmailButKeepRoles_WhenRowHasNoAddress()
    {
        // Option A seam: a PRESENT row with a null Email is the broken #822 invariant. The port surfaces
        // that absence honestly (Email == null), distinct from a null summary (row gone), and never
        // coalesces to "" here — the empty-string policy is the handler's. Roles survive the missing email.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, Email = null, UserName = "no-email" };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.GetRolesAsync(user).Returns(new List<string> { "User" });

        var summary = await _sut.GetAccountSummaryAsync(userId, ct);

        summary.ShouldNotBeNull();
        summary!.Email.ShouldBeNull();
        summary.Roles.ShouldContain("User");
    }
}
