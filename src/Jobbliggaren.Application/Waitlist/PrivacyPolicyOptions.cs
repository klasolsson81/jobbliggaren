namespace Jobbliggaren.Application.Waitlist;

/// <summary>
/// Server-side privacy-policy-versionering. Stämplas på
/// <see cref="Jobbliggaren.Domain.Waitlist.ConsentSnapshot.PrivacyPolicyVersion"/>
/// vid waitlist-signup som GDPR Art. 7(1) bevis för vilken policy-version
/// användaren accepterade. Klient-skickad version accepteras inte
/// (manipulerbar — bevisvärde faller).
///
/// <para>
/// Bind:as mot <c>PrivacyPolicy</c>-sektionen i <c>appsettings.json</c>:
/// <code>
/// "PrivacyPolicy": {
///   "CurrentVersion": "1.0"
/// }
/// </code>
/// </para>
///
/// <para>
/// BUILD.md §20 noterar att versionerad policy-text inte är live ännu.
/// Default <c>"1.0"</c> låser bevissträng; faktisk policy-text levereras
/// av produkt innan första prod-deploy.
/// </para>
/// </summary>
public sealed class PrivacyPolicyOptions
{
    public const string SectionName = "PrivacyPolicy";

    /// <summary>
    /// Aktuell version som tjänsten levererar till användare. Default "1.0".
    /// </summary>
    public string CurrentVersion { get; set; } = "1.0";
}
