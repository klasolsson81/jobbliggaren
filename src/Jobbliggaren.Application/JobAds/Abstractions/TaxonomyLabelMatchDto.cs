namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// The concept ids whose label a set of minus-words named exactly (<see cref="ITaxonomyLabelMatcher"/>),
/// one list per job-ad column they are compared against. There is no region list: every län label
/// ends in " län", so a single word can never name one.
/// </summary>
public sealed record TaxonomyLabelMatchDto(
    IReadOnlyList<string> Municipality,
    IReadOnlyList<string> OccupationGroup,
    IReadOnlyList<string> Occupation,
    IReadOnlyList<string> EmploymentType,
    IReadOnlyList<string> WorktimeExtent)
{
    public static TaxonomyLabelMatchDto Empty { get; } = new([], [], [], [], []);
}
