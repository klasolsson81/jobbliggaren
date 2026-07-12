using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Auth.Queries.GetCurrentUser;

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

        // #822: e-posten hämtas ur identity-storen (SSOT), inte ur en claim. Den gamla
        // vägen (ICurrentUser.Email) läste en claim som bara den avvecklade JWT-
        // generatorn emit:ade, så DTO:n bar tom sträng för ALLA inloggade användare —
        // vilket i sin tur dödade den typade bekräftelsen i radera-konto-dialogen.
        // Att i stället cacha adressen i session-posten avvisades: den blir inaktuell
        // efter ett e-postbyte (#679) och lägger klartext-PII i Redis, som idag inte
        // bär någon (ADR 0066/0049).
        //
        // Tom sträng vid saknad adress bevarar DTO-kontraktet (aldrig null). En
        // fail-closed 401 här hade riskerat total utelåsning för ett konto utan
        // e-post — /me körs på varje (app)-sidrendering.
        var email = await userAccountService.GetEmailAsync(userId, cancellationToken);

        return new CurrentUserDto(userId, email ?? string.Empty, roles);
    }
}
