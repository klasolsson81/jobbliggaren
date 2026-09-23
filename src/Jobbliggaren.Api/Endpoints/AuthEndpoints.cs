using System.Diagnostics;
using System.Globalization;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.ChangeEmail;
using Jobbliggaren.Application.Auth.Commands.ChangePassword;
using Jobbliggaren.Application.Auth.Commands.CompleteLoginChallenge;
using Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;
using Jobbliggaren.Application.Auth.Commands.ConsumeLoginLink;
using Jobbliggaren.Application.Auth.Commands.Login;
using Jobbliggaren.Application.Auth.Commands.Logout;
using Jobbliggaren.Application.Auth.Commands.RefreshSession;
using Jobbliggaren.Application.Auth.Commands.Register;
using Jobbliggaren.Application.Auth.Commands.RequestLoginChallenge;
using Jobbliggaren.Application.Auth.Commands.RequestPasswordReset;
using Jobbliggaren.Application.Auth.Commands.RequestReauthenticationChallenge;
using Jobbliggaren.Application.Auth.Commands.ResendEmailConfirmation;
using Jobbliggaren.Application.Auth.Commands.ResetPassword;
using Jobbliggaren.Application.Auth.Commands.VerifyEmail;
using Jobbliggaren.Application.Auth.Commands.VerifyEmailChangeChallenge;
using Jobbliggaren.Application.Auth.Commands.VerifyLoginChallenge;
using Jobbliggaren.Application.Auth.Commands.VerifyReauthenticationChallenge;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Api.Endpoints;

public static partial class AuthEndpoints
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

            // #714: email-confirmation-first (flag ON) mints NO session, so the response is an identical
            // 202 Accepted (empty body) for BOTH a fresh and a taken address — closing the 200-vs-400
            // account-enumeration status oracle. The only differentiator (a confirmation link vs an
            // account-exists notice) is delivered out-of-band to the submitted inbox. On the legacy
            // instant-login path (flag OFF) a session was minted → 200 + sessionId in the body, and the
            // Next.js proxy sets the HTTPOnly cookie (ADR 0018).
            return result.Value.Session is { } session
                ? Results.Ok(new { sessionId = session.SessionId })
                : Results.Accepted();
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

        // Self-service change-password + C6 (#678, epik #481). The re-auth credential is the grant from
        // /reauth/verify (#1739): ReauthenticationBehavior redeems it server-side BEFORE the handler (a
        // hijacked long-lived session cannot change the password on its own); a grant that cannot be
        // redeemed throws ReauthenticationFailedException -> byte-identical 401 (Program.cs). The current
        // password travels beside it because Identity requires it. A weak new password is a 400 (validator)
        // before UserManager runs. On success the endpoint owns C6 (below) and returns the re-issued
        // { sessionId, persistent } like /login (ADR 0018 — backend sets no cookies; the Next layer re-sets
        // the __Host- cookie). AuthWrite rate-limit — same credential-risk profile as /login.
        group.MapPost("/change-password", async (
            ChangePasswordRequest body,
            IMediator mediator,
            ISessionStore sessions,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new ChangePasswordCommand(body.ReauthGrant, body.CurrentPassword, body.NewPassword), ct);
            if (result.IsFailure)
                return ToErrorResult(result.Error);

            // The handler returns the authenticated user id (also the User.PasswordChanged audit
            // aggregate id) — use it directly, no second ICurrentUser read.
            var userId = result.Value;

            // C6 — logout-everywhere + re-issue the current session so THIS device stays logged in.
            // Read the current session's lifetime first so the replacement keeps the same profile
            // (a "Håll mig inloggad" persistent login is not silently downgraded); default to the
            // short Session profile in the can't-happen case that the session id is absent post-auth.
            var lifetime = SessionLifetime.Session;
            if (currentUser.SessionId is { } sessionId)
            {
                var current = await sessions.GetAsync(sessionId, CancellationToken.None);
                if (current is not null)
                    lifetime = current.Lifetime;
            }

            // Invalidate-BEFORE-create is a correctness invariant: CreateAsync SADDs into the user
            // index that InvalidateAllForUserAsync snapshots-then-deletes, so create-first would be
            // swept. InvalidateAll plants the COND-B tombstone; the fresh CreateAsync is not blocked
            // by it (only RotateAsync fails closed on :revoked), so the new session authenticates
            // immediately while every other device is logged out.
            //
            // CancellationToken.None: the password is already changed (committed above); a client
            // disconnect must not leave the account half-rotated (all sessions killed, none
            // re-issued). Mirrors the /me/delete post-commit teardown.
            await sessions.InvalidateAllForUserAsync(userId, CancellationToken.None);
            var reissued = await sessions.CreateAsync(userId, lifetime, CancellationToken.None);

            return Results.Ok(new
            {
                sessionId = reissued.Id.Reveal(),
                persistent = lifetime == SessionLifetime.Persistent,
            });
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

        // Self-service change-email — REQUEST step (#679, epik #481; two codes since #1739, ADR 0142 D5).
        // Re-auth-gated like change-password: the grant from /reauth/verify is redeemed server-side by
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
        // user and this address, and only then is the account moved. On success the endpoint owns C6 as
        // /change-password does: every session is invalidated and THIS device is issued a fresh one, keeping its
        // lifetime profile. The teardown is not caught: a failure there answers an error over a committed change,
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

            // Invalidate-BEFORE-create, as /change-password: CreateAsync SADDs into the user index that
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

        // Registration email-confirmation — CONFIRM step (#714). PUBLIC (no RequireAuthorization): the
        // activation link is opened from the account's own inbox, possibly logged-out or on a different
        // device, so the opaque token IS the authorization. Every rejection is a uniform 400 (no
        // account/enumeration oracle). On success EmailConfirmed is
        // set and the user can log in; NO session is issued (the confirming client is not necessarily
        // the user's) and NO logout-everywhere (this is not a recovery-vector change — the address was
        // always the account's). AuthWrite rate-limit (per-IP) against generic abuse; the opaque token
        // is not brute-forceable, so no per-uid limiter is needed.
        group.MapPost("/verify-email", async (
            VerifyEmailRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new VerifyEmailCommand(body.Uid, body.Token), ct);
            return result.IsFailure
                ? ToErrorResult(result.Error)
                : Results.NoContent();
        }).RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

        // Registration email-confirmation — RESEND step (#733). PUBLIC (no RequireAuthorization): the
        // stuck user is unauthenticated (login-403) or just-registered with no session, so the uniform
        // response is the authorization-free contract (parity /verify-email). ALWAYS 202 Accepted — a
        // malformed email is the only 400 (existence-INDEPENDENT, not an oracle): a fresh-unconfirmed, a
        // taken-confirmed and a non-existent address are indistinguishable on status AND body. The send is
        // INLINE Api-side (mint+send in one process / one Data-Protection keyring so the link resolves at
        // /verify-email; CTO 2026-07-10, recorded on the handler). ⚠ The residual response-timing channel
        // here is NOT rate-capped by the per-target cooldown — that claim is withdrawn (security-auditor
        // 2026-08-10): a per-address window caps REPEATED sampling of one address, while enumeration needs
        // exactly one measurement per candidate. What binds is AuthWrite, per-IP and parallelisable. It IS
        // inert while the flag is OFF. #1171 moved the reset path's send off the request path for this
        // reason; this endpoint has not been reworked and its channel is open when the flag is on. AuthWrite
        // (per-IP) + that per-target Redis cooldown (handler) throttle email-bombing.
        group.MapPost("/resend-confirmation", async (
            ResendConfirmationRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new ResendEmailConfirmationCommand(body.Email), ct);
            return result.IsFailure
                ? ToErrorResult(result.Error)
                : Results.Accepted();
        }).RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

        // Password reset — REQUEST step (#1171). PUBLIC: the requester has lost access by definition, so
        // there is nothing to authenticate with. ALWAYS 202 Accepted for a known address, an unknown one
        // and a cooled repeat alike — a malformed email is the only 400, and it is existence-INDEPENDENT
        // so it is not an oracle (parity /resend-confirmation).
        //
        // The ONE non-202 is a 503 (Auth.EmailDeliveryUnavailable) when no configured sender can deliver,
        // and it is not an oracle because of WHERE the handler decides it: the capability check is the
        // handler's first statement and reads no input, so the split is a property of the server's
        // configuration, evaluated before the submitted address is looked at. Placed after the account
        // lookup it would be reachable only for existing accounts — which is why /resend-confirmation,
        // whose check sits after its lookup, must never return 503 at all.
        //
        // AuthWrite (per-IP, and its rejection is 429 rather than 503 because RateLimitingExtensions
        // overrides ASP.NET's default) plus the per-target 60s Redis cooldown throttle email-bombing. The
        // cooldown does that and ONLY that: it does not rate-cap a timing channel, because a per-address
        // window caps repeated sampling of one address while enumeration needs one measurement per
        // candidate. There is no timing channel left to cap — the lookup, the mint and the provider round
        // trip all moved behind IPasswordResetDispatcher, so the request path never reads the account
        // (senior-cto-advisor 2026-08-10).
        group.MapPost("/forgot-password", async (
            ForgotPasswordRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new RequestPasswordResetCommand(body.Email), ct);
            return result.IsFailure
                ? ToErrorResult(result.Error)
                : Results.Accepted();
        }).RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

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

        // Password reset — APPLY step (#1171). PUBLIC: the link is opened from the account's own inbox,
        // logged out by definition, so the opaque single-use token IS the authorization. Every TOKEN
        // rejection is a uniform 400; a PASSWORD rejection names its rule, which is safe because Identity
        // verifies the token BEFORE running the password validators — that arm is reachable only by
        // someone already holding a valid token. In practice that is Auth.PwnedPassword alone:
        // ResetPasswordCommandValidator carries Identity's own 12-character floor, so ValidationBehavior
        // fells a short password first and answers with the {errors} shape instead — Auth.PasswordTooShort
        // is never emitted on this route.
        //
        // On success the endpoint enacts C6 (logout-everywhere) with NO re-issue, unlike /change-password:
        // the actor here is anonymous and the link may be opened on any device, so minting a session for
        // whoever opened it would turn recovery into login. 204, and the user logs in with the new password.
        group.MapPost("/reset-password", async (
            ResetPasswordRequest body,
            IMediator mediator,
            ISessionStore sessions,
            ILogger<ResetPasswordCommand> logger,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new ResetPasswordCommand(body.Uid, body.Token, body.NewPassword), ct);
            if (result.IsFailure)
                return ToErrorResult(result.Error);

            var userId = result.Value;

            // C6 — the password just changed via a recovery vector, so every session dies. The Redis store
            // is independent of Identity's SecurityStamp, so the stamp rotation inside ResetPasswordAsync
            // does NOT touch it and this call is the only logout-everywhere mechanism.
            // CancellationToken.None: the reset is committed; a disconnect must not leave sessions alive.
            // Best-effort + logged as a security event — a Redis blip must not fail a completed reset
            // (the token is already spent, so a retry would report "invalid link"), but live-session
            // residue after a possible account takeover must be detectable.
            try
            {
                await sessions.InvalidateAllForUserAsync(userId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                LogResetSessionInvalidationFailed(logger, ex, userId);
            }

            return Results.NoContent();
        }).RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);
    }

    /// <summary>
    /// POST /auth/change-password body — the re-auth grant (redeemed server-side by
    /// ReauthenticationBehavior, #1739), the current password (Identity requires it) and the new password
    /// (strength-validated by ChangePasswordCommandValidator). A pure transport DTO; no value is logged.
    /// </summary>
    public sealed record ChangePasswordRequest(string? ReauthGrant, string? CurrentPassword, string? NewPassword);

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

    /// <summary>
    /// POST /auth/forgot-password body — the address to send a reset link to. A pure transport DTO; the
    /// address is never logged, and the response is identical whether or not it belongs to an account.
    /// </summary>
    public sealed record ForgotPasswordRequest(string? Email);

    /// <summary>
    /// POST /auth/reset-password body — the userId and opaque token from the emailed link, plus the new
    /// password. A pure transport DTO; no value is logged.
    /// <para>
    /// <c>Uid</c> is a <see cref="Guid"/>, so the emailed link MUST carry the dashed "D" form:
    /// System.Text.Json's Guid converter accepts only that, and a compact "N" uid 400s at the binder on
    /// every click (#981). <c>EmailTemplates.PasswordReset</c> renders <c>{UserId:D}</c> for this reason.
    /// </para>
    /// </summary>
    public sealed record ResetPasswordRequest(Guid Uid, string? Token, string? NewPassword);

    /// <summary>
    /// POST /auth/verify-email body — the (uid, URL-safe token) carried by the registration activation
    /// link and posted from the public landing page. Token-gated (the link is opened from the account's
    /// inbox, possibly logged-out): the token is the authorization. No email is needed (the address is
    /// not changing). A pure transport DTO; the token is never logged.
    /// </summary>
    public sealed record VerifyEmailRequest(Guid Uid, string? Token);

    /// <summary>
    /// POST /auth/resend-confirmation body — the email address to re-send a registration confirmation
    /// link to (#733). A pure transport DTO; the address is never logged. The response is a uniform 202
    /// regardless of whether the address has an unconfirmed account (anti-enumeration).
    /// </summary>
    public sealed record ResendConfirmationRequest(string? Email);

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
        // Byte-identical 401 shared with the central ReauthenticationFailedException arm
        // (Program.cs) via AuthProblem — see AuthProblem for the oracle rationale.
        AuthErrorCodes.InvalidCredentials or AuthErrorCodes.AccountLocked => AuthProblem.InvalidCredentials(),

        // #714 — email-confirmation-first login gate. A distinct, actionable 403 ("confirm your email
        // first"): reachable ONLY after a correct password (UserAccountService.ValidateCredentialsAsync),
        // so it is not an enumeration oracle — a wrong password / unknown account still funnels to the
        // byte-identical 401 above. 403 ("we know who you are, but you can't proceed") is an
        // endpoint-local status like the 401 arm — no new ErrorKind (the kind-union models
        // 400/404/409/410; #239 Variant B, RFC 9110 §15.5.4). Same ProblemDetails shape as the central
        // mapper (title=code, detail=message). This arm is reachable only via /login.
        AuthErrorCodes.EmailNotConfirmed => Results.Problem(
            detail: AuthErrorCodes.EmailNotConfirmedMessage,
            title: AuthErrorCodes.EmailNotConfirmed,
            statusCode: StatusCodes.Status403Forbidden),

        // ADR 0083 Amendment 2026-08-03 — public registration is held closed while the app is
        // reachable but its launch gates are not green. 503 is the SERVER-AVAILABILITY axis
        // ("capacity deliberately withheld, and coming back" — RFC 9110 §15.6.4 names scheduled
        // maintenance), which is a third axis distinct from the 400/404/409/410 request/resource
        // semantics the kind-union models — same rule as the 401 identity arm (#239 Variant B) and
        // the 403 authorization arm (#714) above, applied a third time. It is therefore NOT the §3
        // per-endpoint Code-matching anti-pattern: that ban targets the heuristic
        // Code.EndsWith(".NotFound") shape, not a named constant in one auth switch that still
        // falls through to the central mapper.
        //
        // No Retry-After: the opening date is unknown, and a wrong Retry-After is worse than none
        // (clients and caches honour it). POST /auth/register and POST /auth/challenge/complete return
        // this — the health endpoints are untouched, so uptime monitoring is unaffected.
        AuthErrorCodes.RegistrationsClosed => Results.Problem(
            detail: AuthErrorCodes.RegistrationsClosedMessage,
            title: AuthErrorCodes.RegistrationsClosed,
            statusCode: StatusCodes.Status503ServiceUnavailable),

        // #1087 — no transactional email provider is configured, so a flow whose success is DEFINED
        // by delivery refuses instead of reporting a completed action that cannot occur. The SAME
        // availability axis as the RegistrationsClosed arm directly above, applied a fourth time
        // (after the 401 identity arm and the 403 authorization arm): capacity deliberately withheld,
        // returning when someone sets Email:Provider. No new ErrorKind — see AuthErrorCodes
        // .EmailDeliveryUnavailable for why the fork the CTO named is closed by this precedent.
        //
        // No Retry-After, for the reason written on the arm above: the date is unknown and a wrong
        // one is worse than none.
        //
        // Public producers since #1171 — the earlier note that this was reachable only from the
        // authenticated /auth/change-email no longer holds, so the reason it discloses nothing about any
        // address is different for each:
        //   · POST /auth/change-email — authenticated and re-authenticated, so the caller already
        //     owns the account and learns nothing new.
        //   · POST /auth/forgot-password — unauthenticated, and safe instead by ORDER: the handler's
        //     capability check is its first statement and reads no input, so this 503 is decided
        //     before the submitted address is looked at and cannot vary with it. Move that check
        //     after the account lookup and this arm becomes an enumeration oracle.
        //   · POST /auth/challenge (#1735) — unauthenticated, and safe by the same ORDER: its handler
        //     checks capability first, before it reads the address.
        AuthErrorCodes.EmailDeliveryUnavailable => Results.Problem(
            detail: AuthErrorCodes.EmailDeliveryUnavailableMessage,
            title: AuthErrorCodes.EmailDeliveryUnavailable,
            statusCode: StatusCodes.Status503ServiceUnavailable),

        _ => error.ToProblemResult(),
    };

    // #1171 — the password-reset teardown is best-effort: live-session residue must be detectable. Keeps the
    // full exception (a Redis fault's stack aids ops and carries no user PII); only the userId surrogate.
    // After a RESET, live-session residue means an account that may have just been taken over still has the
    // attacker's sessions.
    [LoggerMessage(4004, LogLevel.Error,
        "Password reset: session invalidation FAILED for user {UserId} — " +
        "password changed, sessions may still be live")]
    private static partial void LogResetSessionInvalidationFailed(ILogger logger, Exception ex, Guid userId);
}
