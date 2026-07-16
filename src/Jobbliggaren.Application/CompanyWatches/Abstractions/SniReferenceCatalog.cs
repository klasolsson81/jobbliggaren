namespace Jobbliggaren.Application.CompanyWatches.Abstractions;

/// <summary>
/// The SNI 2025 hierarchy as the Application layer consumes it: sections (avdelning, "A"–"V") →
/// divisions (huvudgrupp, 2-digit) → leaves (detaljgrupp, 5-digit — the ONLY level a criterion may
/// store, Fork B1: the picker expands a section/division selection to its leaves FE-side and the
/// wire contract stays leaves-only). Leaf codes carry no dot ("01110"), matching
/// <c>company_register.sni_codes</c>.
///
/// <para>
/// Referential integrity (every leaf's division exists, every division's section exists, all codes
/// unique and well-formed) is enforced fail-loud by the Infrastructure loader at host build — a
/// malformed dataset never becomes a running host. The constructor here re-checks only what it
/// needs for its own lookups.
/// </para>
/// </summary>
public sealed class SniReferenceCatalog
{
    private readonly HashSet<string> _leafCodes;

    public SniReferenceCatalog(
        string version,
        IReadOnlyList<SniSection> sections,
        IReadOnlyList<SniDivision> divisions,
        IReadOnlyList<SniLeaf> leaves)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(divisions);
        ArgumentNullException.ThrowIfNull(leaves);

        Version = version;
        Sections = sections;
        Divisions = divisions;
        Leaves = leaves;
        _leafCodes = leaves.Select(static l => l.Code).ToHashSet(StringComparer.Ordinal);

        if (_leafCodes.Count != leaves.Count)
            throw new ArgumentException("SNI-katalogen innehåller dubblerade lövkoder.", nameof(leaves));
    }

    /// <summary>Dataset version stamp (e.g. "2025.v1") — surfaced to the FE so a stale picker
    /// cache is diagnosable, never guessed at.</summary>
    public string Version { get; }

    public IReadOnlyList<SniSection> Sections { get; }

    public IReadOnlyList<SniDivision> Divisions { get; }

    public IReadOnlyList<SniLeaf> Leaves { get; }

    /// <summary>Ordinal membership check — the existence-validator's whole question.</summary>
    public bool LeafExists(string code) => _leafCodes.Contains(code);
}

/// <summary>Avdelning ("A" = Jordbruk, skogsbruk och fiske …). Grouping level for the picker.</summary>
public sealed record SniSection(string Code, string Name);

/// <summary>Huvudgrupp (2-digit, "62" = IT-tjänster). <see cref="SectionCode"/> points at its
/// avdelning. Grouping/expansion level for the picker.</summary>
public sealed record SniDivision(string Code, string SectionCode, string Name);

/// <summary>Detaljgrupp (5-digit leaf, "62010"). <see cref="DivisionCode"/> = the first two digits.
/// The only level a stored criterion may carry.</summary>
public sealed record SniLeaf(string Code, string DivisionCode, string Name);
