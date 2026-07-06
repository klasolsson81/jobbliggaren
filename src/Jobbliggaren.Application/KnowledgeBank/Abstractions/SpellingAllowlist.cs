using System.Collections.Frozen;
using System.Text;

namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// The versioned spelling allowlist (Fas 4b PR-6, ADR 0093 §D4) — the set of proper nouns
/// and technical terms the C7 spelling criterion suppresses so a legitimate CV token the
/// Hunspell dictionary lacks (e.g. "kubernetes", "devops") is never flagged as a
/// misspelling. Plain-string version (senior-cto-advisor DQ3 — the allowlist evolves at a
/// different cadence than the rubric, parity with <c>ClicheList.Version</c>).
/// <para>
/// Membership is the allowlist's OWN contract (SoC): terms are folded to Unicode NFC and
/// compared <see cref="StringComparer.OrdinalIgnoreCase"/> so a precomposed "å" matches a
/// combining-ring "å" and casing never matters (the Swedish å/ä/ö have unambiguous 1:1 case
/// maps, so ordinal-ignore-case is culture-safe — CTO-bind PR-6 D-D). Whole-token only — a
/// caller checks a full word, never a substring (so "java" never suppresses the misspelling
/// "javaa").
/// </para>
/// </summary>
public sealed class SpellingAllowlist
{
    private readonly FrozenSet<string> _terms;

    /// <summary>The asset version (<c>allowlistVersion</c>).</summary>
    public string Version { get; }

    /// <summary>Builds the allowlist, NFC-folding + trimming every term into a
    /// case-insensitive frozen set (blank entries dropped).</summary>
    public SpellingAllowlist(string version, IEnumerable<string> terms)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(terms);

        Version = version;
        _terms = terms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(Normalize)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The number of distinct allowlisted terms.</summary>
    public int Count => _terms.Count;

    /// <summary>
    /// True if <paramref name="token"/> is an allowlisted proper noun / technical term
    /// (NFC-folded, case-insensitive, whole-token). A blank token is never a member.
    /// </summary>
    public bool Contains(string token) =>
        !string.IsNullOrWhiteSpace(token) && _terms.Contains(Normalize(token));

    // ONE canonicalization (parity FindingTargetFingerprint.Normalize's NFC discipline): fold
    // to NFC + trim so a combining-diacritic drift between the asset and the CV text can never
    // slip a term past the allowlist. Case folding is left to the set's OrdinalIgnoreCase comparer.
    private static string Normalize(string value) => value.Trim().Normalize(NormalizationForm.FormC);
}
