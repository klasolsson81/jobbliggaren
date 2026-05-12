namespace JobbPilot.Application.JobAds.Commands.SyncPlatsbankenSnapshot;

/// <summary>
/// Aggregerad statistik från en snapshot-import. Returneras till admin-endpoint
/// och loggas via <c>JobAdsSyncedDomainEvent</c> (ADR 0032 §8, wire:as i P8c).
/// </summary>
public sealed record SyncPlatsbankenSnapshotResult(
    int FetchedCount,
    int AddedCount,
    int UpdatedCount,
    int SkippedCount,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
