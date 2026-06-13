namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// STEG 6 Approach B (2026-05-24) — konfigurations-binding för
/// <see cref="IOccupationSynonymExpander"/>. Bind:as mot
/// <c>SearchSynonyms</c>-sektionen i <c>appsettings.json</c>.
///
/// <para>
/// Mapping-key är fritext-söktermen (lowercase). Value är array av JobTech
/// occupation-concept_ids (verifierade mot
/// <c>taxonomy.api.jobtechdev.se/v1/taxonomy/suggesters/autocomplete</c>
/// 2026-05-24).
/// </para>
/// </summary>
public sealed class SearchSynonymsOptions
{
    public const string SectionName = "SearchSynonyms";

    /// <summary>
    /// Fritext → occupation-concept_ids. Key är lowercase. Default tom.
    /// Bindas från <c>appsettings.json</c>:
    /// <code>
    /// "SearchSynonyms": {
    ///   "Occupations": {
    ///     "systemutvecklare": [ "fg7B_yov_smw", "rQds_YGd_quU", ... ]
    ///   }
    /// }
    /// </code>
    /// </summary>
    public Dictionary<string, string[]> Occupations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
