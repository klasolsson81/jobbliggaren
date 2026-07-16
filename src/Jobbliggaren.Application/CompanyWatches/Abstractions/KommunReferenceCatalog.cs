namespace Jobbliggaren.Application.CompanyWatches.Abstractions;

/// <summary>
/// The Swedish län/kommun list as the Application layer consumes it: 21 län (2-digit codes) →
/// 290 kommuner (4-digit SCB codes, leading zero load-bearing: "0180" = Stockholm). These are
/// <b>SCB kommun codes matching <c>company_register.sate_kommun_code</c></b> — a deliberately
/// different namespace from the JobTech <c>municipality_concept_id</c> a job ad carries (RF-4,
/// ADR 0105). The code and the copy keep the namespaces apart; nothing may convert between them.
///
/// <para>
/// Referential integrity (every kommun's län exists, kommun code prefixed by its län code, all
/// codes unique and well-formed) is enforced fail-loud by the Infrastructure loader at host build.
/// </para>
/// </summary>
public sealed class KommunReferenceCatalog
{
    private readonly HashSet<string> _kommunCodes;

    public KommunReferenceCatalog(
        string version,
        IReadOnlyList<LanEntry> lan,
        IReadOnlyList<KommunEntry> kommuner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(lan);
        ArgumentNullException.ThrowIfNull(kommuner);

        Version = version;
        Lan = lan;
        Kommuner = kommuner;
        _kommunCodes = kommuner.Select(static k => k.Code).ToHashSet(StringComparer.Ordinal);

        if (_kommunCodes.Count != kommuner.Count)
            throw new ArgumentException("Kommun-katalogen innehåller dubblerade koder.", nameof(kommuner));
    }

    /// <summary>Dataset version stamp (e.g. "2026-01-01.v1").</summary>
    public string Version { get; }

    public IReadOnlyList<LanEntry> Lan { get; }

    public IReadOnlyList<KommunEntry> Kommuner { get; }

    /// <summary>Ordinal membership check — the existence-validator's whole question.</summary>
    public bool Exists(string code) => _kommunCodes.Contains(code);
}

/// <summary>Län ("01" = Stockholms län). Grouping level for the picker cascade.</summary>
public sealed record LanEntry(string Code, string Name);

/// <summary>Kommun ("0180" = Stockholm). <see cref="LanCode"/> = the first two digits.</summary>
public sealed record KommunEntry(string Code, string Name, string LanCode);
