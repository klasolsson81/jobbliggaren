using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetJobsWatermark;

/// <summary>
/// #293 (ADR 0042 Beslut E amendment) — reads the authenticated user's last-seen-jobs
/// watermark (<c>JobSeeker.LastSeenJobsAt</c>). The FE uses the scalar to compute the per-user
/// "Ny" tag client-side (NY = JobAd.CreatedAt &gt; watermark), keeping per-user state OFF the
/// public <c>JobAdDto</c> projection (ADR 0063 Beslut b). Parameterless — the watermark is the
/// authenticated user's own. No authenticated user / no JobSeeker → null watermark (anon and
/// first-ever visitors get no NY; the visit itself establishes the baseline via
/// <see cref="Jobbliggaren.Application.JobAds.Commands.MarkJobsSeen.MarkJobsSeenCommand"/>).
/// </summary>
public sealed record GetJobsWatermarkQuery : IQuery<JobsWatermarkDto>;
