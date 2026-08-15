using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// Startup validation for <see cref="AuthOptions"/> (ADR 0083 Amendment 2026-08-03,
/// senior-cto-advisor bind 2026-08-03). Refuses to boot on TWO combinations, both of which require
/// <c>RegistrationsOpen</c> and neither of which fires inside Development or Test:
/// <list type="number">
/// <item><c>RegistrationsOpen</c> WITHOUT <c>RequireEmailConfirmation</c> — legacy instant-login: an
/// account minted with no proof the registrant owns the address, and the acknowledged-deferred
/// 200-vs-400 duplicate-enumeration oracle live on a public IP.</item>
/// <item><c>RegistrationsOpen</c> WITH <c>RequireEmailConfirmation</c> but a registered
/// <see cref="IEmailSender"/> that cannot deliver — the account is created, login is blocked on
/// <c>EmailConfirmed</c>, and the activation link reaches nobody. The account is <b>permanently
/// unreachable</b>, and <c>ResendEmailConfirmationCommandHandler</c> is equally silent (it must keep
/// returning a uniform 202 for anti-enumeration reasons, so it cannot report the failure at all).
/// Added 2026-08-09 as senior-cto-advisor's D1, the composition-time boot refusal
/// <c>NullEmailSender</c>'s own contract names as its owner.</item>
/// </list>
/// <para>
/// Why controls rather than comments: both combinations were documented and unenforced, and
/// repairing that with more documentation would reproduce its mechanism.
/// </para>
/// <para>
/// Both fire in ONE direction only and neither weakens the fail-safe default: an absent
/// <c>Auth</c> section binds both flags to <c>false</c> and boots clean whatever the sender answers.
/// Only opening the gate can trip either.
/// </para>
/// <para>
/// The exemption is an ALLOWLIST (Development, Test), never <c>!IsProduction()</c> — a denylist would
/// exempt Staging and every unrecognised environment name silently, which is the class of silence this
/// change repairs. It reuses the house's established exemption predicate rather than inventing a
/// third. Measured: the integration harness forces Development, so the 142 instant-login bootstrap
/// sites are exempt by that clause and not by accident.
/// </para>
/// <para>
/// <b>Rule 2 asks the sender, never the configuration key.</b> <see cref="IEmailSender.CanDeliver"/>
/// is the capability member #1087 added precisely so a delivery-dependent consumer can refuse up
/// front; a provider added later is classified by its own answer, with nothing here to keep in sync.
/// Reading <c>Email:Provider</c> instead would re-enumerate the switch in
/// <c>DependencyInjection.AddEmailSender</c> and would go stale the day a third provider lands.
/// </para>
/// <para>
/// <b>Why the dependency resolves here and why the Worker is untouched.</b> This validator is
/// registered in <c>AddIdentityAndSessions</c>, which every HOST composition reaches together with
/// <c>AddEmailSender</c>, so wherever this type resolves, an <see cref="IEmailSender"/>
/// does. (One test calls that module alone, to pin the registration; it never resolves the
/// validator.) Where the pairing ever stops holding it fails LOUD, on an unresolvable constructor
/// argument at boot, never on a silently open gate. <c>ProductionStartupSmokeTests</c> boots a real
/// Production host with the real <c>NullEmailSender</c>, so the construction is pinned rather than
/// argued. Re-measure the composition with:
/// <c>grep -rn "AddIdentityAndSessions" --include=*.cs src/ tests/</c>.
/// The Worker calls <c>AddEmailSender</c> too but composes identity through
/// <c>AddCoreIdentityForWorker</c>, which binds the same <c>Auth</c> section with a plain
/// <c>Configure</c> and registers no validator. That asymmetry is deliberate and is preserved here:
/// the Worker owns no registration surface, so a shared env file must not take it down for a
/// condition it cannot exercise. Putting either rule inside <c>AddEmailSender</c> — the one seam both
/// hosts share — would do exactly that.
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

        // The RequireEmailConfirmation clause is stated rather than inherited from the branch above,
        // so the rule stands on its own predicate if the two are ever reordered.
        if (options.RegistrationsOpen && options.RequireEmailConfirmation && !emailSender.CanDeliver)
        {
            return ValidateOptionsResult.Fail(
                "Auth:RegistrationsOpen=true med Auth:RequireEmailConfirmation=true kräver en "
                + "Email:Provider som faktiskt levererar utanför Development/Test (aktuell miljö: "
                + $"{environment.EnvironmentName}; registrerad avsändare: "
                + $"{emailSender.GetType().Name}). Utan leverans skapas kontot, inloggningen spärras "
                + "på EmailConfirmed och aktiveringslänken når ingen — kontot blir permanent onåbart, "
                + "och återsändningen är lika tyst. Sätt Email__Provider=Scaleway med "
                + "Email__Scaleway-nycklarna (förutsättningarna ägs av #734, prod-flippens "
                + "GDPR-grind av #183), eller lämna registreringen stängd.");
        }

        return ValidateOptionsResult.Success;
    }
}
