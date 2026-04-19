using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using JobbPilot.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace JobbPilot.Infrastructure.Auth;

public sealed class UserAccountService(
    UserManager<ApplicationUser> userManager)
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
            return Result.Failure<Guid>(
                DomainError.Validation($"Auth.{error.Code}", error.Description));
        }

        return Result.Success(user.Id);
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
            return Result.Failure<UserCredentials>(
                DomainError.Validation("Auth.InvalidCredentials", "E-post eller lösenord är felaktigt."));

        var valid = await userManager.CheckPasswordAsync(user, password);
        if (!valid)
            return Result.Failure<UserCredentials>(
                DomainError.Validation("Auth.InvalidCredentials", "E-post eller lösenord är felaktigt."));

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
}
