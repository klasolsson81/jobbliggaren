using System.Diagnostics;
using System.Globalization;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.ChangeEmail;
using Jobbliggaren.Application.Auth.Commands.CompleteLoginChallenge;
using Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;
using Jobbliggaren.Application.Auth.Commands.ConsumeLoginLink;
using Jobbliggaren.Application.Auth.Commands.Logout;
using Jobbliggaren.Application.Auth.Commands.RefreshSession;
using Jobbliggaren.Application.Auth.Commands.RequestLoginChallenge;
using Jobbliggaren.Application.Auth.Commands.RequestReauthenticationChallenge;
using Jobbliggaren.Application.Auth.Commands.VerifyEmailChangeChallenge;
using Jobbliggaren.Application.Auth.Commands.VerifyLoginChallenge;
using Jobbliggaren.Application.Auth.Commands.VerifyReauthenticationChallenge;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/logout", async (IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new LogoutCommand(), ct);
            // Cookie-radering sker i Next.js-proxyn (ADR 0018) — backend är cookie-agnostiskt.
            return Results.NoContent();
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.AuthLoosePolicy);

        // Slides the current session and rotates its id if due (#481 persistent-login).
        // Called by the Next.js proxy refresh seam. The id is validated + slid by the
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

        // Re-authentication — REQUEST step (#1739, ADR 0142 D5). AUTHENTICATED: the code goes to the account's
        // own address, resolved from the session, and the body carries nothing. Every refusal is visible,
        // because the caller is the account holder: 503 when the sender cannot deliver, 409 inside the
        // cooldown, 409 past the day's codes. The challenge id is the requester's alone; it appears in no log
        // line and no URL.
        group.MapPost("/reauth", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new RequestReauthenticationChallengeCommand(), ct);
            return result.IsFailure
                ? ToErrorResult(result.Error)
                : Results.Accepted(uri: (string?)null, value: new { challengeId = result.Value.Reveal() });
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

        // Re-authentication — VERIFY step (#1739). AUTHENTICATED: the code is presented against the challenge
        // id, and the store asserts that the challenge belongs to this user and this purpose. A verified code
        // is a single-use grant the sensitive operation then carries as `reauthGrant`; never a session. The
        // failures are the login code's: a wrong code 400, a burned or expired one 410.
        group.MapPost("/reauth/verify", async (
            ReauthenticationVerifyRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new VerifyReauthenticationChallengeCommand(body.ChallengeId, body.Code), ct);
            return result.IsFailure
                ? ToErrorResult(result.Error)
                : Results.Ok(new { reauthGrant = result.Value.Reveal() });
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

        // Self-service change-email — REQUEST step (#679, epik #481; two codes since #1739, ADR 0142 D5).
        // Re-auth-gated: the grant from /reauth/verify is redeemed server-side by
        // ReauthenticationBehavior (unusable -> byte-identical 401) BEFORE the handler. A taken address is a 409,
        // a malformed one a 400. On success a code goes to the NEW address and the answer is 202 with the
        // challenge id — the email is NOT changed and NO session is touched until the code is verified and the
        // change confirmed. The challenge id is the requester's alone; it appears in no log line and no URL.
        group.MapPost("/change-email", async (
            ChangeEmailRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new ChangeEmailCommand(body.ReauthGrant, body.NewEmail), ct);
            return result.IsFailure
                ? ToErrorResult(result.Error)
                : Results.Accepted(uri: (string?)null, value: new { challengeId = result.Value.ChallengeId.Reveal() });
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

        // Change-email — VERIFY step (#1739). AUTHENTICATED: the code mailed to the new address is presented
        // against the challenge id, and the store asserts that the challenge belongs to this user and this
        // purpose. A verified code is a single-use grant for this user and the proven address; never a session.
        // The failures are the login code's: a wrong code 400, a burned or expired one 410.
        group.MapPost("/change-email/verify", async (
            EmailChangeVerifyRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new VerifyEmailChangeChallengeCommand(body.ChallengeId, body.Code), ct);
            return result.IsFailure
                ? ToErrorResult(result.Error)
                : Results.Ok(new { changeEmailGrant = result.Value.Reveal() });
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

        // Change-email — CONFIRM step (#679; a grant since #1739). AUTHENTICATED: the grant is redeemed for this
        // user and this address, and only then is the account moved. On success the endpoint owns C6: every
        // session is invalidated and THIS device is issued a fresh one, keeping its lifetime profile. The teardown is not caught: a failure there answers an error over a committed change,
        // never a 200 claiming other devices were logged out. CancellationToken.None: the change is committed; a
        // client disconnect must not leave the account half-rotated.
        group.MapPost("/change-email/confirm", async (
            EmailChangeConfirmRequest body,
            IMediator mediator,
            ISessionStore sessions,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new ConfirmEmailChangeCommand(body.ChangeEmailGrant, body.NewEmail), ct);
            if (result.IsFailure)
                return ToErrorResult(result.Error);

            var userId = result.Value;

            var lifetime = SessionLifetime.Session;
            if (currentUser.SessionId is { } sessionId)
            {
                var current = await sessions.GetAsync(sessionId, CancellationToken.None);
                if (current is not null)
                    lifetime = current.Lifetime;
            }

            // Invalidate-BEFORE-create: CreateAsync SADDs into the user index that
            // InvalidateAllForUserAsync snapshots-then-deletes.
            await sessions.InvalidateAllForUserAsync(userId, CancellationToken.None);
            var reissued = await sessions.CreateAsync(userId, lifetime, CancellationToken.None);

            return Results.Ok(new
            {
                sessionId = reissued.Id.Reveal(),
                persistent = lifetime == SessionLifetime.Persistent,
            });
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

        // Login challenge — REQUEST step (#1735, ADR 0142 D2). PUBLIC and uniform: every well-formed address
        // answers 202 with a challenge id, whether it has an account, was just used, or is over its budget;
        // the only other answers are a format 400 and the 503s (a sender that cannot deliver, a store that is
        // unreachable), none of which depends on the address. The request path reads no account.
        group.MapPost("/challenge", async (
            LoginChallengeRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new RequestLoginChallengeCommand(body.Email), ct);
            return result.IsFailure
                ? ToErrorResult(result.Error)
                : Results.Accepted(uri: (string?)null, value: new { challengeId = result.Value.Reveal() });
        }).RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

        // Login challenge — the two PROOF steps (#1735, ADR 0142 D3). A code presented against the challenge
        // id, or a link's token; either one, once, ends in the same outcome, resolved at proof time. Failures
        // go to the central kind-mapper: a wrong code is 400, a burned or expired one and any unusable link
        // 410. Neither route reads a password or touches lockout. A signed-in outcome is always a
        // persistent session (D4), so the body carries no persistent flag.
        group.MapPost("/challenge/verify", async (
            LoginChallengeVerifyRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new VerifyLoginChallengeCommand(body.ChallengeId, body.Code), ct);
            return result.IsFailure ? ToErrorResult(result.Error) : LoginOutcomeResult(result.Value);
        }).RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

        group.MapPost("/link", async (
            LoginLinkRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new ConsumeLoginLinkCommand(body.Token), ct);
            return result.IsFailure ? ToErrorResult(result.Error) : LoginOutcomeResult(result.Value);
        }).RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

        // Login challenge — the COMPLETE step (#1737, ADR 0142 D3). A proven new address accepts the terms and
        // gets its account. PUBLIC: the grant from `consentRequired` is the authorization, and the body
        // carries no address, so nothing here can vary with one. It answers the same outcome union as the
        // two proof steps; a grant that cannot be used, for any reason, is one 410.
        group.MapPost("/challenge/complete", async (
            LoginChallengeCompleteRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new CompleteLoginChallengeCommand(body.GrantToken, body.AcceptTerms), ct);
            return result.IsFailure ? ToErrorResult(result.Error) : LoginOutcomeResult(result.Value);
        }).RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

    }

    /// <summary>
    /// POST /auth/change-email body — the re-auth grant (redeemed server-side by ReauthenticationBehavior,
    /// #1739) and the new email address (a code goes there before any swap). A pure transport DTO; neither value
    /// is logged.
    /// </summary>
    public sealed record ChangeEmailRequest(string? ReauthGrant, string? NewEmail);

    /// <summary>POST /auth/change-email/verify body (#1739). The code is a credential and is never logged.</summary>
    public sealed record EmailChangeVerifyRequest(string? ChallengeId, string? Code);

    /// <summary>
    /// POST /auth/change-email/confirm body (#1739) — the grant /change-email/verify answered and the address it
    /// was issued for. The grant is a credential and is never logged.
    /// </summary>
    public sealed record EmailChangeConfirmRequest(string? ChangeEmailGrant, string? NewEmail);

    /// <summary>POST /auth/challenge body (#1735). A pure transport DTO; the address is never logged.</summary>
    public sealed record LoginChallengeRequest(string? Email);

    /// <summary>POST /auth/challenge/verify body (#1735). The code is a credential and is never logged.</summary>
    public sealed record LoginChallengeVerifyRequest(string? ChallengeId, string? Code);

    /// <summary>POST /auth/reauth/verify body (#1739). The code is a credential and is never logged.</summary>
    public sealed record ReauthenticationVerifyRequest(string? ChallengeId, string? Code);

    /// <summary>POST /auth/link body (#1735). The token is a credential and is never logged.</summary>
    public sealed record LoginLinkRequest(string? Token);

    /// <summary>
    /// POST /auth/challenge/complete body (#1737). The grant token is a credential and is never logged. An
    /// omitted <c>acceptTerms</c> binds false and is refused.
    /// </summary>
    public sealed record LoginChallengeCompleteRequest(string? GrantToken, bool AcceptTerms);

    // Every outcome is a 200 carrying `outcome`. Internal so a test can hand it every variant: the default
    // arm below would otherwise turn a variant added without its arm into a 500 found at runtime.
    internal static IResult LoginOutcomeResult(LoginOutcome outcome) => outcome switch
    {
        LoginOutcome.SignedIn signedIn => Results.Ok(new
        {
            outcome = LoginOutcome.SignedIn.WireName,
            sessionId = signedIn.SessionId,
        }),
        LoginOutcome.PendingDeletion pending => Results.Ok(new
        {
            outcome = LoginOutcome.PendingDeletion.WireName,
            permanentDeletionDate = pending.PermanentDeletionEarliest.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture),
        }),
        LoginOutcome.RegistrationClosed => Results.Ok(new { outcome = LoginOutcome.RegistrationClosed.WireName }),
        LoginOutcome.ConsentRequired consent => Results.Ok(new
        {
            outcome = LoginOutcome.ConsentRequired.WireName,
            grantToken = consent.Grant.Reveal(),
        }),
        LoginOutcome.AccountUnavailable => Results.Ok(new { outcome = LoginOutcome.AccountUnavailable.WireName }),
        _ => throw new UnreachableException($"Unmapped login outcome {outcome.GetType().Name}."),
    };

    // 401 is an authentication-identity status ("who are you"), a different axis from the
    // request/resource-semantics the kind-union models (400/404/409/410) — so it stays an
    // endpoint-local concern rather than a new ErrorKind (senior-cto-advisor 2026-06-26, #239
    // Decision 1 Variant B; RFC 9110 §15.5.2). Every other Auth failure delegates
    // to the central kind-mapper so the 400/404/409/410 rule lives in exactly one place (DRY).
    private static IResult ToErrorResult(DomainError error) => error.Code switch
    {
        // Byte-identical 401 shared with the central ReauthenticationFailedException arm
        // (Program.cs) via AuthProblem — see AuthProblem for the oracle rationale.
        AuthErrorCodes.InvalidCredentials => AuthProblem.InvalidCredentials(),

        // ADR 0083 Amendment 2026-08-03 — public registration is held closed while the app is
        // reachable but its launch gates are not green. 503 is the SERVER-AVAILABILITY axis
        // ("capacity deliberately withheld, and coming back" — RFC 9110 §15.6.4 names scheduled
        // maintenance), which is a third axis distinct from the 400/404/409/410 request/resource
        // semantics the kind-union models — same rule as the 401 identity arm (#239 Variant B) above.
        // It is therefore NOT the §3
        // per-endpoint Code-matching anti-pattern: that ban targets the heuristic
        // Code.EndsWith(".NotFound") shape, not a named constant in one auth switch that still
        // falls through to the central mapper.
        //
        // No Retry-After: the opening date is unknown, and a wrong Retry-After is worse than none
        // (clients and caches honour it). POST /auth/challenge/complete returns this — the health
        // endpoints are untouched, so uptime monitoring is unaffected.
        AuthErrorCodes.RegistrationsClosed => Results.Problem(
            detail: AuthErrorCodes.RegistrationsClosedMessage,
            title: AuthErrorCodes.RegistrationsClosed,
            statusCode: StatusCodes.Status503ServiceUnavailable),

        // #1087 — no transactional email provider is configured, so a flow whose success is DEFINED
        // by delivery refuses instead of reporting a completed action that cannot occur. The SAME
        // availability axis as the RegistrationsClosed arm directly above: capacity deliberately
        // withheld, returning when someone sets Email:Provider. No new ErrorKind — see AuthErrorCodes
        // .EmailDeliveryUnavailable for why the fork the CTO named is closed by this precedent.
        //
        // No Retry-After, for the reason written on the arm above: the date is unknown and a wrong
        // one is worse than none.
        //
        // Its producers, and why it discloses nothing about any address:
        //   · POST /auth/change-email — authenticated and re-authenticated, so the caller already
        //     owns the account and learns nothing new.
        //   · POST /auth/challenge (#1735) — unauthenticated, and safe by ORDER: the handler's
        //     capability check is its first statement and reads no input, so this 503 is decided
        //     before the submitted address is looked at and cannot vary with it. Move that check
        //     after the account lookup and this arm becomes an enumeration oracle.
        AuthErrorCodes.EmailDeliveryUnavailable => Results.Problem(
            detail: AuthErrorCodes.EmailDeliveryUnavailableMessage,
            title: AuthErrorCodes.EmailDeliveryUnavailable,
            statusCode: StatusCodes.Status503ServiceUnavailable),

        _ => error.ToProblemResult(),
    };
}
