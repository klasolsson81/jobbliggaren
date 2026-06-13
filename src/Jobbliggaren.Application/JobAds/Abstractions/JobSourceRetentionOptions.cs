using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Retention-konfig för externa JobAd-källor. Application-lagret äger
/// kontraktet; Infrastructure (<c>JobTechOptions</c>) bidrar källvärdet
/// via en typed-options-aliasing i DI (CTO-rond 2026-05-13 — Application
/// får inte bero på Infrastructure-specifika options-typer).
///
/// <para>
/// <b>Notera:</b> denna typ binds i <c>AddJobSources</c> mot
/// <c>JobTechOptions.SectionName</c> ("JobTech")-sektionen — inte mot någon
/// egen sektion. Property-namnen mellan typerna måste därför vara identiska
/// (för närvarande <c>RawPayloadRetentionDays</c>). Ingen
/// <c>SectionName</c>-konstant exponeras eftersom det vore vilseledande.
/// </para>
/// </summary>
public sealed class JobSourceRetentionOptions
{
    /// <summary>
    /// Dagar att behålla <c>raw_payload</c> efter <c>published_at</c>.
    /// Default 30 (ADR 0032 §8-amendment 2026-05-12). Range-validerat.
    /// </summary>
    [Range(1, 365)]
    public int RawPayloadRetentionDays { get; set; } = 30;

    /// <summary>
    /// Antal konsekutiva snapshot-runs där samma ExternalId måste saknas
    /// innan retention-jobbet arkiverar (defense-in-depth mot snapshot-
    /// trunkering — ADR 0032-amendment 2026-05-23 + 2026-05-16). Default 3
    /// (CTO-rond 2026-05-23). Range-validerat.
    /// </summary>
    [Range(1, 30)]
    public int SnapshotMissThreshold { get; set; } = 3;

    /// <summary>
    /// Absolut floor för antal parsade snapshot-items innan miss-tracking
    /// får uppdateras (CTO-rond 2026-05-23 Q5 = 30 000). Snapshot under
    /// detta antal är "uppenbart trasig" oavsett 7-dygns-historik och miss-
    /// tracking skippas helt → ingen falsk arkivering kan inträffa.
    /// Range-validerat.
    /// </summary>
    [Range(1, 1_000_000)]
    public int SnapshotAbsoluteFloor { get; set; } = 30_000;

    /// <summary>
    /// Relativ floor: <c>ParsedTotal &lt; RelativeFloorRatio × max_observed_7d</c>
    /// → miss-tracking skippas (CTO-rond 2026-05-23 Q5 = 0.80). Skydd mot
    /// dramatisk regression mot rullande 7-dagars-max. <c>max_observed_7d</c>
    /// härleds från audit-historik (System.JobAdsSynced records). Range-validerat.
    /// </summary>
    [Range(0.0, 1.0)]
    public double SnapshotRelativeFloorRatio { get; set; } = 0.80;

    /// <summary>
    /// Post-archive circuit-breaker (CTO-rond 2026-05-23 H1 +
    /// security-auditor 2026-05-23). Om retention-jobbets <c>candidates / active</c>
    /// (Platsbanken Active) överstiger detta värde: ABORT före <c>ExecuteUpdate</c>,
    /// audit-rad med <c>ThresholdAborted=true</c> + <c>AbortReason="max-archive-pct-exceeded"</c>,
    /// jobbet kastar <see cref="Jobbliggaren.Domain.Common.DomainException"/> för
    /// fail-loud (Hangfire-retry surfar via dashboard + CloudWatch).
    /// <para>
    /// Default 0.25 = 25 %. Räkne-exempel: korpus 56k aktiva, förväntad första-
    /// körning ~10k archive ≈ 18 % &lt; 25 % → släpps igenom. Steady-state ~0-2 %.
    /// Operator-ofog (SnapshotMissThreshold=1) skulle ge 50 %+ → stoppas.
    /// Range-validerat (CTO motiverade 0.25 framför 0.20 — noll marginal mot
    /// förväntad första-körning gör 0.20 falsk-positiv-känslig).
    /// </para>
    /// </summary>
    [Range(0.05, 1.00)]
    public double MaxArchivePctPerRun { get; set; } = 0.25;
}
