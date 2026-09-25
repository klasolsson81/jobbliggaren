using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobSeekers;

/// <summary>
/// ADR 0142 D6 (#1736, closes #1484) — the terms-acceptance stamp a <see cref="JobSeeker"/> is
/// registered with: the instant the holder accepted the terms, and the versions of the terms and the
/// privacy policy that were current then. Owned by the aggregate; three columns on
/// <c>job_seekers</c> (<c>terms_accepted_at</c>, <c>terms_version</c>, <c>privacy_policy_version</c>).
/// <para>
/// <b>Naming is deliberate.</b> This is contract formation (Art. 6(1)(b)), not consent in the Art. 7
/// sense — a <c>consent_*</c> name would imply an Art. 7(3) withdrawal right that does not exist;
/// "withdrawing" the terms is closing the account. <see cref="PrivacyPolicyVersion"/> stamps the
/// <b>Art. 13 notice version</b> for Art. 5(2) accountability, never an acceptance fact (ADR 0142 D6).
/// </para>
/// <para>
/// <b>The versions are constants here, never inputs.</b> Domain reads no files; each value is the ISO
/// date the published copy carries as "Senast uppdaterad", and
/// <c>TermsAcceptanceVersionsMatchPublishedPolicyTests</c> pins both locales of
/// <c>messages/{sv,en}/content-legal.json</c> against these constants. No request in the epic carries
/// the version the user saw (registration and the code-step <c>complete</c> both send a bool), so the
/// only factory is total. A validating <c>Create(termsVersion, privacyPolicyVersion, acceptedAt)</c>
/// returning <c>Result</c> is deferred to the part that first sends a version — not omitted.
/// </para>
/// <para>
/// A <c>sealed record</c>, not a struct: EF Core maps it as an optional owned type, and
/// <c>OwnsOne</c> requires a reference type. The private constructor's parameter names match the
/// property names so EF's constructor binding materializes it — the <c>ManualPosting</c> form.
/// </para>
/// </summary>
public sealed record TermsAcceptance
{
    /// <summary>The "Senast uppdaterad" date of the published terms (<c>terms.updated</c>).</summary>
    public const string CurrentTermsVersion = "2026-09-25";

    /// <summary>The "Senast uppdaterad" date of the published privacy policy (<c>privacy.updated</c>).</summary>
    public const string CurrentPrivacyPolicyVersion = "2026-09-26";

    public DateTimeOffset AcceptedAt { get; }
    public string TermsVersion { get; }
    public string PrivacyPolicyVersion { get; }

    private TermsAcceptance(DateTimeOffset acceptedAt, string termsVersion, string privacyPolicyVersion)
    {
        AcceptedAt = acceptedAt;
        TermsVersion = termsVersion;
        PrivacyPolicyVersion = privacyPolicyVersion;
    }

    /// <summary>
    /// The stamp for a holder who accepted the terms as currently published, at the clock's now.
    /// Total by construction: both versions are the constants above, and the instant is the same
    /// unguarded clock read <see cref="JobSeeker.Register"/> makes for <c>CreatedAt</c>.
    /// </summary>
    public static TermsAcceptance AcceptCurrent(IDateTimeProvider clock) =>
        new(clock.UtcNow, CurrentTermsVersion, CurrentPrivacyPolicyVersion);
}
