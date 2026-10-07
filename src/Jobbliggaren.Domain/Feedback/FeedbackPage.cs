using System.Diagnostics.CodeAnalysis;
using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Feedback;

/// <summary>
/// The fixed set of pages feedback is collected for (#1979). The name is the wire key the web's
/// route map sends and the value persisted, so a rating category can never come from a URL, a
/// filter or an ad/CV id — only from this list.
/// </summary>
public sealed class FeedbackPage : SmartEnum<FeedbackPage>
{
    public static readonly FeedbackPage Overview = new("overview", 1);
    public static readonly FeedbackPage Jobs = new("jobs", 2);
    public static readonly FeedbackPage JobAd = new("job-ad", 3);
    public static readonly FeedbackPage Matches = new("matches", 4);
    public static readonly FeedbackPage SavedAds = new("saved-ads", 5);
    public static readonly FeedbackPage SavedSearches = new("saved-searches", 6);
    public static readonly FeedbackPage Applications = new("applications", 7);
    public static readonly FeedbackPage Application = new("application", 8);
    public static readonly FeedbackPage NewApplication = new("new-application", 9);
    public static readonly FeedbackPage Statistics = new("statistics", 10);
    public static readonly FeedbackPage ActivityReport = new("activity-report", 11);
    public static readonly FeedbackPage FollowedCompanies = new("followed-companies", 12);
    public static readonly FeedbackPage CompanySearch = new("company-search", 13);
    public static readonly FeedbackPage IndustryWatches = new("industry-watches", 14);
    public static readonly FeedbackPage ApplicationHistory = new("application-history", 15);
    public static readonly FeedbackPage Cv = new("cv", 16);
    public static readonly FeedbackPage CvImport = new("cv-import", 17);
    public static readonly FeedbackPage CvReview = new("cv-review", 18);
    public static readonly FeedbackPage MyPages = new("my-pages", 19);

    private FeedbackPage(string name, int value) : base(name, value) { }

    /// <summary>Exact, case-sensitive match on the wire key; anything else is refused.</summary>
    public static bool TryFromKey(string? key, [NotNullWhen(true)] out FeedbackPage? page)
    {
        page = null;
        return !string.IsNullOrEmpty(key) && TryFromName(key, ignoreCase: false, out page);
    }
}
