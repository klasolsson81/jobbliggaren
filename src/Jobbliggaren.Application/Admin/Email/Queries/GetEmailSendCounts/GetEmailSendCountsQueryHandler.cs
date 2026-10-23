using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Email.Queries.GetEmailSendCounts;

/// <summary>
/// Reads our own counts for every whole Swedish date the period touches, and asks the provider for the same period only
/// when they total zero (the gate in <see cref="EmailSendCountsEvaluator"/>). That provider read is the delivery
/// summary's own, remembered for a minute, so it adds no call of its own; its sentinel is never read here, because a
/// count above zero is its own proof.
/// </summary>
public sealed class GetEmailSendCountsQueryHandler(
    IEmailSendCountsSource counts,
    IEmailDeliveryStatisticsSource statistics,
    IDateTimeProvider clock,
    ISwedishCalendar calendar)
    : IQueryHandler<GetEmailSendCountsQuery, EmailSendCountsDto>
{
    public async ValueTask<EmailSendCountsDto> Handle(GetEmailSendCountsQuery query, CancellationToken cancellationToken)
    {
        if (query.Period is not { } period)
        {
            throw new ArgumentException("The validator refuses a query without a period.", nameof(query));
        }

        var now = clock.UtcNow;
        var from = EmailSendCountDates.FirstDateOf(period, now, calendar);
        var own = await counts.ReadAsync(from, calendar.DayOf(now), cancellationToken);
        var witness = EmailSendCountsEvaluator.NeedsWitness(own)
            ? await statistics.ReadAsync(period, cancellationToken)
            : null;

        return EmailSendCountsEvaluator.Evaluate(period, from, calendar.DayWindow(from).Start, own, witness);
    }
}
