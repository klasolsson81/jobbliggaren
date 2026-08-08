using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// Provider-scoped configuration for the Amazon SES v2 arm (ADR 0124, #1237). Bound and validated
/// ONLY inside <c>AddEmailSender</c>'s <c>Email:Provider="Ses"</c> branch, so Console/Null keep
/// their lazy, non-validating binding.
/// <para>
/// <b>Why a separate options class rather than fields on <c>EmailOptions</c>.</b> The shared
/// options object is constructed by every sender and by every Console/Null test; hanging a
/// provider's credentials on it would make an SES concern reachable from arms that have none
/// (ISP), and #220 already removed a dead <c>EmailOptions.AwsRegion</c> for exactly that shape.
/// A provider-scoped section is the house idiom (<c>BreachCheckOptions</c>,
/// <c>DigestDispatchOptions</c>).
/// </para>
/// <para>
/// <b>No defaults, deliberately.</b> Every property is <see cref="RequiredAttribute"/> with an
/// empty initialiser: a default region is a silent-wrong-region footgun, and a missing credential
/// must fail loudly rather than let the SDK's own chain pick up some other identity. There is no
/// instance role on the VPS, so these are a static IAM user's key — gitignored
/// <c>appsettings.Local.json</c> locally, managed secret in ops, never a committed file
/// (CLAUDE.md §5).
/// </para>
/// </summary>
public sealed class SesEmailOptions
{
    public const string SectionName = "Email:Ses";

    /// <summary>
    /// AWS region for the SES endpoint, e.g. <c>eu-north-1</c>. ALWAYS explicit — the SDK's default
    /// region chain (<c>AWS_REGION</c>, <c>~/.aws/config</c>, IMDS) must never be what decides which
    /// jurisdiction outbound mail leaves from, because the region is itself a data-protection fact
    /// (#1169).
    /// </summary>
    [Required]
    public string Region { get; init; } = string.Empty;

    /// <summary>IAM access key id for the send-only user. Never logged, never committed.</summary>
    [Required]
    public string AccessKeyId { get; init; } = string.Empty;

    /// <summary>IAM secret access key for the send-only user. Never logged, never committed.</summary>
    [Required]
    public string SecretAccessKey { get; init; } = string.Empty;
}
