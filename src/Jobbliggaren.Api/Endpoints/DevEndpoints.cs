using Jobbliggaren.Application.Dev.Abstractions;
using Jobbliggaren.Application.Dev.Commands.ConfirmEmail;
using Jobbliggaren.Application.Dev.Commands.ResetMyData;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// DEV-ONLY endpoints — remove before launch (Klas), together with everything they gate
/// (<c>docs/runbooks/release-checklist.md</c>). These exist solely so onboarding flows can be
/// re-tested.
///
/// <para><b>The two routes are mapped by two different methods, and that is the point.</b> They
/// have different change-reasons — one is gated on the ENVIRONMENT and can never be reachable
/// outside Development, the other on CONFIGURATION so it can be turned on for a deployed test box.
/// Kept in one call behind one condition, the unauthenticated <c>confirm-email</c> seam would sit
/// one <c>||</c> away from being re-armed in Production by an edit aimed at the other route.</para>
/// </summary>
public static class DevEndpoints
{
    /// <summary>
    /// DEV-ONLY, ENVIRONMENT-gated and NOT configurable. The caller (<c>Program.cs</c>) MUST guard
    /// this with <c>app.Environment.IsDevelopment()</c> and nothing else — no flag may widen it.
    /// <c>ProductionStartupSmokeTests</c> measures that in BOTH polarities of
    /// <c>DevTools:EnableResetMyData</c>, because "no flag widens it" is exactly the kind of claim
    /// that stays true only while someone is measuring it.
    /// </summary>
    public static void MapDevEnvironmentOnlyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dev").WithTags("Dev");

        // DEV-ONLY — token-free confirmed-login seam for the Playwright E2E suite (#796).
        // Force-confirms a test account's email so the loginAs specs can obtain a login-
        // capable user against a flag-ON backend (Auth:RequireEmailConfirmation=true)
        // without a real out-of-band email round-trip. UNAUTHENTICATED by design: the
        // caller has just registered and is login-gated (no session yet). Reachable ONLY
        // in Development — this method is mapped under Program.cs's IsDevelopment() gate
        // AND the IDevEmailConfirmer impl is DI-registered ONLY in Development (two
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

    /// <summary>
    /// DEV-ONLY, CONFIGURATION-gated: mapped in Development, and outside it only when
    /// <c>DevTools:EnableResetMyData</c> is explicitly true (Klas-direktiv 2026-08-27 — the box
    /// runs <c>ASPNETCORE_ENVIRONMENT=Production</c>, so the one environment that needed
    /// re-testing was the one that could not). The flag defaults to false, and the handler
    /// refuses independently of this gate. REMOVE BEFORE LAUNCH.
    /// </summary>
    public static void MapDevResetMyDataEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dev").WithTags("Dev");

        // DEV-ONLY — clears the current user's CV data, saved/recent searches, graded
        // matches and match preferences (re-triggers the welcome modal). Does NOT delete
        // the account — the login keeps working. Owner-scoped inside the handler
        // (ICurrentUser → JobSeeker). Returns 204. REMOVE BEFORE LAUNCH.
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
    }

    /// <summary>DEV-ONLY request body for <c>POST /api/v1/dev/confirm-email</c> (#796).</summary>
    public sealed record ConfirmEmailDevRequest(string? Email);
}
