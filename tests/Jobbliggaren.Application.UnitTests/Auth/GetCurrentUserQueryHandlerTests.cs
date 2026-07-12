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
        userAccountService.GetRolesAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new List<string> { "User" });
        userAccountService.GetEmailAsync(userId, Arg.Any<CancellationToken>())
            .Returns("klas@example.com");

        var handler = new GetCurrentUserQueryHandler(
            currentUser, userAccountService, NullLogger<GetCurrentUserQueryHandler>.Instance);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.UserId.ShouldBe(userId);
        result.Email.ShouldBe("klas@example.com");
        result.Roles.ShouldContain("User");

        await userAccountService.Received(1).GetEmailAsync(userId, Arg.Any<CancellationToken>());
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
        userAccountService.GetRolesAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        userAccountService.GetEmailAsync(userId, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var handler = new GetCurrentUserQueryHandler(
            currentUser, userAccountService, NullLogger<GetCurrentUserQueryHandler>.Instance);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Email.ShouldBe(string.Empty);
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
