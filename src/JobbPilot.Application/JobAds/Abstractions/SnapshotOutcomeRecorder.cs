namespace JobbPilot.Application.JobAds.Abstractions;

/// <summary>
/// Single-write recorder för <see cref="SnapshotOutcome"/>. Caller skapar
/// instansen och passerar in i <see cref="IJobSource.FetchSnapshotAsync"/>;
/// implementationen kallar <see cref="Record"/> exakt en gång precis innan
/// <c>yield break</c>. Caller läser <see cref="Outcome"/> efter att
/// <c>await foreach</c> avslutats.
/// <para>
/// Single-write-invarianten skyddar mot förvirring vid bugg där en
/// implementation av misstag försöker rapportera flera utfall för samma run —
/// fail-fast (CLAUDE.md §3.4 — exceptions för oväntade tillstånd).
/// </para>
/// </summary>
public sealed class SnapshotOutcomeRecorder
{
    public SnapshotOutcome? Outcome { get; private set; }

    public void Record(SnapshotOutcome outcome)
    {
        if (Outcome is not null)
            throw new InvalidOperationException(
                "SnapshotOutcome har redan registrerats för denna iteration. " +
                "Varje FetchSnapshotAsync-anrop får exakt en Record-anrop.");
        Outcome = outcome;
    }
}
