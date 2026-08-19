using Jobbliggaren.Application.Auth;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.TestSupport;
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
    // Only the user store is exercised; UserManager's eight remaining collaborators are
    // deliberately absent. NSubstitute 6 types the ctor args as non-nullable object[], so the
    // absence has to be stated as `null!` — building the eight real Identity collaborators is
    // what CLAUDE.md §2.4 rules out.
    private readonly UserManager<ApplicationUser> _userManager =
        Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
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

    // ---- #1349: the compensating delete stops failing silently ----------------------------------

    private const string FailureDescription = "Optimistic concurrency failure, object has been modified.";

    private (UserAccountService Sut, RecordingLogger<UserAccountService> Logger, Guid UserId, ApplicationUser User)
        ArrangeDelete(IdentityResult deleteResult)
    {
        // The SHARED recorder (tests/Shared/RecordingLogger.cs, already Compile-linked into this
        // project). It records EventId and the structured properties, not only the formatted
        // string, and it SNAPSHOTS them - which matters here because UserAccountService lives in
        // Infrastructure, compiled against the R9 generator, whose pooled thread-local
        // LoggerMessageState is cleared the moment the generated method returns. A hand-rolled
        // recorder that keeps the state rather than snapshotting it reads back empty (#1237).
        var logger = new RecordingLogger<UserAccountService>();
        var sut = new UserAccountService(
            _userManager, _equalizer, Options.Create(new AuthOptions()), logger);
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, Email = "gone@example.com" };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.DeleteAsync(user).Returns(deleteResult);
        return (sut, logger, userId, user);
    }

    [Fact]
    public async Task DeleteUserAsync_ShouldLogTheCode_WhenTheCompensatingDeleteFails()
    {
        // The compensating delete in RegisterCommandHandler's JobSeeker.Register failure arm. A
        // failure here leaves exactly the orphaned Identity row that arm exists to prevent, and
        // before #1349 it said nothing at all.
        //
        // The fixture is what production emits, not a plausible-looking stand-in:
        // UserManager.DeleteAsync is a passthrough to the EF store, which returns Success or
        // exactly one ConcurrencyFailure, and this Description is IdentityErrorDescriber's own
        // string.
        var ct = TestContext.Current.CancellationToken;
        var (sut, logger, userId, _) = ArrangeDelete(IdentityResult.Failed(
            new IdentityError { Code = "ConcurrencyFailure", Description = FailureDescription }));

        await sut.DeleteUserAsync(userId, ct);

        var entry = logger.Records.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.EventId.Id.ShouldBe(4007);
        entry.Message.ShouldContain("ConcurrencyFailure");
        entry.Message.ShouldContain(userId.ToString());
        // Codes, never Descriptions: a Description is user-facing prose that can carry the value
        // that failed.
        entry.Message.ShouldNotContain(FailureDescription);
    }

    [Fact]
    public async Task DeleteUserAsync_ShouldNotTruncate_WhenGivenAnUnreachableMultiErrorResult()
    {
        // DECLARED UNREACHABLE (CLAUDE.md section 5, Tests:). No path in src/ produces a
        // multi-error result here: UserManager.DeleteAsync is a passthrough to
        // IUserStore.DeleteAsync with NO validator pass, and the stock EF store returns Success or
        // exactly one ConcurrencyFailure. Nothing overrides it - measured: no custom IUserStore,
        // no IdentityErrorDescriber override, no UserManager subclass.
        //
        // Seam parity with LogEmailConfirmedPersistFailed does NOT license the plural: that one
        // wraps UpdateAsync, which DOES run validators, which is why its own comment can say
        // "four of the five reachable ones". Parity with a legitimate seam is not provenance.
        //
        // So this asserts only that the READ SIDE degrades safely if that invariant ever breaks -
        // the join names every code rather than the first - and asserts nothing about what
        // production does.
        var ct = TestContext.Current.CancellationToken;
        var (sut, logger, userId, _) = ArrangeDelete(IdentityResult.Failed(
            new IdentityError { Code = "ConcurrencyFailure", Description = "a" },
            new IdentityError { Code = "DefaultError", Description = "b" }));

        await sut.DeleteUserAsync(userId, ct);

        var entry = logger.Records.ShouldHaveSingleItem();
        entry.Message.ShouldContain("ConcurrencyFailure");
        entry.Message.ShouldContain("DefaultError");
    }

    [Fact]
    public async Task DeleteUserAsync_ShouldLogNothing_WhenTheCompensatingDeleteSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sut, logger, userId, user) = ArrangeDelete(IdentityResult.Success);

        await sut.DeleteUserAsync(userId, ct);

        await _userManager.Received(1).DeleteAsync(user);
        logger.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteUserAsync_ShouldLogNothingAndNotDelete_WhenTheRowIsAlreadyGone()
    {
        // The race branch: Identity was already cleaned between the lookup and here. Nothing
        // failed, so nothing is reported - a Warning on an absent row would be noise the operator
        // learns to ignore, which is what would make the real one invisible.
        var ct = TestContext.Current.CancellationToken;
        var logger = new RecordingLogger<UserAccountService>();
        var sut = new UserAccountService(
            _userManager, _equalizer, Options.Create(new AuthOptions()), logger);
        var userId = Guid.NewGuid();
        _userManager.FindByIdAsync(userId.ToString()).Returns((ApplicationUser?)null);

        await sut.DeleteUserAsync(userId, ct);

        await _userManager.DidNotReceive().DeleteAsync(Arg.Any<ApplicationUser>());
        logger.Records.ShouldBeEmpty();
    }
}
