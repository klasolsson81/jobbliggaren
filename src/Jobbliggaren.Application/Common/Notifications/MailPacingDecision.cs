namespace Jobbliggaren.Application.Common.Notifications;

/// <summary>What a <see cref="MailPacingRun"/> answers for one recipient.</summary>
public enum MailPacingDecision
{
    /// <summary>The mail may go now, and it is counted against the run.</summary>
    Admitted,

    /// <summary>The recipient's last mail is within the rule's gap. Try again next run.</summary>
    RecipientTooSoon,

    /// <summary>The run has sent its maximum. Every later recipient waits for the next run.</summary>
    RunFull,
}
