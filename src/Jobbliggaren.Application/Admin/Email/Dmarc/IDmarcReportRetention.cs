namespace Jobbliggaren.Application.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0183) — deletes what was received before the retention cutoff: reports (their records with them)
/// and ledger rows. Application defines the port; Infrastructure implements it. Its one consumer is the retention job.
/// The state row holds no personal data and is never deleted.
/// </summary>
public interface IDmarcReportRetention
{
    ValueTask<DmarcRetentionResult> DeleteReceivedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}

/// <summary>How many rows one retention run deleted.</summary>
public sealed record DmarcRetentionResult(int Reports, int Messages);
