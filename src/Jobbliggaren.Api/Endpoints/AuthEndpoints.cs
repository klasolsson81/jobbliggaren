using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.ChangeEmail;
using Jobbliggaren.Application.Auth.Commands.ChangePassword;
using Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;
using Jobbliggaren.Application.Auth.Commands.Login;
using Jobbliggaren.Application.Auth.Commands.Logout;
using Jobbliggaren.Application.Auth.Commands.RefreshSession;
using Jobbliggaren.Application.Auth.Commands.Register;
using Jobbliggaren.Application.Auth.Commands.RequestPasswordReset;
using Jobbliggaren.Application.Auth.Commands.ResendEmailConfirmation;
using Jobbliggaren.Application.Auth.Commands.ResetPassword;
using Jobbliggaren.Application.Auth.Commands.VerifyEmail;
using Jobbliggaren.Application.Auth.Queries.VerifyCredentials;
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

        // Self-service change-password + C6 (#678, epik #481). The CURRENT password is the re-auth
        // credential: ReauthenticationBehavior verifies it server-side BEFORE the handler (a hijacked
        // long-lived session can't change the password without it); a wrong current password throws
        // ReauthenticationFailedException -> byte-identical 401 (Program.cs). A weak new password is
        // a 400 (validator) before UserManager runs. On success the endpoint owns C6 (below) and
        // returns the re-issued { sessionId, persistent } like /login (ADR 0018 — backend sets no
        // cookies; the Next layer re-sets the __Host- cookie). AuthWrite rate-limit — same
        // credential-risk profile as /login and /verify.
        group.MapPost("/change-password", async (
            ChangePasswordRequest body,
            IMediator mediator,
            ISessionStore sessions,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new ChangePasswordCommand(body.CurrentPassword, body.NewPassword), ct);
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

        // Self-service change-email — REQUEST step (#679, epik #481). Re-auth-gated like
        // change-password: the CURRENT password is verified server-side by ReauthenticationBehavior
        // (wrong -> byte-identical 401) BEFORE the handler. A taken address is a 409 (clear "adressen
        // är upptagen"), a malformed address a 400. On success the handler emails an ownership-
        // confirmation link to the NEW address and returns 202 Accepted — the email is NOT changed and
        // NO session is touched until the link is confirmed (see /confirm-email-change). AuthWrite
        // rate-limit — same credential-risk profile as /login and /change-password.
        group.MapPost("/change-email", async (
            ChangeEmailRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new ChangeEmailCommand(body.CurrentPassword, body.NewEmail), ct);
            return result.IsFailure
                ? ToErrorResult(result.Error)
                : Results.Accepted();
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

        // Self-service change-email — CONFIRM step (#679). PUBLIC (no RequireAuthorization): the link
        // is opened from the NEW inbox, possibly logged-out or on a different device, so the opaque
        // single-use token IS the authorization. Every rejection is a uniform 400 (no account/enum
        // oracle). On success the email is swapped and the endpoint enacts C6 — logout-everywhere —
        // because a recovery-vector change must invalidate all sessions (the Redis store is
        // independent of Identity's SecurityStamp, so stamp rotation does not touch it). AuthWrite
        // rate-limit (per-IP) against generic abuse of the public endpoint; the opaque token is not
        // brute-forceable, so no per-uid limiter is needed (CTO-bind #3).
        group.MapPost("/confirm-email-change", async (
            ConfirmEmailChangeRequest body,
            IMediator mediator,
            ISessionStore sessions,
            ILogger<ConfirmEmailChangeCommand> logger,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new ConfirmEmailChangeCommand(body.Uid, body.Email, body.Token), ct);
            if (result.IsFailure)
                return ToErrorResult(result.Error);

            var userId = result.Value;

            // C6 — the email (an account-recovery vector) just changed: log out EVERY session so the
            // account is re-authenticated with the new address. NO re-issue (the confirming client is
            // not necessarily the user's session). CancellationToken.None: the change is committed; a
            // disconnect must not leave sessions alive. Best-effort + logged as a security event — a
            // Redis blip must not fail a completed change, but live-session residue must be detectable
            // (CTO risk 3).
            try
            {
                await sessions.InvalidateAllForUserAsync(userId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                LogSessionInvalidationFailed(logger, ex, userId);
            }

            return Results.NoContent();
        }).RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy);

        // Registration email-confirmation — CONFIRM step (#714). PUBLIC (no RequireAuthorization): the
        // activation link is opened from the account's own inbox, possibly logged-out or on a different
        // device, so the opaque token IS the authorization. Every rejection is a uniform 400 (no
        // account/enumeration oracle — parity with /confirm-email-change). On success EmailConfirmed is
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

        // Password reset — APPLY step (#1171). PUBLIC: the link is opened from the account's own inbox,
        // logged out by definition, so the opaque single-use token IS the authorization. Every TOKEN
        // rejection is a uniform 400; a PASSWORD rejection names its rule, which is safe because Identity
        // verifies the token BEFORE running the password validators — that arm is reachable only by
        // someone already holding a valid token. In practice that is Auth.PwnedPassword alone:
        // ResetPasswordCommandValidator carries Identity's own 12-character floor, so ValidationBehavior
        // fells a short password first and answers with the {errors} shape instead — Auth.PasswordTooShort
        // is never emitted on this route.
        //
        // On success the endpoint enacts C6 (logout-everywhere) with NO re-issue, following
        // /confirm-email-change rather than /change-password: the actor here is anonymous and the link may
        // be opened on any device, so minting a session for whoever opened it would turn recovery into
        // login. 204, and the user logs in with the new password.
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
    /// POST /auth/change-password body — the current password (server-side re-auth via
    /// ReauthenticationBehavior) and the new password (strength-validated by
    /// ChangePasswordCommandValidator). A pure transport DTO; neither value is logged.
    /// </summary>
    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    /// <summary>
    /// POST /auth/change-email body — the current password (server-side re-auth via
    /// ReauthenticationBehavior) and the new email address (uniqueness pre-checked; ownership
    /// confirmed via an emailed link before the swap). A pure transport DTO; neither value is logged.
    /// </summary>
    public sealed record ChangeEmailRequest(string? CurrentPassword, string? NewEmail);

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
    /// POST /auth/confirm-email-change body — the (uid, new email, URL-safe token) carried by the
    /// confirmation link and posted from the public landing page. Token-gated (the link is opened from
    /// the new inbox, possibly logged-out): the token is the authorization. A pure transport DTO; the
    /// token is never logged.
    /// </summary>
    public sealed record ConfirmEmailChangeRequest(Guid Uid, string? Email, string? Token);

    /// <summary>
    /// POST /auth/verify-email body — the (uid, URL-safe token) carried by the registration activation
    /// link and posted from the public landing page. Token-gated (the link is opened from the account's
    /// inbox, possibly logged-out): the token is the authorization. No email is needed (the address is
    /// not changing, unlike /confirm-email-change). A pure transport DTO; the token is never logged.
    /// </summary>
    public sealed record VerifyEmailRequest(Guid Uid, string? Token);

    /// <summary>
    /// POST /auth/resend-confirmation body — the email address to re-send a registration confirmation
    /// link to (#733). A pure transport DTO; the address is never logged. The response is a uniform 202
    /// regardless of whether the address has an unconfirmed account (anti-enumeration).
    /// </summary>
    public sealed record ResendConfirmationRequest(string? Email);

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
        // mapper (title=code, detail=message). The re-auth path normalizes EmailNotConfirmed back to
        // InvalidCredentials, so this arm is reachable only via /login.
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
        // (clients and caches honour it). Only POST /auth/register can return this — the health
        // endpoints are untouched, so uptime monitoring is unaffected.
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
        // TWO producers since #1171, and the second is PUBLIC — the earlier note that this was
        // reachable only from the authenticated /auth/change-email no longer holds, so the reason it
        // discloses nothing about any address is different for each:
        //   · POST /auth/change-email — authenticated and re-authenticated, so the caller already
        //     owns the account and learns nothing new.
        //   · POST /auth/forgot-password — unauthenticated, and safe instead by ORDER: the handler's
        //     capability check is its first statement and reads no input, so this 503 is decided
        //     before the submitted address is looked at and cannot vary with it. Move that check
        //     after the account lookup and this arm becomes an enumeration oracle.
        AuthErrorCodes.EmailDeliveryUnavailable => Results.Problem(
            detail: AuthErrorCodes.EmailDeliveryUnavailableMessage,
            title: AuthErrorCodes.EmailDeliveryUnavailable,
            statusCode: StatusCodes.Status503ServiceUnavailable),

        _ => error.ToProblemResult(),
    };

    // C6 session-invalidation is best-effort: a completed email change must not be failed by a Redis
    // blip, but live-session residue must be detectable (CTO risk 3). Source-gen per CA1848; no
    // recipient/PII, only the userId surrogate.
    // Keeps the full exception (a Redis fault's stack aids ops; it carries no user PII), unlike the
    // email-send logs which log only the type per §5. Explicit EventId for parity with the sibling
    // change-email log ids (4001/4002).
    [LoggerMessage(4003, LogLevel.Error,
        "Change-email confirm: session invalidation FAILED for user {UserId} — " +
        "email changed, sessions may still be live")]
    private static partial void LogSessionInvalidationFailed(ILogger logger, Exception ex, Guid userId);

    // #1171 — the same shape as 4003 above and for the same reason (a Redis fault's stack aids ops and
    // carries no user PII). Its own EventId because the consequence differs: after a RESET, live-session
    // residue means an account that may have just been taken over still has the attacker's sessions.
    [LoggerMessage(4004, LogLevel.Error,
        "Password reset: session invalidation FAILED for user {UserId} — " +
        "password changed, sessions may still be live")]
    private static partial void LogResetSessionInvalidationFailed(ILogger logger, Exception ex, Guid userId);
}
