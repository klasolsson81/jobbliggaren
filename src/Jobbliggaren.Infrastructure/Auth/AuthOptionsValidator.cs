using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// Startup validation hung on <see cref="AuthOptions"/> (ADR 0083 Amendment 2026-08-03, ADR 0142 D10). Refuses
/// to boot outside Development and Test when the registered <see cref="IEmailSender"/> cannot deliver, whatever
/// <see cref="AuthOptions.RegistrationsOpen"/> says: every login is a mailed code or link, so a sender that drops
/// mail locks every account out, and an open registration would mint accounts nobody can log in to
/// (security-auditor Major 12, #1735).
/// <para>
/// The exemption is an ALLOWLIST (Development, Test), never <c>!IsProduction()</c> — a denylist would
/// exempt Staging and every unrecognised environment name silently.
/// </para>
/// <para>
/// <b>It asks the sender, never the configuration key.</b> <see cref="IEmailSender.CanDeliver"/> is the
/// capability member #1087 added so a delivery-dependent consumer can refuse up front; a provider added later
/// is classified by its own answer, with nothing here to keep in sync. Reading <c>Email:Provider</c> instead
/// would re-enumerate the switch in <c>DependencyInjection.AddEmailSender</c>.
/// </para>
/// <para>
/// <b>Why the dependency resolves here and why the Worker is untouched.</b> This validator is registered in
/// <c>AddIdentityAndSessions</c>, which every HOST composition reaches together with <c>AddEmailSender</c>, so
/// wherever this type resolves, an <see cref="IEmailSender"/> does. <c>ProductionStartupSmokeTests</c> boots a
/// real Production host, so the construction is pinned rather than argued. The Worker calls
/// <c>AddEmailSender</c> too but registers no validator: it owns no login surface, so a shared env file must
/// not take it down for a condition it cannot exercise.
/// </para>
/// </summary>
internal sealed class AuthOptionsValidator(IHostEnvironment environment, IEmailSender emailSender)
    : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Test"))
        {
            return ValidateOptionsResult.Success;
        }

        if (!emailSender.CanDeliver)
        {
            return ValidateOptionsResult.Fail(
                "Utanför Development/Test krävs en Email:Provider som faktiskt levererar (aktuell miljö: "
                + $"{environment.EnvironmentName}; registrerad avsändare: "
                + $"{emailSender.GetType().Name}). Sätt Email__Provider=Scaleway med "
                + "Email__Scaleway-nycklarna enligt deploy/.env.example (GDPR-grinden: "
                + "release-checklist.md §2.5).");
        }

        return ValidateOptionsResult.Success;
    }
}
