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

        // #503 (OWASP A07, senior-cto-advisor G1): hedra Identitys lockout FÖRE
        // hash-verifiering. Ett låst konto avvisas utan att bränna en lösenords-
        // jämförelse och utan att räkna upp vidare. Distinkt intern kod (AccountLocked)
        // så Api-handlern kan emit:a account_locked_out-audit — wire-svaret normaliseras
        // dock till byte-identiskt InvalidCredentials (AuthEndpoints.ToErrorResult) så
        // lockout-tillståndet inte läcker som konto-enumererings- eller DoS-orakel.
        // Kräver LockoutEnabled=true på raden, vilket UserManager.CreateAsync stämplar
        // från opts.Lockout.AllowedForNewUsers (DependencyInjection).
        if (await userManager.IsLockedOutAsync(user))
            return Result.Failure<UserCredentials>(
                DomainError.Validation(AuthErrorCodes.AccountLocked, "E-post eller lösenord är felaktigt."));

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            // Räkna det misslyckade försöket. AccessFailedAsync auto-sätter LockoutEnd
            // när MaxFailedAccessAttempts nås (opts.Lockout, DependencyInjection).
            await userManager.AccessFailedAsync(user);
            return Result.Failure<UserCredentials>(
                DomainError.Validation(AuthErrorCodes.InvalidCredentials, "E-post eller lösenord är felaktigt."));
        }

        // Lyckad verifiering nollar räknaren (bara vid >0 → undvik onödig skrivning).
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
