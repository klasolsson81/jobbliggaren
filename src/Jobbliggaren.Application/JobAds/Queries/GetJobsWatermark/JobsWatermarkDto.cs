namespace Jobbliggaren.Application.JobAds.Queries.GetJobsWatermark;

/// <summary>
/// #293 (ADR 0042 Beslut E amendment) — the user-read watermark for the /jobb surface.
/// <c>LastSeenJobsAt == null</c> when there is no authenticated user, no JobSeeker, or the user
/// has never visited /jobb. The FE renders the "Ny" tag for an ad only when
/// <c>LastSeenJobsAt != null &amp;&amp; JobAd.CreatedAt &gt; LastSeenJobsAt</c> — so a null
/// watermark yields no NY (honest first-visit / anon behaviour).
/// </summary>
public sealed record JobsWatermarkDto(DateTimeOffset? LastSeenJobsAt)
{
    public static readonly JobsWatermarkDto Empty = new((DateTimeOffset?)null);
}
