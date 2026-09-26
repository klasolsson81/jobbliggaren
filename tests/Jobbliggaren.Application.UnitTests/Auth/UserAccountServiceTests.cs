using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// <see cref="UserAccountService"/>'s account reads and its compensating delete, without a database.
/// <see cref="UserManager{TUser}"/> is mocked via the canonical 9-argument NSubstitute constructor:
/// a real <c>UserManager</c> needs an <see cref="IUserStore{TUser}"/> plus eight collaborators, but
/// only the store must be non-null and every method exercised here is <c>virtual</c> (so the stubs
/// intercept before any real store work runs).
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
    private readonly UserAccountService _sut;

    public UserAccountServiceTests() =>
        _sut = new(_userManager, Substitute.For<ILogger<UserAccountService>>(), Substitute.For<IDbExceptionInspector>());

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
        var sut = new UserAccountService(_userManager, logger, Substitute.For<IDbExceptionInspector>());
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, Email = "gone@example.com" };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.DeleteAsync(user).Returns(deleteResult);
        return (sut, logger, userId, user);
    }

    [Fact]
    public async Task DeleteAsync_ShouldLogTheCode_WhenTheCompensatingDeleteFails()
    {
        // The compensating delete in AccountRegistrar's JobSeeker.Register failure arm. A
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

        await ((IPasswordlessAccountCreator)sut).DeleteAsync(userId, ct);

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
    public async Task DeleteAsync_ShouldNotTruncate_WhenGivenAnUnreachableMultiErrorResult()
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

        await ((IPasswordlessAccountCreator)sut).DeleteAsync(userId, ct);

        var entry = logger.Records.ShouldHaveSingleItem();
        entry.Message.ShouldContain("ConcurrencyFailure");
        entry.Message.ShouldContain("DefaultError");
    }

    [Fact]
    public async Task DeleteAsync_ShouldLogNothing_WhenTheCompensatingDeleteSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sut, logger, userId, user) = ArrangeDelete(IdentityResult.Success);

        await ((IPasswordlessAccountCreator)sut).DeleteAsync(userId, ct);

        await _userManager.Received(1).DeleteAsync(user);
        logger.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_ShouldLogNothingAndNotDelete_WhenTheRowIsAlreadyGone()
    {
        // The race branch: Identity was already cleaned between the lookup and here. Nothing
        // failed, so nothing is reported - a Warning on an absent row would be noise the operator
        // learns to ignore, which is what would make the real one invisible.
        var ct = TestContext.Current.CancellationToken;
        var logger = new RecordingLogger<UserAccountService>();
        var sut = new UserAccountService(_userManager, logger, Substitute.For<IDbExceptionInspector>());
        var userId = Guid.NewGuid();
        _userManager.FindByIdAsync(userId.ToString()).Returns((ApplicationUser?)null);

        await ((IPasswordlessAccountCreator)sut).DeleteAsync(userId, ct);

        await _userManager.DidNotReceive().DeleteAsync(Arg.Any<ApplicationUser>());
        logger.Records.ShouldBeEmpty();
    }
}
