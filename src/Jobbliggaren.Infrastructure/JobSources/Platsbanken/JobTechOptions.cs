using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Infrastructure.JobSources.Platsbanken;

/// <summary>
/// JobTech-integration konfiguration. Bindas från sektionen "JobTech" via
/// <see cref="Microsoft.Extensions.Options.IOptions{T}"/> i Api/Worker DI.
/// Validering körs vid startup (ValidateDataAnnotations + ValidateOnStart) så
/// missing config crash:ar deterministiskt.
/// </summary>
public sealed class JobTechOptions
{
    public const string SectionName = "JobTech";

    /// <summary>JobSearch base-URL (klassisk REST/JSON, BUILD.md §9.1).</summary>
    [Required, Url]
    public string JobSearchBaseUrl { get; set; } = "https://jobsearch.api.jobtechdev.se";

    /// <summary>
    /// JobStream base-URL (NDJSON long-polling, 1 req/min rate-limit per
    /// JobTech-docs 2026-05-12).
    /// </summary>
    [Required, Url]
    public string JobStreamBaseUrl { get; set; } = "https://jobstream.api.jobtechdev.se";

    /// <summary>
    /// API-key från apirequest.jobtechdev.se. Lagras i AWS Secrets Manager i
    /// staging/prod (BUILD.md §13.2). I dev kan tom värde tillåtas eftersom
    /// JobTech ofta accepterar anrop utan key (rate-limit:as hårdare).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Retention för <c>raw_payload</c>-kolumnen i dagar (ADR 0032 §8-amendment).
    /// Default 30 dagar. PurgeStaleRawPayloadsJob (P8c) null:ar raw_payload när
    /// published_at är äldre än denna tröskel.
    /// </summary>
    [Range(1, 365)]
    public int RawPayloadRetentionDays { get; set; } = 30;
}
