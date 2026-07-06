using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.ChangeEmail;
using Jobbliggaren.Application.Auth.Commands.ChangePassword;
using Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;
using Jobbliggaren.Application.Auth.Commands.Login;
using Jobbliggaren.Application.Auth.Commands.Logout;
using Jobbliggaren.Application.Auth.Commands.RefreshSession;
using Jobbliggaren.Application.Auth.Commands.Register;
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
    /// POST /auth/confirm-email-change body — the (uid, new email, URL-safe token) carried by the
    /// confirmation link and posted from the public landing page. Token-gated (the link is opened from
    /// the new inbox, possibly logged-out): the token is the authorization. A pure transport DTO; the
    /// token is never logged.
    /// </summary>
    public sealed record ConfirmEmailChangeRequest(Guid Uid, string? Email, string? Token);

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
}
