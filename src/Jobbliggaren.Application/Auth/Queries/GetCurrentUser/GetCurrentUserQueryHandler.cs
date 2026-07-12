using Jobbliggaren.Application.Common.Abstractions;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Auth.Queries.GetCurrentUser;

public sealed partial class GetCurrentUserQueryHandler(
    ICurrentUser currentUser,
    IUserAccountService userAccountService,
    ILogger<GetCurrentUserQueryHandler> logger)
    : IQueryHandler<GetCurrentUserQuery, CurrentUserDto?>
{
    [LoggerMessage(
        EventId = 8220,
        Level = LogLevel.Warning,
        Message = "Authenticated user {UserId} has no email in the identity store. " +
                  "The account cannot self-delete (the typed confirmation has nothing to " +
                  "match) — investigate the Identity row.")]
    private static partial void LogAuthenticatedUserWithoutEmail(
        ILogger logger, Guid userId);

    public async ValueTask<CurrentUserDto?> Handle(
        GetCurrentUserQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return null;

        var userId = currentUser.UserId.Value;
        var roles = await userAccountService.GetRolesAsync(userId, cancellationToken);

        // #822: the identity store is the SSOT for the address — never a claim. The old
        // path read a claim only the retired JWT generator emitted, so the DTO carried an
        // empty string for every signed-in user.
        var email = await userAccountService.GetEmailAsync(userId, cancellationToken);

        if (email is null)
        {
            // Unreachable in practice (registration always sets an address, and a session
            // cannot outlive its Identity row), so an empty address means a broken
            // invariant — not a routine degradation. Say so out loud: it was precisely the
            // ABSENCE of a signal that let the empty email ship. userId only, never PII.
            LogAuthenticatedUserWithoutEmail(logger, userId);
        }

        // Empty string, not a 401: /me is the session probe on every (app) render, so
        // failing closed on a missing attribute would be indistinguishable from "session
        // invalid" and would lock the account out of the whole product. The consumer that
        // MUST fail closed — the irreversible account deletion — does so on its own.
        return new CurrentUserDto(userId, email ?? string.Empty, roles);
    }
}
