namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Utfall från en <see cref="IJobSource.FetchSnapshotAsync"/>-iteration. Sätts
/// av implementationen via <see cref="SnapshotOutcomeRecorder"/> precis innan
/// <c>yield break</c>. Snapshot-jobbet (Application) använder utfallet för att
/// avgöra om miss-tracking ska köra (skippas vid trunkering — kan inte
/// särskilja "missing" från "trunkering" annars).
/// <para>
/// ADR 0032-amendment 2026-05-23 — snapshot är en logisk operation med utfall,
/// inte ren stream. Utfallet exponeras explicit i kontraktet (Saltzer/Schroeder
/// 1975 — explicit > implicit).
/// </para>
/// </summary>
/// <param name="ParsedTotal">Antal items som JobTech-strömmen parsade ut (inkl. skippade hits utan obligatoriska fält).</param>
/// <param name="Attempts">Antal HTTP-försök (bounded retry mot mid-stream-trunkering).</param>
/// <param name="TruncatedAndExhausted">True om bounded retry uttömdes utan komplett stream — caller måste skippa diff-baserad retention-logik.</param>
public sealed record SnapshotOutcome(
    int ParsedTotal,
    int Attempts,
    bool TruncatedAndExhausted);
