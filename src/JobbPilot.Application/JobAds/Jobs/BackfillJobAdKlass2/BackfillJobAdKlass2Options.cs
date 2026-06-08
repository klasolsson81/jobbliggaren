using System.ComponentModel.DataAnnotations;

namespace JobbPilot.Application.JobAds.Jobs.BackfillJobAdKlass2;

/// <summary>
/// Konfiguration för <see cref="BackfillJobAdKlass2Job"/>. Bind:as mot
/// <c>BackfillJobAdKlass2</c>-sektionen i <c>appsettings.json</c>. Defaults
/// speglar <c>BackfillJobAdSsykOptions</c> (samma re-ingest-mekanik via
/// <see cref="Common.JobAdRefetchBackfillRunner"/>).
///
/// <para>
/// <b>PerItemDelayMs</b> — sekventiell throttle mot JobTech jobsearch-API
/// (<c>GET /ad/{id}</c>). Default 200ms ≈ 5 req/s. Vid ~44k NULL-rader (hela
/// tabellen innan re-ingest) ger det ~2,5h körnings-tid — Klas kan sänka delay
/// (snabbare lokal körning) eller MaxItemsPerRun (test-batch).
/// </para>
/// <para>
/// <b>MaxItemsPerRun</b> — defense-in-depth-cap. Default 100 000 (&gt;2× current
/// rad-antal ~44k).
/// </para>
/// </summary>
public sealed class BackfillJobAdKlass2Options
{
    public const string SectionName = "BackfillJobAdKlass2";

    [Range(0, 60_000)]
    public int PerItemDelayMs { get; set; } = 200;

    [Range(1, 1_000_000)]
    public int MaxItemsPerRun { get; set; } = 100_000;

    [Range(1, 1000)]
    public int ProgressLogEvery { get; set; } = 100;
}
