namespace Jobbliggaren.Application.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0185) — the one retention rule for ingested DMARC reports: twelve calendar months from receipt
/// (Klas, 2026-09-04). Ingestion skips a message received before the cutoff and the retention job deletes everything
/// received before it, so neither can keep what the other would drop.
/// </summary>
public static class DmarcRetention
{
    public static DateTimeOffset CutoffOf(DateTimeOffset now) => now.ToUniversalTime().AddMonths(-12);

    /// <summary>
    /// When a message counts as received: the mailbox's own delivery time, never a sender-written header, and never
    /// later than the clock that reads it.
    /// </summary>
    public static DateTimeOffset ReceivedAtOf(DateTimeOffset mailboxReceivedAt, DateTimeOffset now)
    {
        var received = mailboxReceivedAt.ToUniversalTime();
        var clock = now.ToUniversalTime();
        return received < clock ? received : clock;
    }
}
