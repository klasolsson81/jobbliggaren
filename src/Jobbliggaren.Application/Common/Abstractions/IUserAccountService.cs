using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Common.Abstractions;

public sealed record UserCredentials(Guid UserId, IReadOnlyList<string> Roles);

/// <summary>
/// The material a confirmation-link RESEND needs (#733): the account's userId + address plus a freshly
/// minted opaque Base64Url confirmation token. Produced by
/// <see cref="IUserAccountService.TryPrepareEmailConfirmationResendAsync"/> ONLY when
/// email-confirmation-first is enabled AND a still-unconfirmed account exists at the address; the token is
/// minted and validated in the SAME Api process (one Data-Protection keyring) so the emailed link resolves
/// at /verify-email.
/// </summary>
public sealed record EmailConfirmationResend(Guid UserId, string Email, string UrlSafeToken);

public interface IUserAccountService
{
    Task<Result<Guid>> CreateUserAsync(string email, string password, CancellationToken ct);

    /// <summary>
    /// Changes the user's password via Identity (verifies the current password, sets the new one,
    /// and re-stamps the security stamp). The current password is re-verified here even though
    /// <c>ReauthenticationBehavior</c> already checked it — defense-in-depth and the atomic Identity
    /// primitive. Maps the first <c>IdentityError</c> to a <c>DomainError</c> the same way as
    /// <see cref="CreateUserAsync"/> (e.g. <c>Auth.PasswordTooShort</c>, <c>Auth.PasswordMismatch</c>).
    /// </summary>
    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct);

    Task DeleteUserAsync(Guid userId, CancellationToken ct);
    Task<Result<UserCredentials>> ValidateCredentialsAsync(string email, string password, CancellationToken ct);
    Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct);
    Task<string?> GetEmailAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// True if some account already owns <paramref name="email"/> (RequireUniqueEmail). Used by the
    /// change-email request step (#679) for a friendly "address is taken" (409) BEFORE issuing a
    /// token; uniqueness is still enforced authoritatively at <see cref="ConfirmChangeEmailAsync"/>.
    /// </summary>
    Task<bool> IsEmailTakenAsync(string email, CancellationToken ct);

    /// <summary>
    /// Generates a URL-safe change-email ownership-confirmation token bound to the user + the NEW
    /// address (#679). Uses the opaque DataProtector provider (CTO-bind #1); the token encodes the
    /// pending new email, nothing is persisted (the pending state lives in the emailed link), and the
    /// email is NOT changed here. Base64Url-encoded so it survives a URL/query round-trip. Returns
    /// NotFound if the user is gone.
    /// </summary>
    Task<Result<string>> GenerateChangeEmailTokenAsync(Guid userId, string newEmail, CancellationToken ct);

    /// <summary>
    /// Applies a pending email change (#679): verifies the URL-safe token against the user + NEW
    /// address, sets Email/NormalizedEmail (+ EmailConfirmed) and keeps UserName in lockstep with the
    /// email (registration couples them), rotating the security stamp so the token is single-use.
    /// Returns ONE uniform failure for every rejection (user-not-found, bad/expired/malformed token,
    /// address-taken) so the PUBLIC confirm endpoint reveals no account-existence or enumeration oracle.
    /// </summary>
    Task<Result> ConfirmChangeEmailAsync(Guid userId, string newEmail, string urlSafeToken, CancellationToken ct);

    /// <summary>
    /// Generates a URL-safe email-confirmation token for the user's CURRENT address (#714, registration
    /// confirmation). Uses the opaque DataProtector provider (<c>EmailConfirmationTokenProvider</c>,
    /// pinned in DI); the token is bound to the security stamp, time-limited (24h default) and
    /// Base64Url-encoded so it survives a URL/query round-trip. Unlike the change-email token there is
    /// no pending new address. Returns NotFound if the user is gone.
    /// </summary>
    Task<Result<string>> GenerateEmailConfirmationTokenAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Confirms a registration email address (#714): verifies the URL-safe token and sets
    /// <c>EmailConfirmed=true</c>. Returns ONE uniform failure for every rejection (user-not-found,
    /// bad/expired/malformed token) so the PUBLIC confirm endpoint reveals no account-existence or
    /// enumeration oracle. Idempotent within the token lifespan: a double-click both succeed (unlike
    /// <see cref="ConfirmChangeEmailAsync"/>, the security stamp is NOT rotated — an activation link
    /// need not be single-use, and idempotency is the safer click-through UX).
    /// </summary>
    Task<Result> ConfirmEmailAsync(Guid userId, string urlSafeToken, CancellationToken ct);

    /// <summary>
    /// #733 — eligibility + token mint for a confirmation-link RESEND, sealed in Infrastructure. Returns
    /// the delivery material (userId + address + a freshly minted opaque Base64Url token) ONLY when
    /// email-confirmation-first is ENABLED (<see cref="Auth.AuthOptions.RequireEmailConfirmation"/>) AND an
    /// account exists at <paramref name="email"/> that is still unconfirmed; <c>null</c> otherwise
    /// (flag-OFF / non-existent / already-confirmed — all indistinguishable to the caller). The flag-gate
    /// is FIRST (constant-time, before any DB lookup) so flag-OFF is a uniform no-op that never mails a
    /// user whose instant-login works — symmetric with the login gate, preserving #714's prod-safe default
    /// OFF. Sealing the "does an unconfirmed account exist here" knowledge here keeps the uniform
    /// anti-enumeration response in the handler and prevents a future handler turning a bare existence
    /// primitive into an oracle. The token is minted Api-side, in the same Data-Protection keyring that
    /// validates it at /verify-email (CTO 2026-07-10 / ADR 0102 — no cross-process token).
    /// </summary>
    Task<EmailConfirmationResend?> TryPrepareEmailConfirmationResendAsync(string email, CancellationToken ct);
}
