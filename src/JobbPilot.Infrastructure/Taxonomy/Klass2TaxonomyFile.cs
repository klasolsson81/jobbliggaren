using System.Text.Json.Serialization;

namespace JobbPilot.Infrastructure.Taxonomy;

/// <summary>
/// ADR 0043-amendment 2026-06-13 (Klass 2 options) — deserialiserings-form för
/// den FRUSNA <c>klass2-taxonomy.json</c> (embedded resource). Anställningsform
/// (<c>employment-type</c>) + omfattning (<c>worktime-extent</c>) är platta,
/// föräldralösa, legaldefinierade JobTech-mängder. Medvetet SEPARAT från
/// <see cref="TaxonomySnapshotFile"/>/generate.mjs (CTO BESLUT 1 Variant B):
/// generatorns aktualitets-värde är noll för en mängd som aldrig växer, och
/// separationen hindrar en framtida generate.mjs-körning från att skriva över
/// handkurerad data. Precedens: <c>occupation-name-to-ssyk-level-4.v30.json</c>
/// (migrations-immutabilitet, ADR 0067 notat 2026-06-09). Inte en Domain-/
/// Application-typ.
/// </summary>
internal sealed record Klass2TaxonomyFile
{
    /// <summary>Bumpas vid varje redigering → tvingar re-seed (kombineras med
    /// <see cref="TaxonomySnapshotFile.TaxonomyVersion"/> i seederns
    /// idempotens-nyckel).</summary>
    [JsonPropertyName("klass2Version")]
    public string Version { get; init; } = "unknown";

    [JsonPropertyName("employmentTypes")]
    public IReadOnlyList<Klass2Option> EmploymentTypes { get; init; } = [];

    [JsonPropertyName("worktimeExtents")]
    public IReadOnlyList<Klass2Option> WorktimeExtents { get; init; } = [];

    internal sealed record Klass2Option(
        [property: JsonPropertyName("conceptId")] string ConceptId,
        [property: JsonPropertyName("label")] string Label);
}
