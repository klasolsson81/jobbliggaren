using System.Buffers.Text;
using System.Text;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth;

public sealed partial class UserAccountService(
    UserManager<ApplicationUser> userManager,
    ILoginTimingEqualizer loginTimingEqualizer,
    IOptions<AuthOptions> authOptions,
    ILogger<UserAccountService> logger)
    : IUserAccountService
{
    public async Task<Result<Guid>> CreateUserAsync(
        string email, string password, CancellationToken ct)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var error = result.Errors.First();

            // #481 Low — do not leak account existence on registration. Identity's duplicate errors
            // carry a raw English message that echoes the submitted address ("Username 'x@y.z' is
            // already taken"); collapse them to a generic localized message that names neither the
            // field nor the address. The 200-vs-400 status oracle inherent to instant-login
            // registration is deferred (closing it needs email-confirmation-first registration).
            // Other Identity codes stay specific: they are genuinely actionable (e.g. InvalidEmail) and
            // the register validator already caught password/format, so the duplicate is the only
            // enumeration-relevant failure. CTO-bind #2.
            if (IsDuplicateAccountError(error.Code))
                return Result.Failure<Guid>(
                    DomainError.Validation(AuthErrorCodes.DuplicateAccount, AuthErrorCodes.DuplicateAccountMessage));

            return Result.Failure<Guid>(
                DomainError.Validation($"Auth.{error.Code}", error.Description));
        }

        return Result.Success(user.Id);
    }

    public async Task<Result> ChangePasswordAsync(
        Guid userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Result.Failure(
                DomainError.NotFound("Auth.UserNotFound", "Användaren hittades inte."));

        // ChangePasswordAsync verifies the current password, sets the new one, and rotates the
        // security stamp — atomically. Enforces the registered password policy (RequiredLength = 12
        // -> PasswordTooShort). Map the first error the same way as CreateUserAsync so the central
        // DomainError.ToProblemResult mapping resolves the status (Validation -> 400).
        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            var error = result.Errors.First();
            return Result.Failure(
                DomainError.Validation($"Auth.{error.Code}", error.Description));
        }

        return Result.Success();
    }

    public async Task DeleteUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is not null)
            await userManager.DeleteAsync(user);
    }

    public async Task<Result<UserCredentials>> ValidateCredentialsAsync(
        string email, string password, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // Constant-time defense (#481 Low): an unknown email would otherwise short-circuit here
            // before any hash comparison, while a known email with a wrong password pays a full PBKDF2
            // derivation below — the latency delta enumerates registered accounts. Pay the equivalent
            // cost so response timing reveals nothing. Not-found branch ONLY: the lockout branch stays
            // cheap by design (#503 anti-DoS, CTO-bind #1) and its residual timing channel does not aid
            // enumeration (a one-attempt-per-email probe never locks an account).
            loginTimingEqualizer.Equalize(password);
            return Result.Failure<UserCredentials>(
                DomainError.Validation(AuthErrorCodes.InvalidCredentials, AuthErrorCodes.InvalidCredentialsMessage));
        }

        // #503 (OWASP A07, senior-cto-advisor G1): honor Identity's lockout BEFORE the
        // hash check. A locked account is rejected without burning a password comparison
        // and without incrementing further. A distinct internal code (AccountLocked) lets
        // the Api handler emit an account_locked_out audit — the wire response is however
        // normalized to a byte-identical InvalidCredentials (AuthEndpoints.ToErrorResult)
        // so lockout state does not leak as an account-enumeration or DoS-target oracle.
        // Requires LockoutEnabled=true on the row, which UserManager.CreateAsync stamps
        // from opts.Lockout.AllowedForNewUsers (DependencyInjection).
        if (await userManager.IsLockedOutAsync(user))
            return Result.Failure<UserCredentials>(
                DomainError.Validation(AuthErrorCodes.AccountLocked, AuthErrorCodes.InvalidCredentialsMessage));

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            // Count the failed attempt. AccessFailedAsync auto-sets LockoutEnd once
            // MaxFailedAccessAttempts is reached (opts.Lockout, DependencyInjection).
            await userManager.AccessFailedAsync(user);
            return Result.Failure<UserCredentials>(
                DomainError.Validation(AuthErrorCodes.InvalidCredentials, AuthErrorCodes.InvalidCredentialsMessage));
        }

        // A successful verify resets the counter (only when >0 to avoid a needless write).
        if (user.AccessFailedCount > 0)
            await userManager.ResetAccessFailedCountAsync(user);

        // #714 — email-confirmation-first login gate. Placed AFTER the successful password check, so
        // it is reachable ONLY with a correct password and is therefore NOT an account-enumeration
        // oracle: an unknown email / wrong password still returns the byte-identical InvalidCredentials
        // 401 above. A distinct code lets the Api render an actionable 403 ("confirm your email");
        // ReauthenticationService normalizes it back to InvalidCredentials so the re-auth surface stays
        // a uniform 401. Inert when the flag is OFF (legacy instant-login). No AccessFailed increment:
        // the credentials were valid, so this is not a failed login attempt.
        if (authOptions.Value.RequireEmailConfirmation && !user.EmailConfirmed)
            return Result.Failure<UserCredentials>(
                DomainError.Validation(AuthErrorCodes.EmailNotConfirmed, AuthErrorCodes.EmailNotConfirmedMessage));

        var roles = await userManager.GetRolesAsync(user);
        return Result.Success(new UserCredentials(user.Id, roles.ToList()));
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return [];

        var roles = await userManager.GetRolesAsync(user);
        return roles.ToList();
    }

    public async Task<string?> GetEmailAsync(Guid userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user?.Email;
    }

    public async Task<bool> IsEmailTakenAsync(string email, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(email);
        return user is not null;
    }

    public async Task<Result<string>> GenerateChangeEmailTokenAsync(
        Guid userId, string newEmail, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Result.Failure<string>(
                DomainError.NotFound("Auth.UserNotFound", "Användaren hittades inte."));

        // Opaque DataProtector token (CTO-bind #1) bound to (SecurityStamp, "ChangeEmail:{newEmail}").
        // Nothing is persisted; the pending new email lives inside the token. The email is NOT changed.
        var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);

        // Base64Url so the token (base64 with +,/,=) survives the email link -> query string -> POST
        // round-trip without a layer turning '+' into a space (CTO-bind #2 mitigation). Decoded 1:1
        // in ConfirmChangeEmailAsync.
        var urlSafeToken = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token));
        return Result.Success(urlSafeToken);
    }

    public async Task<Result> ConfirmChangeEmailAsync(
        Guid userId, string newEmail, string urlSafeToken, CancellationToken ct)
    {
        // Uniform failure for EVERY rejection below (user-not-found, malformed/bad/expired token,
        // address-taken-at-confirm): a PUBLIC confirm endpoint must not distinguish them, or it
        // becomes an account-existence / email-enumeration oracle (parity AuthProblem's byte-identical
        // 401). Callers surface DomainError.Validation -> 400.
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return InvalidTokenFailure();

        string token;
        try
        {
            token = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(urlSafeToken));
        }
        catch (FormatException)
        {
            // A malformed (non-Base64Url) token is just an invalid token — same uniform failure.
            return InvalidTokenFailure();
        }

        // ChangeEmailAsync verifies the token against (SecurityStamp, "ChangeEmail:{newEmail}"), sets
        // Email + NormalizedEmail + EmailConfirmed=true, and rotates the security stamp (single-use:
        // the token and any sibling pending token die). Re-runs the RequireUniqueEmail validator, so a
        // taken address fails here — the TOCTOU backstop for the request-time pre-check.
        var changeResult = await userManager.ChangeEmailAsync(user, newEmail, token);
        if (!changeResult.Succeeded)
            return InvalidTokenFailure();

        // Risk 1 (CTO-bind): ChangeEmailAsync updates Email/NormalizedEmail but NOT UserName.
        // Registration sets UserName == email (CreateUserAsync) and login resolves via
        // FindByEmailAsync, so keep UserName in lockstep to avoid stale PII (the old address lingering
        // in UserName / NormalizedUserName) + a latent divergence. Can't-fail in practice (newEmail is
        // now this user's unique email and every UserName mirrors a unique Email); if it ever lags we
        // do NOT fail the completed change — the recovery vector already moved — we log and continue.
        var userNameResult = await userManager.SetUserNameAsync(user, newEmail);
        if (!userNameResult.Succeeded)
            LogUserNameSyncLagged(userId);

        return Result.Success();
    }

    public async Task<Result<string>> GenerateEmailConfirmationTokenAsync(
        Guid userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Result.Failure<string>(
                DomainError.NotFound("Auth.UserNotFound", "Användaren hittades inte."));

        // #714 — opaque DataProtector token (EmailConfirmationTokenProvider, pinned in DI) bound to the
        // security stamp + the "EmailConfirmation" purpose. Nothing is persisted; the token IS the
        // pending activation state. No pending new address (contrast the change-email token).
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);

        // Base64Url so the token survives the email link -> query string -> POST round-trip (parity
        // with the change-email token, #679). Decoded 1:1 in ConfirmEmailAsync.
        var urlSafeToken = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token));
        return Result.Success(urlSafeToken);
    }

    public async Task<Result> ConfirmEmailAsync(
        Guid userId, string urlSafeToken, CancellationToken ct)
    {
        // Uniform failure for EVERY rejection below (user-not-found, malformed/bad/expired token): a
        // PUBLIC confirm endpoint must not distinguish them, or it becomes an account-existence /
        // enumeration oracle (parity with ConfirmChangeEmailAsync + AuthProblem's byte-identical 401).
        // Callers surface DomainError.Validation -> 400.
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return InvalidConfirmationTokenFailure();

        string token;
        try
        {
            token = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(urlSafeToken));
        }
        catch (FormatException)
        {
            // A malformed (non-Base64Url) token is just an invalid token — same uniform failure.
            return InvalidConfirmationTokenFailure();
        }

        // ConfirmEmailAsync verifies the token against (SecurityStamp, "EmailConfirmation") and sets
        // EmailConfirmed=true. It does NOT rotate the security stamp, so a double-click within the 24h
        // lifespan is idempotent (both succeed) — the safer click-through UX for an activation link
        // (contrast ChangeEmailAsync, which rotates the stamp because a recovery-vector change must be
        // single-use). No UserName lockstep: the address is unchanged.
        var confirmResult = await userManager.ConfirmEmailAsync(user, token);
        if (!confirmResult.Succeeded)
            return InvalidConfirmationTokenFailure();

        return Result.Success();
    }

    // Identity IdentityErrorDescriber codes (== the describer method names) for a taken username /
    // email. With UserName == Email + RequireUniqueEmail, a duplicate register trips both (#481 Low).
    private const string IdentityDuplicateUserNameCode = "DuplicateUserName";
    private const string IdentityDuplicateEmailCode = "DuplicateEmail";

    private static bool IsDuplicateAccountError(string code) =>
        code == IdentityDuplicateUserNameCode || code == IdentityDuplicateEmailCode;

    private static Result InvalidTokenFailure() =>
        Result.Failure(DomainError.Validation(
            "Auth.InvalidEmailChangeToken",
            "Bekräftelselänken är ogiltig eller har gått ut. Begär en ny ändring av e-postadressen."));

    private static Result InvalidConfirmationTokenFailure() =>
        Result.Failure(DomainError.Validation(
            AuthErrorCodes.InvalidEmailConfirmationToken,
            AuthErrorCodes.InvalidEmailConfirmationTokenMessage));

    [LoggerMessage(4001, LogLevel.Warning,
        "[UserAccountService] Change-email: UserName sync lagged Email for user {UserId} " +
        "(username kept stale, email change succeeded)")]
    private partial void LogUserNameSyncLagged(Guid userId);
}
