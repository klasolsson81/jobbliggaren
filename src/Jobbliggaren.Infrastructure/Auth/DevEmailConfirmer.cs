using Jobbliggaren.Application.Dev.Abstractions;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). Implements the token-free confirmed-login
/// seam over <see cref="UserManager{TUser}"/>. Registered in DI ONLY in Development
/// (<see cref="DependencyInjection.AddDevOnlyTestingSupport"/>) — the structural gate
/// that keeps this primitive out of the container in every deployed environment.
///
/// <para>
/// Deliberately does NOT go through <c>UserAccountService</c> / a real confirmation
/// token: that production surface seals email-existence knowledge on purpose
/// (<c>TryPrepareEmailConfirmationResendAsync</c>). This seam is the opposite — a
/// blunt, dev-only force-confirm — so it stays isolated behind its own port.
/// </para>
/// </summary>
public sealed class DevEmailConfirmer(UserManager<ApplicationUser> userManager) : IDevEmailConfirmer
{
    public async Task<DevEmailConfirmOutcome> ForceConfirmByEmailAsync(
        string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return DevEmailConfirmOutcome.NotFound;

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }

        return DevEmailConfirmOutcome.Confirmed;
    }
}
