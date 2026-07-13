namespace Jobbliggaren.Application.Landing.Common;

/// <summary>
/// Publik aggregat-stats för landingpage. ADR 0064 — publik anonym
/// read-aggregat-mönster (pre-computed Redis-cache via Worker-jobb).
///
/// <para>
/// <b>Talen är NULLABLE, och det är hela poängen (CTO-bind 2026-07-13, A′).</b> Tidigare returnerade
/// en cache-miss ett hårdkodat golv (<c>ActiveCount: 40_000</c>) märkt med <see cref="IsStale"/>, och
/// landningssidan renderade det som ett faktum — en siffra ingen mätt, visad för varje anonym besökare
/// utan brasklapp. Golvets eget försvar ("vi ljuger inte uppåt") höll bara så länge den verkliga
/// korpusen råkade överstiga 40 000; en retention-sweep, en färsk miljö eller en halv ingest hade lagt
/// den under, och produktens ytterdörr hade överdrivit korpusen.
/// </para>
///
/// <para>
/// En flagga kan inte tvinga en konsument att titta på den — och den allra första konsumenten gjorde
/// det inte. <c>null</c> kan: med NRT/<c>strict</c> blir det ett KOMPILERINGSFEL att rendera ett omätt
/// tal, så typsystemet bär invarianten i stället för en granskares vaksamhet.
/// </para>
///
/// <para>
/// <b><c>0</c> och <c>null</c> är olika svar.</b> En MÄTT nolla ("inget publicerat än idag", sant kl.
/// 00:05 UTC) renderas fortfarande som 0. Bara "vi vet inte" är <c>null</c>.
/// </para>
/// </summary>
/// <param name="ActiveCount">
/// Antal JobAds med Status=Active (Status är hela avgränsningen — JobAd har ingen soft-delete-axel,
/// #821), eller <c>null</c> när värdet inte är
/// mätt (cache-miss). Aldrig ett uppskattat eller påhittat tal.
/// </param>
/// <param name="NewToday">
/// Antal JobAds med Status=Active OCH PublishedAt &gt;= dagens UTC-midnatt, eller <c>null</c> när värdet
/// inte är mätt. En mätt nolla är <c>0</c>, aldrig <c>null</c>.
/// </param>
/// <param name="IsStale">
/// <c>true</c> när värdena inte är färska (cache-miss → talen är <c>null</c>). Kvar som OPERATIV signal
/// (telemetri, partner-integrationer) — inte längre som ursäkt för att rendera en siffra vi inte mätt.
/// </param>
/// <param name="RefreshedAt">UTC-tidpunkt då Worker beräknade värdet; <c>null</c> när inget är mätt.</param>
public sealed record LandingStatsDto(
    int? ActiveCount,
    int? NewToday,
    bool IsStale,
    DateTimeOffset? RefreshedAt)
{
    /// <summary>
    /// Det ärliga icke-svaret vid cache-miss: inga tal, explicit stale. Ersätter den tidigare
    /// <c>Floor</c>-konstanten — systemet renderar aldrig en storhet det inte mätt.
    /// </summary>
    public static readonly LandingStatsDto Unknown = new(
        ActiveCount: null,
        NewToday: null,
        IsStale: true,
        RefreshedAt: null);
}
