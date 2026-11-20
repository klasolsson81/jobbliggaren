using System.Collections.Frozen;

namespace Jobbliggaren.Application.RecentJobSearches.Common;

/// <summary>
/// #2199 PR 2b — the occupation groups whose every name is a religious office, so a search that selects one
/// reveals a faith (GDPR Art. 9; security-auditor, #2199 PR 3 Major 1). A search that singles one of them out,
/// or names an office filed under one, is never stored in recent searches (<see cref="RecentSearchCaptureGate"/>,
/// <see cref="Abstractions.IReligiousOccupations"/>).
/// </summary>
/// <remarks>
/// <para>
/// The membership is senior-cto-advisor's, under the reach Klas delegated on 2026-10-25: these four are the
/// only religion-specific groups in JobTech's taxonomy, and names filed elsewhere are left out because each
/// means a job in a religious organisation or building, not a religious office. Those are not guarded and
/// not assessed; the ruling does not say they are outside Art. 9.
/// </para>
/// <para>
/// The names under these groups are never listed here. They are derived at load from the occupation-name
/// lineage artefact, and <c>ReligiousOccupationsArtefactTests</c> fails when the taxonomy is refreshed, so the
/// four are re-confirmed against every new version.
/// </para>
/// </remarks>
public static class ReligiousOccupationGroups
{
    /// <summary>Präster.</summary>
    public const string Priests = "2L2s_EU8_PzR";

    /// <summary>Pastorer m.fl.</summary>
    public const string Pastors = "x5rn_vga_gRK";

    /// <summary>Diakoner.</summary>
    public const string Deacons = "wzeq_No2_LF1";

    /// <summary>Chefer och ledare inom trossamfund.</summary>
    public const string LeadersOfReligiousCommunities = "x9mo_1VH_Rps";

    /// <summary>The four group concept ids.</summary>
    public static FrozenSet<string> ConceptIds { get; } =
        new[] { Priests, Pastors, Deacons, LeadersOfReligiousCommunities }.ToFrozenSet(StringComparer.Ordinal);
}
