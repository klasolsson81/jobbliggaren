namespace Jobbliggaren.Application.Common.Security;

/// <summary>
/// Turns a personal identifier into a stable, non-reversible pseudonym for the accountability
/// record (GDPR Art. 5(2)/30) — so we can prove <i>which</i> erasure request we served without
/// storing the identifier we were asked to erase.
/// </summary>
/// <remarks>
/// <b>The tension this resolves, stated plainly.</b> Art. 5(2) says record what you did. Art.
/// 5(1)(c) says do not keep her email. A plaintext identifier in <c>audit_log</c> would mean the
/// erasure request itself becomes the last place her address survives — the single most absurd
/// outcome available to us, and one this codebase came within one design review of shipping.
/// <para>
/// <b>Why not a plain hash.</b> An unkeyed digest of an email is dictionary-reversible in
/// milliseconds — the address space is small and the input is guessable. It is not a pseudonym,
/// it is a fig leaf. (The old runbook proposed <c>md5</c>; that is rejected outright.) A keyed
/// HMAC with a server-held pepper is not reversible without the pepper, which is what makes the
/// output pseudonymous data under Art. 4(5) rather than merely obscured personal data.
/// </para>
/// <para>
/// <b>It is still personal data.</b> We hold the pepper, so we can re-derive the link — Recital 26
/// is explicit that pseudonymised data remains personal data. This port buys a strictly better
/// at-rest posture, not an exemption, and the Art. 30 register says so.
/// </para>
/// <para>
/// ADR 0090 D5 bound HMAC-SHA256(server pepper) as the house pseudonymisation primitive. It was
/// never built. This is that primitive.
/// </para>
/// </remarks>
public interface IIdentifierPseudonymizer
{
    /// <summary>
    /// Returns a stable, lowercase hex HMAC-SHA256 of <paramref name="identifier"/> under the
    /// server pepper. Deterministic (the same identifier always yields the same pseudonym, so an
    /// operator can tell a repeat request from a new one) and non-reversible without the pepper.
    /// The identifier is trimmed and lowercased first, so casing/whitespace noise does not
    /// produce two pseudonyms for one person.
    /// </summary>
    string Pseudonymize(string identifier);
}
