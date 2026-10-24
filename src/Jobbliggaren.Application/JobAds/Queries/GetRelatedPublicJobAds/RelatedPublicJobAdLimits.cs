namespace Jobbliggaren.Application.JobAds.Queries.GetRelatedPublicJobAds;

/// <summary>The related section's two bounds (ADR 0182). <c>RelatedPublicJobAdLimitsTests</c> pins both.</summary>
public static class RelatedPublicJobAdLimits
{
    /// <summary>
    /// At most five rows: enough to offer a choice under one ad without turning its page into a list page of the group
    /// (ADR 0166 D10 trigger 17 holds this cap).
    /// </summary>
    public const int MaxRows = 5;

    /// <summary>
    /// The candidates read before the one-row-per-employer walk. Set by cost, never by the employer distribution, which
    /// cannot be measured on the box without a count per employer, and such a count can name a person (CLAUDE.md §9.2).
    /// The database's work is the group's heap fetch and a top-N sort, the same for any window; the window bounds only
    /// the rows sent and walked, about 25 KB. The section comes up short only when at most four employers besides the
    /// anchor's hold all of the window's rows, and then every row it shows is still true.
    /// </summary>
    public const int CandidateWindow = 100;
}
