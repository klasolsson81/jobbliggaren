using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Common.Abstractions;

public sealed record UserCredentials(Guid UserId, IReadOnlyList<string> Roles);

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
}
