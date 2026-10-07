using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// <see cref="UserAccountService"/>'s account reads without a database.
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
        _sut = new(_userManager, Substitute.For<ILogger<UserAccountService>>(), Substitute.For<IDbExceptionInspector>(),
            AccountAccessTestKit.Coordinator(), AccountAccessTestKit.Reader(_ => null));

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
