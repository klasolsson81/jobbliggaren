using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// #1981 PR 3 (ADR 0180) — our own counts of the attempts to hand a mail to the provider, per Swedish civil date, mail
/// kind and outcome. Application defines the port; Infrastructure implements it. Its one consumer is the per-kind query
/// handler, which an architecture test pins.
/// <para>
/// A database failure is an exception, as on every database read. So is a stored value this release does not know,
/// which only a later release's writer followed by an image rollback can leave, and a missing recording start, which
/// only a hand edit can leave: a read that counted around either would understate what it shows.
/// </para>
/// </summary>
public interface IEmailSendCountsSource
{
    /// <summary>Every count from <paramref name="firstDay"/> through <paramref name="lastDay"/>, both inclusive.</summary>
    ValueTask<EmailSendCountsRead> ReadAsync(DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken);
}

/// <summary>One stored count: the attempts of one kind that came to one outcome on one date.</summary>
public sealed record EmailSendCountRow
{
    public EmailSendCountRow(DateOnly day, EmailKind kind, EmailSendOutcome outcome, int attempts)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(outcome))
        {
            throw new ArgumentException("A count carries a kind and an outcome this release knows.", nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempts);
        Day = day;
        Kind = kind;
        Outcome = outcome;
        Attempts = attempts;
    }

    public DateOnly Day { get; }

    public EmailKind Kind { get; }

    public EmailSendOutcome Outcome { get; }

    public int Attempts { get; }
}

/// <summary>What one read of our own counts ends in: a closed set.</summary>
public abstract record EmailSendCountsRead
{
    private EmailSendCountsRead() { }

    /// <summary>
    /// The counts in the window, and when counting began. A date with no row had no attempt of that kind and outcome,
    /// from <see cref="RecordingStartedAt"/> on.
    /// </summary>
    public sealed record Observed(IReadOnlyList<EmailSendCountRow> Rows, DateTimeOffset RecordingStartedAt)
        : EmailSendCountsRead;

    /// <summary>This deployment hands no mail to a provider, so nothing is counted.</summary>
    public sealed record NotConfigured : EmailSendCountsRead;
}
