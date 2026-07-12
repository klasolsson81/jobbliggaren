using Jobbliggaren.Application.Dev.Abstractions;
using Jobbliggaren.Application.Dev.Commands.ConfirmEmail;
using Jobbliggaren.Application.Dev.Commands.ResetMyData;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// DEV-ONLY endpoints — NOT mapped in production; remove before launch (Klas).
/// These exist solely so Klas can re-test onboarding flows locally. The caller
/// (<c>Program.cs</c>) MUST guard the registration with
/// <c>app.Environment.IsDevelopment()</c> so the routes never exist in a deployed
/// environment (defense in depth alongside the FE button being dev-gated too).
/// </summary>
public static class DevEndpoints
{
    public static void MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dev").WithTags("Dev");

        // DEV-ONLY — clears the current user's CV data, saved/recent searches and
        // resets match preferences to Empty (re-triggers the welcome modal). Does
        // NOT delete the account — the login keeps working. Owner-scoped inside the
        // handler (ICurrentUser → JobSeeker). Returns 204. REMOVE BEFORE LAUNCH.
        group.MapPost("/reset-my-data", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new ResetMyDataCommand(), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(
                    detail: result.Error.Message,
                    title: result.Error.Code,
                    statusCode: 400);
        }).RequireAuthorization();

        // DEV-ONLY — token-free confirmed-login seam for the Playwright E2E suite (#796).
        // Force-confirms a test account's email so the loginAs specs can obtain a login-
        // capable user against a flag-ON backend (Auth:RequireEmailConfirmation=true)
        // without a real out-of-band email round-trip. UNAUTHENTICATED by design: the
        // caller has just registered and is login-gated (no session yet). Reachable ONLY
        // in Development — this whole group is mapped under Program.cs's IsDevelopment()
        // gate AND the IDevEmailConfirmer impl is DI-registered ONLY in Development (two
        // independent structural gates). REMOVE BEFORE LAUNCH.
        group.MapPost("/confirm-email", async (
            ConfirmEmailDevRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body?.Email))
                return Results.BadRequest();

            var outcome = await mediator.Send(new ConfirmEmailDevCommand(body.Email), ct);
            return outcome == DevEmailConfirmOutcome.Confirmed
                ? Results.NoContent()
                : Results.NotFound();
        });
    }

    /// <summary>DEV-ONLY request body for <c>POST /api/v1/dev/confirm-email</c> (#796).</summary>
    public sealed record ConfirmEmailDevRequest(string? Email);
}
