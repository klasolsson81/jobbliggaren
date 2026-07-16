namespace Jobbliggaren.Domain.JobAds;

/// <summary>
/// Where an <see cref="AdContact"/> came from (#842 Tier A, ADR 0106 / CTO re-bind R1(b)).
/// </summary>
/// <remarks>
/// The discriminator is not decoration. A regex hit is OUR inference; the advertiser's
/// <c>application_contacts</c> block is HER declaration. Presenting the first as the second is the
/// same class of untruth CLAUDE.md §5 bans for the CV engine (a verdict without cited evidence) —
/// the UI must say which one it is showing. Persisted in jsonb BY NAME, never by ordinal, so a
/// future enum reordering cannot silently re-label stored contacts
/// (see <c>AdContactsConverters</c>).
/// </remarks>
public enum AdContactOrigin
{
    /// <summary>Declared by the advertiser in the source's structured <c>application_contacts</c> block.</summary>
    Declared,

    /// <summary>
    /// Promoted from a deterministic detector hit in the ad body (email/phone the scrub removed).
    /// <c>Name</c> is always null here — we never guess a name (no NER, ADR 0106 D5).
    /// </summary>
    ExtractedFromBody,
}
