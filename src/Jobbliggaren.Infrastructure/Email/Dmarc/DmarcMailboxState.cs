using Jobbliggaren.Application.Admin.Email.Dmarc;

namespace Jobbliggaren.Infrastructure.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0183) — the one row of <c>dmarc_mailbox_state</c>: the latest run, the latest success, and the
/// cursor (epoch and last position claimed). Never seeded: no row is the true state "never attempted".
/// </summary>
internal sealed class DmarcMailboxState
{
    private DmarcMailboxState()
    {
    }

    public int Id { get; private set; }

    public DateTimeOffset LastAttemptAt { get; private set; }

    public DmarcRunOutcome LastOutcome { get; private set; }

    public DateTimeOffset? LastSuccessAt { get; private set; }

    public long? UidValidity { get; private set; }

    public long? LastUid { get; private set; }
}
