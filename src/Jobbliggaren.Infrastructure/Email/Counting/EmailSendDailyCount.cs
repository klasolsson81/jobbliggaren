using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Infrastructure.Email.Counting;

/// <summary>
/// #1981 PR 3 (ADR 0180) — one row of <c>email_send_daily_counts</c>: how many attempts of one mail kind came to one
/// outcome on one Swedish civil date. No recipient, no user and no time of day. Infrastructure-internal (the ADR 0172 D3
/// form): written only by the tally's upsert, so it is mapped for the migration schema and is never a <c>DbSet</c> on
/// <c>IAppDbContext</c>.
/// </summary>
internal sealed class EmailSendDailyCount
{
    private EmailSendDailyCount()
    {
    }

    public DateOnly Day { get; private set; }

    public EmailKind Kind { get; private set; }

    public EmailSendOutcome Outcome { get; private set; }

    public int Attempts { get; private set; }
}
