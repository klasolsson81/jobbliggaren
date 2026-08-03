using Jobbliggaren.Application.Auth;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// Startup validation for <see cref="AuthOptions"/> (ADR 0083 Amendment 2026-08-03,
/// senior-cto-advisor bind 2026-08-03). Refuses to boot on ONE combination:
/// <c>RegistrationsOpen</c> without <c>RequireEmailConfirmation</c>, outside Development and Test.
/// <para>
/// Why a control rather than a comment: that combination is legacy instant-login — an account minted
/// with no proof the registrant owns the address, and the acknowledged-deferred 200-vs-400 duplicate
/// enumeration oracle live on a public IP. It is the posture <b>#734</b> exists to prevent. This
/// change exists because a flag's effective state was documented but unenforced; repairing that with
/// more documentation would reproduce its mechanism.
/// </para>
/// <para>
/// It fires in ONE direction only and does not weaken the fail-safe default: an absent
/// <c>Auth</c> section binds both flags to <c>false</c> and boots clean. Only opening the gate can
/// trip it.
/// </para>
/// <para>
/// The exemption is an ALLOWLIST (Development, Test), never <c>!IsProduction()</c> — a denylist would
/// exempt Staging and every unrecognised environment name silently, which is the class of silence this
/// change repairs. It reuses the house's established exemption predicate rather than inventing a
/// third. Measured: the integration harness forces Development, so the 142 instant-login bootstrap
/// sites are exempt by that clause and not by accident.
/// </para>
/// </summary>
internal sealed class AuthOptionsValidator(IHostEnvironment environment) : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Test"))
        {
            return ValidateOptionsResult.Success;
        }

        if (options.RegistrationsOpen && !options.RequireEmailConfirmation)
        {
            return ValidateOptionsResult.Fail(
                $"Auth:RegistrationsOpen=true kräver Auth:RequireEmailConfirmation=true utanför "
                + $"Development/Test (aktuell miljö: {environment.EnvironmentName}). Öppen "
                + "registrering utan e-postbekräftelse skapar konton bundna till adresser "
                + "registranten inte bevisligen äger, och exponerar duplikat-oraklet på en publik "
                + "IP. Sätt Auth__RequireEmailConfirmation=true OCH en riktig Email:Provider "
                + "(förutsättningarna ägs av #734), eller lämna registreringen stängd.");
        }

        return ValidateOptionsResult.Success;
    }
}
