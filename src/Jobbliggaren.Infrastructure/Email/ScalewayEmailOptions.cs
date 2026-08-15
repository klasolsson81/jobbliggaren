using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// Provider-scoped configuration for the Scaleway Transactional Email arm (#183). Bound and
/// validated ONLY inside <c>AddEmailSender</c>'s <c>Email:Provider="Scaleway"</c> branch, so
/// Console/Null keep their lazy, non-validating binding.
/// <para>
/// <b>Why a separate options class rather than fields on <c>EmailOptions</c>.</b> The shared
/// options object is constructed by every sender and by every Console/Null test; hanging a
/// provider's credentials on it would make a Scaleway concern reachable from arms that have none
/// (ISP), and #220 already removed a dead <c>EmailOptions.AwsRegion</c> for exactly that shape.
/// A provider-scoped section is the house idiom (<c>BreachCheckOptions</c>,
/// <c>DigestDispatchOptions</c>). The reasoning is unchanged by the provider swap — it was never
/// about which provider, only about which arms may see a credential.
/// </para>
/// <para>
/// <b>No defaults, deliberately.</b> Every property is <see cref="RequiredAttribute"/> with an
/// empty initialiser: a default region is a silent-wrong-region footgun, and a missing credential
/// must fail loudly rather than send unauthenticated. There is no ambient credential chain to fall
/// back to here — unlike the retired SES arm, an unauthenticated request is simply refused by the
/// API — but the value still belongs in a gitignored <c>appsettings.Local.json</c> locally and a
/// managed secret in ops, never a committed file (CLAUDE.md §5).
/// </para>
/// <para>
/// <b>TWO secrets, not two halves of one.</b> <see cref="SecretKey"/> authenticates the caller and
/// <see cref="ProjectId"/> selects the project the mail is billed and attributed to. They are
/// separate values with separate rotation lifetimes, so each is required independently and neither
/// is derivable from the other.
/// </para>
/// </summary>
public sealed class ScalewayEmailOptions
{
    public const string SectionName = "Email:Scaleway";

    /// <summary>
    /// Scaleway region for the Transactional Email endpoint — <c>fr-par</c> today, and the only
    /// value the arm accepts (see <see cref="ScalewayClientRegistration"/>, which owns the
    /// allow-list and builds the region into the client's base address). ALWAYS explicit: the
    /// region is itself a data-protection fact (#1169), and it is also the one string that decides
    /// whether the URL the sender POSTs to exists at all.
    /// </summary>
    [Required]
    public string Region { get; init; } = string.Empty;

    /// <summary>
    /// Scaleway API secret key, sent as the <c>X-Auth-Token</c> header. Never logged, never
    /// committed.
    /// </summary>
    [Required]
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>
    /// Scaleway project id, sent as <c>project_id</c> in the request body. Not a credential on its
    /// own, but it is never logged either — it identifies the account, and the send path logs
    /// nothing beyond the email kind.
    /// </summary>
    [Required]
    public string ProjectId { get; init; } = string.Empty;
}
