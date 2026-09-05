namespace Jobbliggaren.Application.CompanyWatches.Queries;

/// <summary>
/// The surface names recorded on a cross-user attempt (ADR 0031). One value per read surface, and
/// deliberately NOT <c>nameof</c>: the two original values predate this type and carry no
/// <c>Query</c> suffix, so deriving the new ones from type names would put two shapes in one audit
/// column and break continuity with the log history already written under the old values.
/// </summary>
internal static class CriterionReadOperation
{
    public const string BrowseCompanies = "BrowseCompanies";
    public const string GetCriterionMatchMagnitude = "GetCriterionMatchMagnitude";
    public const string BrowseCriterionAds = "BrowseCriterionAds";
    public const string GetCriterionAdMagnitude = "GetCriterionAdMagnitude";
    public const string GetMyMatchingAdCountForCriterion = "GetMyMatchingAdCountForCriterion";
}
