using System.ComponentModel.DataAnnotations;

namespace JobbPilot.Application.JobAds.Abstractions;

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
}
