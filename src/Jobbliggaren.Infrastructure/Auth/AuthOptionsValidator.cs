using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// Startup validation for <see cref="AuthOptions"/> (ADR 0083 Amendment 2026-08-03,
/// senior-cto-advisor bind 2026-08-03). Refuses to boot on TWO conditions, neither of which fires inside
/// Development or Test:
/// <list type="number">
/// <item><c>RegistrationsOpen</c> WITHOUT <c>RequireEmailConfirmation</c> — legacy instant-login: an
/// account minted with no proof the registrant owns the address, and the acknowledged-deferred
/// 200-vs-400 duplicate-enumeration oracle live on a public IP.</item>
/// <item>a registered <see cref="IEmailSender"/> that cannot deliver, whatever either flag says. Every
/// login is a mailed code or link and there is no break-glass (ADR 0142 D10), so such a host answers
/// every login request with the uniform 202 and sends nothing: nobody can log in, and nothing says so.
/// The rule was added 2026-08-09 as senior-cto-advisor's D1, the composition-time boot refusal
/// <c>NullEmailSender</c>'s own contract names as its owner, and then required both flags on, because it
/// guarded only registration's activation link; security-auditor Major 12 (#1735) dropped both.</item>
/// </list>
/// <para>
/// Why controls rather than comments: both combinations were documented and unenforced, and
/// repairing that with more documentation would reproduce its mechanism.
/// </para>
/// <para>
/// Rule 1 fires in ONE direction only: an absent <c>Auth</c> section binds both flags to <c>false</c>
/// and cannot trip it. Rule 2 has no flag to leave closed, by design: a deployed host that cannot
/// deliver mail is a host nobody can log in to.
/// </para>
/// <para>
/// The exemption is an ALLOWLIST (Development, Test), never <c>!IsProduction()</c> — a denylist would
/// exempt Staging and every unrecognised environment name silently, which is the class of silence this
/// change repairs. It reuses the house's established exemption predicate rather than inventing a
/// third.
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
/// Production host, so the construction is pinned rather than argued. Re-measure the composition with:
/// <c>grep -rn "AddIdentityAndSessions" --include=*.cs src/ tests/</c>.
/// The Worker calls <c>AddEmailSender</c> too but composes identity through
/// <c>AddCoreIdentityForWorker</c>, which binds the same <c>Auth</c> section with a plain
/// <c>Configure</c> and registers no validator. That asymmetry is deliberate and is preserved here:
/// the Worker owns no registration or login surface, so a shared env file must not take it down for
/// a condition it cannot exercise. Putting either rule inside <c>AddEmailSender</c> — the one seam both
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
                + "(förutsättningarna: docs/runbooks/registration-gate.md), eller lämna registreringen "
                + "stängd.");
        }

        if (!emailSender.CanDeliver)
        {
            return ValidateOptionsResult.Fail(
                "Utanför Development/Test krävs en Email:Provider som faktiskt levererar (aktuell miljö: "
                + $"{environment.EnvironmentName}; registrerad avsändare: "
                + $"{emailSender.GetType().Name}). All inloggning sker med en kod eller länk per e-post "
                + "och det finns ingen reservväg, så utan leverans kan ingen logga in, och begäran "
                + "besvaras ändå med ett enhetligt 202. Sätt Email__Provider=Scaleway med "
                + "Email__Scaleway-nycklarna enligt deploy/.env.example (GDPR-grinden: "
                + "release-checklist.md §2.5).");
        }

        return ValidateOptionsResult.Success;
    }
}
