namespace Jobbliggaren.Infrastructure.Email.Counting;

/// <summary>
/// #1981 PR 3 (ADR 0181) — the one row of <c>email_send_counting</c>: when counting began. The migration that creates
/// the table writes it with the database clock, and no code writes it afterwards, so a period that reaches further back
/// is shown as covered from this instant and never as a lower bound.
/// </summary>
internal sealed class EmailSendCounting
{
    private EmailSendCounting()
    {
    }

    public int Id { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }
}
