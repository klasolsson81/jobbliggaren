using JobbPilot.Application.Common.Abstractions;
using Mediator;

namespace JobbPilot.Application.Auth.Queries.GetCurrentUser;

public sealed class GetCurrentUserQueryHandler(
    ICurrentUser currentUser,
    IUserAccountService userAccountService)
    : IQueryHandler<GetCurrentUserQuery, CurrentUserDto?>
{
    public async ValueTask<CurrentUserDto?> Handle(
        GetCurrentUserQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return null;

        var userId = currentUser.UserId.Value;
        var roles = await userAccountService.GetRolesAsync(userId, cancellationToken);

        return new CurrentUserDto(userId, string.Empty, roles);
    }
}
