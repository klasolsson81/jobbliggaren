using Jobbliggaren.Application.Auth.Queries.GetCurrentUser;
using Jobbliggaren.Application.Common.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #822 — den tidigare versionen av dessa tester stubbade <c>ICurrentUser.Email</c> och
/// asserterade att DTO:n bar tillbaka samma sträng. Testet var grönt medan produkten var
/// trasig: under opaka sessioner emit:as ingen e-post-claim, så handlern returnerade tom
/// sträng i skarp drift. Mocken pinnade en lögn. Identity-storen (IUserAccountService) är
/// nu enda källan — och det är den vägen dessa tester bevakar.
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

        var handler = new GetCurrentUserQueryHandler(currentUser, userAccountService);

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
        // DTO-kontraktet är icke-null. En saknad adress får degradera fältet — aldrig
        // fälla hela /me (som körs på varje (app)-sidrendering) till 401.
        var userId = Guid.NewGuid();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.GetRolesAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        userAccountService.GetEmailAsync(userId, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var handler = new GetCurrentUserQueryHandler(currentUser, userAccountService);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Email.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task Handle_WhenNotAuthenticated_ReturnsNull()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new GetCurrentUserQueryHandler(currentUser, Substitute.For<IUserAccountService>());

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.ShouldBeNull();
    }
}
