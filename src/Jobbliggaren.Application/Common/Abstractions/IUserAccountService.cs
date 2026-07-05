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
}
