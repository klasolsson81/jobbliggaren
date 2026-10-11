using Hangfire;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// Worker wrapper for <see cref="BackgroundMatchingJob"/> (ADR 0080, ADR 0161) that applies the Hangfire
/// attributes without leaking a Hangfire dependency into the Application layer (Clean Architecture,
/// ADR 0023 sub-decision 2; parity with <see cref="ExpireJobAdsWorker"/>).
/// <para>
/// <b>The lock is what keeps a Toppmatch from being mailed twice.</b> <c>user_job_ad_matches</c> has no
/// concurrency token, so two runs at once would both claim the same Pending row. A tick that does not get
/// the lock within 120 s gives up without a retry: the next tick five minutes later is the retry, because
/// the watermark and the Pending rows are the queue. 120 s is under half the interval, so at most one tick
/// waits (the derivation in <see cref="CompanyWatchCriterionMaterialisationWorker"/>).
/// </para>
/// </summary>
public sealed class BackgroundMatchingWorker(BackgroundMatchingJob job)
{
    /// <summary>
    /// Every five minutes, two minutes past each multiple of five, so the tick never starts together with
    /// the ad stream (<c>*/10</c>) or the landing statistics (<c>*/5</c>). A constant, not a configuration
    /// value: <see cref="BackgroundMatchingJob.TopMatchMailBound"/> rests on it, and a test computes the
    /// bound from it.
    /// </summary>
    public const string Cron = "2-59/5 * * * *";

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
