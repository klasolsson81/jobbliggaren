namespace Jobbliggaren.Application.Common.Notifications;

/// <summary>
/// One run's use of a <see cref="MailPacingRule"/>: it counts the mails it has admitted. Create one per
/// run and never share it, since the count is the run's.
/// </summary>
public sealed class MailPacingRun(MailPacingRule rule)
{
    private int _admitted;

    /// <summary>How many mails this run has admitted.</summary>
    public int Admitted => _admitted;

    /// <summary>
    /// Decides whether one mail to a recipient may go now. <paramref name="lastMailToRecipientAt"/> is
    /// the time of the recipient's last mail from this sender, or null if there is none. An admitted
    /// mail is counted at once, so ask only when the mail is about to be sent.
    /// </summary>
    public MailPacingDecision Decide(DateTimeOffset? lastMailToRecipientAt, DateTimeOffset now)
    {
        if (_admitted >= rule.MaxMailsPerRun)
            return MailPacingDecision.RunFull;

        if (lastMailToRecipientAt is { } last && now - last < rule.MinGapPerRecipient)
            return MailPacingDecision.RecipientTooSoon;

        _admitted++;
        return MailPacingDecision.Admitted;
    }
}
