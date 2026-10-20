using Mediator;

namespace Jobbliggaren.Application.Admin.Email.Queries.GetEmailDelivery;

public sealed class GetEmailDeliveryQueryHandler(IEmailDeliveryStatisticsSource statistics)
    : IQueryHandler<GetEmailDeliveryQuery, EmailDeliveryTotalsDto>
{
    public async ValueTask<EmailDeliveryTotalsDto> Handle(GetEmailDeliveryQuery query, CancellationToken cancellationToken)
    {
        if (query.Period is not { } period)
        {
            throw new ArgumentException("The validator refuses a query without a period.", nameof(query));
        }

        // Both reads run at once, inside the read client's one timeout.
        var read = statistics.ReadAsync(period, cancellationToken).AsTask();
        var sentinel = statistics.ReadSentinelAsync(cancellationToken).AsTask();
        await Task.WhenAll(read, sentinel);
        return EmailDeliveryEvaluator.EvaluateTotals(period, await read, await sentinel);
    }
}
