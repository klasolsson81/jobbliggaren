using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.Login;
using Jobbliggaren.Application.Auth.Commands.Logout;
using Jobbliggaren.Application.Auth.Commands.RefreshSession;
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

        group.MapPost("/logout", async (IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new LogoutCommand(), ct);
            // Cookie-radering sker i Next.js-proxyn (ADR 0018) — backend är cookie-agnostiskt.
            return Results.NoContent();
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.AuthLoosePolicy);

        // Slides the current session and rotates its id if due (#481 persistent-login).
        // Called by the Next.js middleware refresh seam. The id is validated + slid by the
        // auth pipeline (GetAsync), then rotated-if-due. On { rotated: true } the proxy
        // replaces the __Host- cookie value with the returned sessionId (ADR 0018 — backend
        // sets no cookies). Driven by the Next.js proxy refresh seam wired in the 2b-3b
        // activation. AuthLoose rate-limit: same interval-driven profile as logout.
        group.MapPost("/refresh", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new RefreshSessionCommand(), ct);
            return result.IsFailure
                ? ToErrorResult(result.Error)
                : Results.Ok(result.Value);
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

    // 401 is an authentication-identity status ("who are you"), a different axis from the
    // request/resource-semantics the kind-union models (400/404/409/410) — so it stays an
    // endpoint-local concern rather than a new ErrorKind (senior-cto-advisor 2026-06-26, #239
    // Decision 1 Variant B; RFC 9110 §15.5.2). The 401 here also preserves the deliberate
    // deleted-account oracle-avoidance (a soft-deleted login returns the same Auth.InvalidCredentials
    // as a wrong password — docs/runbooks/account-deletion.md). Every other Auth failure delegates
    // to the central kind-mapper so the 400/404/409/410 rule lives in exactly one place (DRY).
    //
    // #503 G3 (senior-cto-advisor): AccountLocked is an INTERNAL discriminant (it lets the login
    // handler emit an account_locked_out audit) that MUST render byte-identically to a wrong-password
    // 401 — same status, title AND detail — so lockout state leaks neither account existence
    // (enumeration) nor a DoS-target confirmation. The arm reuses the InvalidCredentials literals
    // verbatim and never surfaces error.Code/error.Message from the AccountLocked error. Pinned by
    // the oracle-parity integration tests (LockoutTests).
    private static IResult ToErrorResult(DomainError error) => error.Code switch
    {
        AuthErrorCodes.InvalidCredentials or AuthErrorCodes.AccountLocked => Results.Problem(
            detail: "E-post eller lösenord är felaktigt.",
            title: AuthErrorCodes.InvalidCredentials,
            statusCode: StatusCodes.Status401Unauthorized),
        _ => error.ToProblemResult(),
    };
}
