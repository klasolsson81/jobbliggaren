using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Auth.Registration;

/// <summary>
/// Creates the Identity account of a proven new address, and takes it back if its profile cannot be made
/// (ADR 0142 D3/D10). A port of its own, never <c>IUserAccountService</c>.
/// </summary>
public interface IPasswordlessAccountCreator
{
    /// <summary>
    /// A user with a confirmed address and no password. A duplicate — by the address or by the user name,
    /// which is the address — collapses to <c>Auth.DuplicateAccount</c>.
    /// </summary>
    Task<Result<Guid>> CreatePasswordlessUserAsync(string email, CancellationToken ct);

    /// <summary>Compensates a create whose profile did not commit.</summary>
    Task DeleteAsync(Guid userId, CancellationToken ct);
}
