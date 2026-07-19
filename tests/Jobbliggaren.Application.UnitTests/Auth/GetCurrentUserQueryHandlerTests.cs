using Jobbliggaren.Application.Auth.Queries.GetCurrentUser;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #822 — the previous version of these tests stubbed <c>ICurrentUser.Email</c> and
/// asserted the DTO echoed the same string back. It was green while the product was
/// broken: under opaque sessions no email claim is ever emitted, so in production the
/// handler returned an empty string. The mock pinned a lie. The identity store
/// (<c>IUserAccountService</c>) is now the only source, and that is the path these tests
/// guard — a regression to the claim would no longer compile.
/// </summary>
public class GetCurrentUserQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenAuthenticated_ResolvesEmailFromTheIdentityStore()
    {
        var userId = Guid.NewGuid();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.GetAccountSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new AccountSummary("klas@example.com", new List<string> { "User" }));

        var handler = new GetCurrentUserQueryHandler(
            currentUser, userAccountService, NullLogger<GetCurrentUserQueryHandler>.Instance);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.UserId.ShouldBe(userId);
        result.Email.ShouldBe("klas@example.com");
        result.Roles.ShouldContain("User");

        // #828 — the /me probe resolves address + roles in ONE port call. Asserting it goes through
        // GetAccountSummaryAsync (not the granular GetRolesAsync + GetEmailAsync pair) is the durable
        // guard against a regression to two identity round-trips.
        await userAccountService.Received(1)
            .GetAccountSummaryAsync(userId, Arg.Any<CancellationToken>());
        await userAccountService.DidNotReceive().GetEmailAsync(userId, Arg.Any<CancellationToken>());
        await userAccountService.DidNotReceive().GetRolesAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenIdentityStoreHasNoEmail_ReturnsEmptyStringNotNull()
    {
        // The DTO contract is non-null. A missing address may degrade the field — it must
        // never fail /me itself (which runs on every (app) page render) to a 401.
        var userId = Guid.NewGuid();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.GetAccountSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new AccountSummary(null, new List<string>()));

        var handler = new GetCurrentUserQueryHandler(
            currentUser, userAccountService, NullLogger<GetCurrentUserQueryHandler>.Instance);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Email.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task Handle_WhenEmailMissingButRolesPresent_KeepsTheRoles()
    {
        // Option A (dotnet-architect bind #828): a broken-invariant account with no address still has its
        // roles surfaced — the missing email must not collateral-drop the roles. Guards the subtle case
        // that a naive "no email => empty summary" collapse would lose.
        var userId = Guid.NewGuid();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.GetAccountSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new AccountSummary(null, new List<string> { "Admin" }));

        var handler = new GetCurrentUserQueryHandler(
            currentUser, userAccountService, NullLogger<GetCurrentUserQueryHandler>.Instance);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Email.ShouldBe(string.Empty);
        result.Roles.ShouldContain("Admin");
    }

    [Fact]
    public async Task Handle_WhenAccountRowIsGone_ReturnsEmptyDtoNotNull()
    {
        // A null summary (no identity row) must degrade to the same empty-but-present DTO as a missing
        // email — /me is the session probe, so an authenticated userId never resolves to a 401 here.
        var userId = Guid.NewGuid();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.GetAccountSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns((AccountSummary?)null);

        var handler = new GetCurrentUserQueryHandler(
            currentUser, userAccountService, NullLogger<GetCurrentUserQueryHandler>.Instance);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.UserId.ShouldBe(userId);
        result.Email.ShouldBe(string.Empty);
        result.Roles.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenNotAuthenticated_ReturnsNull()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new GetCurrentUserQueryHandler(
            currentUser,
            Substitute.For<IUserAccountService>(),
            NullLogger<GetCurrentUserQueryHandler>.Instance);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.ShouldBeNull();
    }
}
