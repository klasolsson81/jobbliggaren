namespace Jobbliggaren.Application.UserStatus.Queries.GetJobAdStatusBatch;

/// <summary>
/// ADR 0063 — read-projektion för per-user-overlay-status på publik
/// JobAdDto-list. Innehåller endast IDs (matching-mängder) — frontend
/// lookup:ar O(1) i `Set` mot dessa innan rendering.
/// </summary>
public sealed record JobAdStatusBatchDto(
    IReadOnlyList<Guid> SavedIds,
    IReadOnlyList<Guid> AppliedIds);
