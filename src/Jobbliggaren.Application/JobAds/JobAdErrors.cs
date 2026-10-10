using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.JobAds;

public static class JobAdErrors
{
    /// <summary>An id we never held, or one the public surface never held (404).</summary>
    public static DomainError NotFound { get; } =
        DomainError.NotFound("JobAd.NotFound", "Annonsen finns inte.");

    /// <summary>
    /// An ad that existed and is gone (410). Deliberately NEUTRAL: naming GDPR Art. 17 would let
    /// any caller — the ad id is public, and Arbetsförmedlingen publishes the same ad in its open
    /// Historiska annonser dataset — infer that the recruiter in it exercised a right. On the
    /// public surface an ended ad gets this same answer, for the same reason (ADR 0160).
    /// </summary>
    public static DomainError Gone { get; } =
        DomainError.Gone("JobAd.Gone", "Annonsen är inte längre tillgänglig.");
}
