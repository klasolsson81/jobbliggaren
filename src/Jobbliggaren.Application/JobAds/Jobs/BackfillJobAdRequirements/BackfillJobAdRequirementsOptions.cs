using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdRequirements;

/// <summary>
/// Konfiguration för <see cref="BackfillJobAdRequirementsJob"/>. Bind:as mot
/// <c>BackfillJobAdRequirements</c>-sektionen i <c>appsettings.json</c>. Defaults
/// speglar <see cref="Common.JobAdRefetchBackfillRunner"/>-paritet
/// (<c>BackfillJobAdKlass2Options</c> / <c>BackfillJobAdSsykOptions</c>).
///
/// <para>
/// <b>PerItemDelayMs</b> — sekventiell throttle mot JobTech jobsearch-API
/// (<c>GET /ad/{id}</c>). Default 200ms ≈ 5 req/s. F4-4b re-hämtar HELA tabellen
/// (alla rader saknar <c>must_have</c> tills körningen skett, till skillnad mot
/// Klass2:s ~21% NULL) → ~54k rader ger ~3h körnings-tid. Klas kan sänka delay
/// (snabbare lokal körning) eller MaxItemsPerRun (test-batch).
/// </para>
/// <para>
/// <b>MaxItemsPerRun</b> — defense-in-depth-cap. Default 100 000 (&gt;current
/// rad-antal ~54k).
/// </para>
/// </summary>
public sealed class BackfillJobAdRequirementsOptions
{
    public const string SectionName = "BackfillJobAdRequirements";

    [Range(0, 60_000)]
    public int PerItemDelayMs { get; set; } = 200;

    [Range(1, 1_000_000)]
    public int MaxItemsPerRun { get; set; } = 100_000;

    [Range(1, 1000)]
    public int ProgressLogEvery { get; set; } = 100;
}
