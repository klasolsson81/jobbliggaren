namespace Jobbliggaren.Application.CompanyWatches.Abstractions;

/// <summary>
/// Search aliases over the SNI 2025 branch names (#1115) — the everyday and occupational words a
/// user types that SNI itself has no word for. SNI classifies ACTIVITIES, so "systemutveckl"
/// returned zero rows against all 944 nodes; the vocabulary gap is what this catalog closes.
///
/// <para>
/// <b>This is not a crosswalk, and the distinction is load-bearing</b> (#560 bind 4). An alias
/// carries only the SNI code(s) its source published it under; no SSYK/JobTech concept id appears
/// here or in the asset. Nothing derives, preselects or persists from an alias — it widens what the
/// picker's filter SHOWS, and the user still picks the SNI node, which is exactly the guarantee
/// bind 4 states ("användaren väljer bransch (SNI) ur sökbar lista"). The <c>sni</c> URL axis and
/// every query predicate are untouched.
/// </para>
///
/// <para>
/// Two sources, and <see cref="SniAlias.Source"/> says which per row: <c>"scb"</c> entries are
/// reproduced verbatim from SCB SNI-sök, <c>"authored"</c> entries are written by this repo for
/// demand words SCB does not carry at all. Provenance, the demand list and the entry conditions for
/// an authored term live in <c>tools/sni-aliases/</c>.
/// </para>
/// </summary>
public sealed class SniAliasCatalog
{
    private readonly Dictionary<string, IReadOnlyList<string>> _termsByCode;

    public SniAliasCatalog(
        string version,
        string sniVersion,
        string demandVersion,
        IReadOnlyList<SniAlias> aliases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(sniVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(demandVersion);
        ArgumentNullException.ThrowIfNull(aliases);

        Version = version;
        SniVersion = sniVersion;
        DemandVersion = demandVersion;
        Aliases = aliases;

        // A code may carry both an "scb" and an "authored" row; the picker wants one list per code,
        // while the rows keep their provenance for review and tests.
        _termsByCode = aliases
            .GroupBy(static a => a.Code, StringComparer.Ordinal)
            .ToDictionary(
                static g => g.Key,
                static g => (IReadOnlyList<string>)g.SelectMany(static a => a.Terms).ToList(),
                StringComparer.Ordinal);
    }

    /// <summary>Alias dataset version ("2025.alias.v1").</summary>
    public string Version { get; }

    /// <summary>The SNI dataset version this alias set was built against. Pinned equal to
    /// <see cref="SniReferenceCatalog.Version"/> at host build, so re-versioning SNI cannot silently
    /// leave a stale alias set behind.</summary>
    public string SniVersion { get; }

    /// <summary>The dated demand list the extract was selected by ("2026-09-05.v1"). Surfaced so a
    /// coverage question can be answered against the list that produced it.</summary>
    public string DemandVersion { get; }

    public IReadOnlyList<SniAlias> Aliases { get; }

    /// <summary>Every alias term for one SNI code, both sources merged. Empty where the code has
    /// none — most codes do not.</summary>
    public IReadOnlyList<string> TermsFor(string code) =>
        _termsByCode.GetValueOrDefault(code, []);
}

/// <summary>
/// One alias row: the SNI code, where the terms came from, and the terms themselves. Terms are
/// stored exactly as the source published them — normalisation is a matching concern and never
/// changes the stored string, so what the UI shows is what SCB wrote.
/// </summary>
public sealed record SniAlias(string Code, string Source, IReadOnlyList<string> Terms);
