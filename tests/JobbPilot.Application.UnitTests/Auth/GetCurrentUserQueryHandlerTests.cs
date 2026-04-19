using JobbPilot.Application.Auth.Queries.GetCurrentUser;
using JobbPilot.Application.Common.Abstractions;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Auth;

public class GetCurrentUserQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenAuthenticated_ReturnsCurrentUserDto()
    {
        var userId = Guid.NewGuid();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.GetRolesAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new List<string> { "User" });

        var handler = new GetCurrentUserQueryHandler(currentUser, userAccountService);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.UserId.ShouldBe(userId);
        result.Roles.ShouldContain("User");
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
