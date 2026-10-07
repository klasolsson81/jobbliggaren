using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Auth.Registration;

/// <summary>
/// Creates the Identity account inside the caller's account-access transaction (ADR 0142 D3/D10).
/// </summary>
public interface IPasswordlessAccountCreator
{
    /// <summary>
    /// A user with a confirmed address and no password. A duplicate — by the address or by the user name,
    /// which is the address — collapses to <c>Auth.DuplicateAccount</c>.
    /// </summary>
    Task<Result<Guid>> CreatePasswordlessUserAsync(Guid userId, string email, CancellationToken ct);
}
