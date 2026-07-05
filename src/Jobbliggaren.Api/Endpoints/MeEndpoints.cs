using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Auth.Queries.GetCurrentUser;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobSeekers.Commands.SetMatchPreferences;
using Jobbliggaren.Application.JobSeekers.Commands.UpdateFollowedCompanyNotificationConsent;
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
                : result.Error.ToProblemResult();
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
                : result.Error.ToProblemResult();
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
                : result.Error.ToProblemResult();
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);

        // ADR 0087 D3/D5 (#311 PR-2b) — company-follow notification consent (the SEPARATE opt-in
        // toggle gating company-follow digests; a distinct GDPR Art. 6/7 processing purpose from
        // background-match notifications, ADR 0087 D5). PUT = idempotent set of {enabled}; the
        // aggregate owns the GDPR consent stamping (first opt-in immutable Art. 7(1); opt-out
        // records the Art. 7(3) withdrawal). The current state is READ via GET /profile (the flag
        // rides the JobSeekerProfileDto projection) — no dedicated read endpoint. The digest cadence
        // is SHARED with background-match (ADR 0087 D2) and set via /notification-consent, so it is
        // NOT part of this contract. Without this endpoint the shipped follow-notification rail
        // (PR-4) is unreachable. MeWritePolicy (user-owned mutation, parity /notification-consent).
        // 204 / Problem 400.
        group.MapPut("/followed-company-notification-consent", async (
            UpdateFollowedCompanyNotificationConsentCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
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
                return result.Error.ToProblemResult();

            // Failsafe: om Redis är ner får vi en exception → klienten ser 500,
            // men kontot är redan soft-deletat (idempotent re-DELETE ger ingen
            // skada vid retry). Vi medvetet INTE swallow:ar Redis-fel — sessionen
            // måste avslutas eller incidenten flaggas.
            if (currentUser.UserId.HasValue)
            {
                // PR2c-0 Layer 2: plant the account-deletion tombstone BEFORE the eager
                // invalidation, so GetAsync fail-closed rejects (and self-heals) any session
                // that survives a partial InvalidateAllForUserAsync (Redis blip / race). The
                // tombstone is the durable read-path backstop (GDPR Art. 17); InvalidateAll is
                // the fast path that tears the sessions down immediately.
                //
                // CancellationToken.None (NOT the request ct): the account is already
                // soft-deleted (committed above), so this post-commit erasure MUST complete
                // regardless of a client disconnect. On the request ct, a fire-and-close between
                // the commit and this block would abort the plant → sessions survive to
                // sliding-expiry and the read-path stays open, defeating the durable-erasure
                // guarantee (code-reviewer PR2c-0 Major). Art. 17 teardown is not the caller's
                // to cancel; the plant leads (fail-closed) so it lands even if InvalidateAll throws.
                await sessions.MarkUserDeletedAsync(currentUser.UserId.Value, CancellationToken.None);
                await sessions.InvalidateAllForUserAsync(currentUser.UserId.Value, CancellationToken.None);
            }

            return Results.NoContent();
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.AccountDeletionPolicy);
    }
}
