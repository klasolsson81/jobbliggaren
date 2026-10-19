using Mediator;

namespace Jobbliggaren.Application.Admin.Email.Queries.GetEmailRecentFailures;

/// <summary>
/// Reads the failures with the same period's statistics and the sentinel, because an empty list is trusted only beside
/// both (<see cref="EmailDeliveryEvaluator.EvaluateFailures"/>).
/// </summary>
public sealed class GetEmailRecentFailuresQueryHandler(
    IEmailRecentFailuresSource failures,
    IEmailDeliveryStatisticsSource statistics) : IQueryHandler<GetEmailRecentFailuresQuery, EmailRecentFailuresDto>
{
    public async ValueTask<EmailRecentFailuresDto> Handle(GetEmailRecentFailuresQuery query, CancellationToken cancellationToken)
    {
        if (query.Period is not { } period)
        {
            throw new ArgumentException("The validator refuses a query without a period.", nameof(query));
        }

        // All three reads run at once, inside the read client's one timeout.
        var list = failures.ReadAsync(period, cancellationToken).AsTask();
        var read = statistics.ReadAsync(period, cancellationToken).AsTask();
        var sentinel = statistics.ReadSentinelAsync(cancellationToken).AsTask();
        await Task.WhenAll(list, read, sentinel);
        return EmailDeliveryEvaluator.EvaluateFailures(period, await list, await read, await sentinel);
    }
}
