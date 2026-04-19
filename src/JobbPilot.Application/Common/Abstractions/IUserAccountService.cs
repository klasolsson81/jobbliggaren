using JobbPilot.Domain.Common;

namespace JobbPilot.Application.Common.Abstractions;

public sealed record UserCredentials(Guid UserId, IReadOnlyList<string> Roles);

public interface IUserAccountService
{
    Task<Result<Guid>> CreateUserAsync(string email, string password, CancellationToken ct);
    Task DeleteUserAsync(Guid userId, CancellationToken ct);
    Task<Result<UserCredentials>> ValidateCredentialsAsync(string email, string password, CancellationToken ct);
    Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct);
}
