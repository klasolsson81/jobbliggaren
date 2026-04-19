using JobbPilot.Application.Auth.Commands.Login;
using JobbPilot.Application.Auth.Commands.Logout;
using JobbPilot.Application.Auth.Commands.Refresh;
using JobbPilot.Application.Auth.Commands.Register;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Api.Endpoints;

public static class AuthEndpoints
{
    private const string RefreshCookieName = "jobbpilot-refresh";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/register", async (
            RegisterCommand command, IMediator mediator, HttpContext ctx, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            if (result.IsFailure)
                return ToErrorResult(result.Error);

            AppendRefreshCookie(ctx, result.Value.RefreshToken, result.Value.RefreshTokenExpiresAt);
            return Results.Ok(new { result.Value.AccessToken, result.Value.AccessTokenExpiresAt });
        });

        group.MapPost("/login", async (
            LoginCommand command, IMediator mediator, HttpContext ctx, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            if (result.IsFailure)
                return ToErrorResult(result.Error);

            AppendRefreshCookie(ctx, result.Value.RefreshToken, result.Value.RefreshTokenExpiresAt);
            return Results.Ok(new { result.Value.AccessToken, result.Value.AccessTokenExpiresAt });
        });

        group.MapPost("/refresh", async (
            IMediator mediator, HttpContext ctx, CancellationToken ct) =>
        {
            var token = ctx.Request.Cookies[RefreshCookieName];
            if (string.IsNullOrWhiteSpace(token))
                return Results.Unauthorized();

            var result = await mediator.Send(new RefreshCommand(token), ct);
            if (result.IsFailure)
                return Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 401);

            AppendRefreshCookie(ctx, result.Value.RefreshToken, result.Value.RefreshTokenExpiresAt);
            return Results.Ok(new { result.Value.AccessToken, result.Value.AccessTokenExpiresAt });
        });

        group.MapPost("/logout", async (IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new LogoutCommand(), ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static IResult ToErrorResult(DomainError error) => error.Code switch
    {
        "Auth.InvalidCredentials" => Results.Problem(
            detail: error.Message, title: error.Code, statusCode: 401),
        _ => Results.Problem(
            detail: error.Message, title: error.Code, statusCode: 400),
    };

    private static void AppendRefreshCookie(HttpContext ctx, string token, DateTimeOffset expiresAt) =>
        ctx.Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = expiresAt,
        });
}
