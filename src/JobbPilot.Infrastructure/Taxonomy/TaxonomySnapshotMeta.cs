namespace JobbPilot.Infrastructure.Taxonomy;

/// <summary>
/// ADR 0043 — enradig idempotens-markör för snapshot-seedern (mönster
/// analogt <c>__EFMigrationsHistory</c>). Lagrar vilken
/// JobTech-taxonomi-version som seedats så seedern blir både idempotent
/// (skippar om version matchar) OCH uppdaterande vid manuell
/// snapshot-regenerering + commit (version bumpas → re-seed). Undviker
/// att skriva ~2 700 rader vid varje app-start/Api-task (CTO MAP-1).
/// </summary>
internal sealed class TaxonomySnapshotMeta
{
    public const int SingletonId = 1;

    /// <summary>Fast PK = 1 (en rad). Check-constraint i config.</summary>
    public int Id { get; init; } = SingletonId;

    /// <summary>JobTech-taxonomi-version som senast seedats.</summary>
    public required string TaxonomyVersion { get; set; }

    public required DateTimeOffset SeededAt { get; set; }
}
