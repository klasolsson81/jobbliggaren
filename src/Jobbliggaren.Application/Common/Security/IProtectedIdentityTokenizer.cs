namespace Jobbliggaren.Application.Common.Security;

/// <summary>
/// Turns a personnummer-shaped (enskild-firma) organisationsnummer into a stable, non-reversible
/// keyed token for at-rest storage in <c>company_watches</c> (ADR 0090 D5 / #544). A sole
/// proprietor's org.nr <i>equals</i> their personnummer, so it must never be held in plaintext —
/// while the follow key must stay deterministically equality-matchable by
/// <c>CompanyWatchScanJob</c> (a DEK breaks SQL <c>IN</c>; a keyed HMAC does not).
/// </summary>
/// <remarks>
/// <b>Deliberately distinct from <see cref="IIdentifierPseudonymizer"/> — a rule with two
/// normalisers is two rules.</b> That port pseudonymises an <i>email</i> for the Art. 17 erasure
/// audit log and normalises <c>Trim().ToLowerInvariant()</c>; its own contract says it must never be
/// used to find a subject's data again. This port does the opposite by design: it tokenises an
/// <i>already-canonical</i> 10-digit org.nr <b>verbatim</b>, and its whole purpose is to find the
/// matching ad, deterministically. Sharing the audit port's normalisation would couple this live
/// at-rest key to the audit-log key so a future audit-normalisation change would silently orphan
/// every stored watch token (the product's cardinal sin — a watch that matches nothing forever).
/// <para>
/// Keyed HMAC-SHA256 under a <b>separate</b> server pepper (own key, own rotation posture — ADR 0090
/// D5, R1 permanent: destroy-in-place backfill makes it non-rotatable for existing rows). The token
/// is non-reversible without the pepper but is <b>still personal data</b> (Recital 26): never logged,
/// never surfaced as an org.nr, never in a URL / cache key.
/// </para>
/// </remarks>
public interface IProtectedIdentityTokenizer
{
    /// <summary>
    /// Returns a stable lowercase-hex HMAC-SHA256 of the <b>verbatim</b> 10-digit org.nr under the
    /// watch pepper. Deterministic (same org.nr → same token, so <c>CompanyWatchScanJob</c> can
    /// equality-match) and non-reversible without the pepper.
    /// </summary>
    string Tokenize(string organizationNumber);
}
