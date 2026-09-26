using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Application.CompanyWatches.Abstractions;

/// <summary>
/// #1681 part 2 (ADR 0139; senior-cto-advisor 2026-09-06) — a stable digest of the PREDICATE a
/// materialisation was computed from, so a read can tell "these numbers are for the criterion on
/// screen" from "these numbers are for a predicate the user has since edited".
///
/// <para>
/// <b>Why a fingerprint and not <c>UpdatedAt</c> vs <c>MaterialisedAt</c>.</b> The obvious guard —
/// "is the row newer than the materialisation" — is keyed on the wrong fact.
/// <see cref="CompanyWatchCriterion.Rename"/> bumps <c>UpdatedAt</c> exactly as
/// <see cref="CompanyWatchCriterion.UpdateCriteria"/> does (verified in source, 2026-09-06), so
/// <c>UpdatedAt</c> is a row mtime, not a predicate-change signal. A timestamp guard would therefore
/// blank a working watch's numbers because its owner renamed it — a false refusal on the most common
/// edit. Worse, once the create/update recompute lands it will deliberately NOT fire on a rename (a
/// rename must not cost a register resolution), so a renamed criterion would carry
/// <c>UpdatedAt &gt; MaterialisedAt</c> until the next daily run, every time. A guard that blanks a
/// correct watch is worse than the defect it guards.
/// </para>
///
/// <para>
/// This answers the actual question instead of a proxy for it: <i>was this member set produced by
/// THIS predicate?</i> It is immune to renames and to any future third way of mutating the aggregate,
/// and it degrades in the right direction — a run that failed for one criterion leaves the old
/// fingerprint, so the read reports ignorance rather than asserting a number.
/// </para>
///
/// <para>
/// <b>It canonicalises HERE rather than trusting the spec to arrive canonical.</b>
/// <see cref="CompanyWatchCriteriaSpec.Create"/> does sort and de-duplicate, but
/// <see cref="CompanyWatchCriteriaSpec.FromTrusted"/> — the path EF loading takes — copies its input
/// verbatim. Relying on the stored columns having been normalised on write is exactly the
/// "invariant that holds as long as you came in the front door" the browse port's own docblock
/// warns about, and here it would fail CLOSED in the annoying direction: a reordered-but-identical
/// set would look like a different predicate and blank the numbers.
/// </para>
///
/// <para>
/// <b>SHA-256 with no key, and it is NOT tokenisation.</b> There is no secret, and none is wanted:
/// both sides must compute the same value from the same predicate, and a keyed digest would buy
/// nothing here. ⚠ It is also not a privacy measure and must not be described as one — the SNI/kommun
/// space is small enough to enumerate, so this digest is reversible by anyone who can read it. That
/// is acceptable ONLY because it adds no exposure: the row it sits on is already person-attributable
/// via <c>criterion_id → user_id</c>, and the plaintext codes it digests live in
/// <c>company_watch_criteria</c> in the same database. Storing the CODES here instead would have been
/// a second copy of criterion-PII in the very table whose person-attributability is an open,
/// unanswered escalation to Klas (ADR 0139 Implementationsstatus) — which is why this is a digest.
/// </para>
/// </summary>
public readonly record struct CriteriaFingerprint
{
    private CriteriaFingerprint(string value) => Value = value;

    /// <summary>Lower-case hex SHA-256, 64 chars — the stored column's exact width.</summary>
    public string Value { get; }

    /// <summary>The stored column's width, single-sourced so the migration and the type agree.</summary>
    public const int Length = 64;

    /// <summary>
    /// The digest of <paramref name="criteria"/>. Deterministic across processes and runs: the input
    /// is canonicalised, the two axes are separated by a byte that cannot occur in an SCB code, and
    /// each axis is length-prefixed so no regrouping of codes can produce a colliding input.
    /// </summary>
    public static CriteriaFingerprint Of(CompanyWatchCriteriaSpec criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        // Canonical form: ordinal sort + distinct, per axis. See the class docblock for why this is
        // done here rather than assumed of the incoming spec.
        var sni = Canonical(criteria.SniCodes);
        var kommun = Canonical(criteria.MunicipalityCodes);

        // '' (UNIT SEPARATOR) delimits codes and '' (RECORD SEPARATOR) the two axes.
        // SCB codes are digits, so neither can occur inside a value — the digest cannot be forged by
        // a code that contains the delimiter. The counts are written too, so ("62", []) and
        // ([], "62") cannot collapse to the same input.
        var payload = new StringBuilder()
            .Append(sni.Length).Append('')
            .AppendJoin('', sni).Append('')
            .Append(kommun.Length).Append('')
            .AppendJoin('', kommun)
            .ToString();

        return new CriteriaFingerprint(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
    }

    /// <summary>
    /// Rebuilds a fingerprint read back from storage. No validation beyond the shape: the value was
    /// produced by <see cref="Of"/> on write, and a stored value that somehow is not a digest simply
    /// fails to match, which degrades to "not materialised" — the safe direction.
    /// </summary>
    public static CriteriaFingerprint FromTrusted(string value) => new(value);

    private static string[] Canonical(IReadOnlyList<string> values) =>
        [.. values.Distinct(StringComparer.Ordinal).OrderBy(static v => v, StringComparer.Ordinal)];

    public override string ToString() => Value;
}
