using System.Linq.Expressions;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Publication;

/// <summary>
/// The one publication predicate for job ads (ADR 0160). Every public surface — the anonymous
/// detail read, the job page, <c>JobPosting</c>, the sitemap and later the catalogue — asks this
/// type whether an ad is public. None keeps a definition of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>The source is the import provenance, never <c>JobAd.Source</c>.</b> <c>Source</c> is
/// user-settable: <c>POST /api/v1/job-ads</c> accepts <c>Source = "Platsbanken"</c> and stores an
/// ownerless, unscrubbed row with no <c>External</c>. Only <c>JobAd.Import</c> writes
/// <c>External</c>, so callers pass <c>External?.Source</c>.
/// </para>
/// <para>
/// <b>The deadline boundary is the archiver's.</b> <c>ExpireJobAdsJob</c> archives
/// <c>ExpiresAt &lt; now</c>, so an ad is still listed at the instant <c>ExpiresAt == now</c>.
/// </para>
/// <para>
/// <see cref="IsListed"/> is the SQL form, added by its first list consumer (the sitemap, ADR 0174).
/// The two forms share no code: an agreement test pins them in every state, and a Testcontainers
/// test pins the SQL translation.
/// </para>
/// </remarks>
public static class JobAdPublication
{
    /// <summary>
    /// The one source whose ads may be published: its licence was read from a primary source
    /// (CC0, ADR 0160). Widening it is an ADR amendment.
    /// </summary>
    public static readonly JobSource PublishedSource = JobSource.Platsbanken;

    /// <param name="importedFrom">The ad's <c>External?.Source</c>; <see langword="null"/> for an ad
    /// that was never imported.</param>
    public static JobAdPublicationState Classify(
        JobAdStatus status,
        DateTimeOffset? expiresAt,
        JobSource? importedFrom,
        DateTimeOffset now)
    {
        // NotPublic first, Erased or not: a row the public surface never held answers like an
        // unknown id, so no state of it can be told apart from outside.
        if (importedFrom != PublishedSource)
            return JobAdPublicationState.NotPublic;

        if (status == JobAdStatus.Erased)
            return JobAdPublicationState.Gone;

        if (status != JobAdStatus.Active || (expiresAt is { } deadline && deadline < now))
            return JobAdPublicationState.Ended;

        return JobAdPublicationState.Listed;
    }

    /// <summary>
    /// <see cref="Classify"/> returns <see cref="JobAdPublicationState.Listed"/>, as a query filter.
    /// </summary>
    /// <remarks>
    /// <c>External != null</c> is redundant in SQL and is there for the compiled form: without it a
    /// never-imported row would throw instead of being excluded.
    /// </remarks>
    public static Expression<Func<JobAd, bool>> IsListed(DateTimeOffset now) =>
        j => j.External != null
             && j.External.Source == PublishedSource
             && j.Status == JobAdStatus.Active
             && (j.ExpiresAt == null || j.ExpiresAt >= now);
}
