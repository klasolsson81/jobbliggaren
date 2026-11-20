namespace Jobbliggaren.Application.RecentJobSearches.Abstractions;

/// <summary>
/// #2199 PR 2b — whether a Yrke selection singles out a religious office: a name the lineage artefact files
/// under one of <see cref="Common.ReligiousOccupationGroups"/> (deprecated names included) on either axis, or
/// one of those groups picked on its own. A group is not singled out when the occupation-group axis holds
/// every group of an occupation field that contains it: "Välj alla" on Yrken med social inriktning means
/// the field, not its priests (senior-cto-advisor, 2026-10-26).
/// </summary>
/// <remarks>
/// It answers for both axes together, so a group id sent as an occupation-name is caught too (the validator
/// checks the shape only), and a whole field counts only on the group axis. It reads the artefacts the
/// search reads but never the search rule: which group files a name is JobTech's classification, and the
/// replaced-by edges belong to the filter.
/// </remarks>
public interface IReligiousOccupations
{
    bool AreSingledOutBy(IReadOnlyCollection<string> occupationGroups, IReadOnlyCollection<string> occupationNames);
}
