using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Jobbliggaren.Infrastructure.Auth;

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
                DomainError.Validation(AuthErrorCodes.InvalidCredentials, "E-post eller lösenord är felaktigt."));

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
                DomainError.Validation(AuthErrorCodes.AccountLocked, "E-post eller lösenord är felaktigt."));

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            // Count the failed attempt. AccessFailedAsync auto-sets LockoutEnd once
            // MaxFailedAccessAttempts is reached (opts.Lockout, DependencyInjection).
            await userManager.AccessFailedAsync(user);
            return Result.Failure<UserCredentials>(
                DomainError.Validation(AuthErrorCodes.InvalidCredentials, "E-post eller lösenord är felaktigt."));
        }

        // A successful verify resets the counter (only when >0 to avoid a needless write).
        if (user.AccessFailedCount > 0)
            await userManager.ResetAccessFailedCountAsync(user);

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
}
