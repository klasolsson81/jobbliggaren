using System.ComponentModel.DataAnnotations;

namespace JobbPilot.Application.JobAds.Jobs.BackfillJobAdSsyk;

/// <summary>
/// Konfiguration för <see cref="BackfillJobAdSsykJob"/>. Bind:as mot
/// <c>BackfillJobAdSsyk</c>-sektionen i <c>appsettings.json</c>.
///
/// <para>
/// <b>PerItemDelayMs</b> — sekventiell throttle mot JobTech jobsearch-API.
/// Default 200ms ger ~5 req/s = ~117 min för 35k rader. Empirisk verifiering
/// 2026-05-24: <c>GET /ad/{id}</c> svarar i ~16ms utan rate-limit-headers
/// exponerade. Konservativt val mot framtida throttling-policy från
/// arbetsformedlingen.se (architect-rond 2026-05-24).
/// </para>
/// <para>
/// <b>MaxItemsPerRun</b> — defense-in-depth-cap så en panik-kick inte
/// drar ohanterligt många requests. Default 100 000 (>2× current NULL-count
/// 35k); Klas kan sänka för testning eller om körnings-tids-fönstret är trångt.
/// </para>
/// </summary>
public sealed class BackfillJobAdSsykOptions
{
    public const string SectionName = "BackfillJobAdSsyk";

    [Range(0, 60_000)]
    public int PerItemDelayMs { get; set; } = 200;

    [Range(1, 1_000_000)]
    public int MaxItemsPerRun { get; set; } = 100_000;

    [Range(1, 1000)]
    public int ProgressLogEvery { get; set; } = 100;
}
