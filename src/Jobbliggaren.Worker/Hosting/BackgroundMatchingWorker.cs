using Hangfire;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// Worker-wrapper för <see cref="BackgroundMatchingJob"/> (ADR 0080, ADR 0161) som applicerar
/// Hangfire-attributen utan att läcka Hangfire-beroende till Application-lagret (Clean Arch — ADR 0023
/// delbeslut 2; paritet <see cref="ExpireJobAdsWorker"/>).
/// <para>
/// <b>Låset är det som gör att en Toppmatch aldrig mejlas två gånger.</b> <c>user_job_ad_matches</c>
/// saknar concurrency token, så två samtidiga körningar skulle båda claima samma Pending-rad. En tick
/// som inte får låset inom 120 s ger upp utan omförsök: nästa tick fem minuter senare är omförsöket,
/// eftersom vattenmärket och Pending-raderna är kön. 120 s är under halva intervallet, så högst en
/// tick står och väntar (samma härledning som <see cref="CompanyWatchCriterionMaterialisationWorker"/>).
/// </para>
/// </summary>
public sealed class BackgroundMatchingWorker(BackgroundMatchingJob job)
{
    /// <summary>
    /// Var femte minut, två minuter efter jämna femminuterstal: ticken startar inte samtidigt som
    /// annonsströmmen (<c>*/10</c>) eller landningsstatistiken (<c>*/5</c>). En konstant, inte ett
    /// konfigurationsvärde: <see cref="BackgroundMatchingJob.TopMatchMailBound"/> vilar på den, och ett test
    /// räknar gränsen ur den.
    /// </summary>
    public const string Cron = "2-59/5 * * * *";

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
