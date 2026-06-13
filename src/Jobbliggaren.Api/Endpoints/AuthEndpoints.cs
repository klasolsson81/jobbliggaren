using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Auth.Commands.Login;
using Jobbliggaren.Application.Auth.Commands.Logout;
using Jobbliggaren.Application.Auth.Commands.Register;
using Jobbliggaren.Application.Auth.Queries.VerifyCredentials;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/register", async (
            RegisterCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            if (result.IsFailure)
                return ToErrorResult(result.Error);

            // Session-id returneras i response body — Next.js-proxyn sätter HTTPOnly-cookie (ADR 0018).
            return Results.Ok(new { sessionId = result.Value.SessionId });
        }).RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

        group.MapPost("/login", async (
            LoginCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            if (result.IsFailure)
                return ToErrorResult(result.Error);

            return Results.Ok(new { sessionId = result.Value.SessionId });
        }).RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

        // [Obsolete] 410 Gone — ersatt av session-baserad auth i Turn 4, ADR 0017.
        // Raderas i Fas 1 tillsammans med RefreshTokenStore och övrig JWT-infrastruktur.
        group.MapPost("/refresh", () =>
            Results.Problem(
                detail: "Refresh-flödet är ersatt av session-baserad autentisering. " +
                        "Använd /auth/login för ny session. Se ADR 0017.",
                title: "Gone",
                statusCode: StatusCodes.Status410Gone))
            .ProducesProblem(StatusCodes.Status410Gone)
            .WithSummary("[Obsolete] Refresh-flödet är ersatt av session-baserad auth — se ADR 0017");

        group.MapPost("/logout", async (IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new LogoutCommand(), ct);
            // Cookie-radering sker i Next.js-proxyn (ADR 0018) — backend är cookie-agnostiskt.
            return Results.NoContent();
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.AuthLoosePolicy);

        // Re-autentisering före destruktiv operation (TD-28 / OWASP ASVS V6.2.5).
        // Validerar lösenord för aktuell session-användare utan att skapa eller
        // ändra sessioner. Klienten skickar endast { password } — email tas från
        // claim. Rate-limit AuthWrite (20/min per IP) — samma riskprofil som login.
        group.MapPost("/verify", async (
            VerifyCredentialsQuery query, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(query, ct);
            return result.IsSuccess
                ? Results.NoContent()
                : ToErrorResult(result.Error);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);
    }

    private static IResult ToErrorResult(DomainError error) => error.Code switch
    {
        "Auth.InvalidCredentials" => Results.Problem(
            detail: error.Message, title: error.Code, statusCode: 401),
        _ => Results.Problem(
            detail: error.Message, title: error.Code, statusCode: 400),
    };
}
