using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Auth.Queries.GetCurrentUser;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobSeekers.Commands.SetMatchPreferences;
using Jobbliggaren.Application.JobSeekers.Commands.UpdateMyProfile;
using Jobbliggaren.Application.JobSeekers.Commands.UpdateNotificationConsent;
using Jobbliggaren.Application.JobSeekers.Queries.GetMyProfile;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

public static class MeEndpoints
{
    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me").WithTags("Me");

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetCurrentUserQuery(), ct);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        }).RequireAuthorization();

        group.MapGet("/profile", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetMyProfileQuery(), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        group.MapPatch("/profile", async (
            UpdateMyProfileCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.Ok()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();

        // F4-12 (ADR 0076) — stated match preferences SSOT (occupation-groups /
        // regions / employment-types). PUT = idempotent full-replace of all three
        // collections (the command carries the complete set; it does not merge).
        // All-empty is a valid write (clears preferences / honest NotAssessed) — no
        // CV/PII read (the profile-builder is preference-driven). MeWritePolicy
        // (not the unrated PATCH /profile path) per the user-owned mutation precedent
        // (saved-job-ads / recent-searches), senior-cto-advisor 2026-06-19.
        group.MapPut("/match-preferences", async (
            SetMatchPreferencesCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);

        // ADR 0080 Vag 4 PR-6 — background-match notification consent (the /installningar opt-in
        // toggle + digest cadence). PUT = idempotent full-replace of {enabled, cadence}; the
        // aggregate owns the GDPR consent stamping (first opt-in immutable Art. 7(1); opt-out
        // records the Art. 7(3) withdrawal). The current state is READ via GET /profile (the
        // consent flag + cadence ride the JobSeekerProfileDto projection) — no dedicated read
        // endpoint. MeWritePolicy (user-owned mutation, parity /match-preferences). 204 / Problem 400.
        group.MapPut("/notification-consent", async (
            UpdateNotificationConsentCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);

        // GDPR Art. 17 — Right to erasure. Soft-deletar kontot + alla user-ägda
        // aggregat i samma transaction (DeleteAccountCommand → UnitOfWorkBehavior).
        // Post-commit invalideras alla Redis-sessioner via secondary user-sessions-
        // index (ADR 0024 D4 + ADR 0017 deferred-not stängd).
        // Hard-delete + Identity-DELETE + audit-anonymisering sker av
        // HardDeleteAccountsJob efter 30-dagars restore-fönster (ADR 0024 D5+D6).
        group.MapDelete("/", async (
            IMediator mediator,
            ISessionStore sessions,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new DeleteAccountCommand(), ct);
            if (result.IsFailure)
                return Results.Problem(
                    detail: result.Error.Message,
                    title: result.Error.Code,
                    statusCode: 400);

            // Failsafe: om Redis är ner får vi en exception → klienten ser 500,
            // men kontot är redan soft-deletat (idempotent re-DELETE ger ingen
            // skada vid retry). Vi medvetet INTE swallow:ar Redis-fel — sessionen
            // måste avslutas eller incidenten flaggas.
            if (currentUser.UserId.HasValue)
                await sessions.InvalidateAllForUserAsync(currentUser.UserId.Value, ct);

            return Results.NoContent();
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.AccountDeletionPolicy);
    }
}
