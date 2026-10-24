using Jobbliggaren.Application.Admin.Email.Dmarc;

namespace Jobbliggaren.Infrastructure.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0184) — the ledger row of one message in the report mailbox, by epoch (UIDVALIDITY) and position
/// (UID): when the mailbox received it and what became of it. No sender, subject, body or filename.
/// <see cref="Attempts"/> counts the runs that claimed it and did not finish; <see cref="Retries"/> counts the caught
/// failures it was retried after.
/// </summary>
internal sealed class DmarcMailboxMessage
{
    private DmarcMailboxMessage()
    {
    }

    public long UidValidity { get; private set; }

    public long Uid { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public DmarcMessageOutcome Outcome { get; private set; }

    public short Attempts { get; private set; }

    public short Retries { get; private set; }
}
