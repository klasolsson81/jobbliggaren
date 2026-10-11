namespace Jobbliggaren.Application.Common.Notifications;

/// <summary>
/// The one rule for how fast notification mail may go out (ADR 0161, #2106). Every sender of
/// notification mail asks a <see cref="MailPacingRun"/> built from a rule whether the next mail may
/// go, and leaves a refused row in its committed queue for the next run. A sender never waits inside
/// its send loop. The rule is pure: it holds no state and does no I/O. The time of a recipient's last
/// mail comes from the sender's own committed rows.
/// <para>
/// The rule decides <b>when</b> a mail goes, never <b>whether</b>: each sender keeps its own consent
/// check, because the consents are separate purposes (ADR 0087 D5).
/// </para>
/// </summary>
public sealed record MailPacingRule
{
    public MailPacingRule(TimeSpan minGapPerRecipient, int maxMailsPerRun)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minGapPerRecipient, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxMailsPerRun, 1);
        MinGapPerRecipient = minGapPerRecipient;
        MaxMailsPerRun = maxMailsPerRun;
    }

    /// <summary>The shortest time between two mails to one recipient.</summary>
    public TimeSpan MinGapPerRecipient { get; }

    /// <summary>The most mails one run may send, across all recipients.</summary>
    public int MaxMailsPerRun { get; }

    /// <summary>
    /// A Toppmatch mail: no gap per recipient and at most 20 mails per run. Each recipient gets at most
    /// one mail per run. A gap
    /// above zero would hold a Top match back, and the signed consent text (ADR 0144 row 7) says top
    /// matches are mailed "direkt" (security-auditor V4, 2026-10-11).
    /// </summary>
    public static MailPacingRule TopMatch { get; } = new(TimeSpan.Zero, 20);
}
