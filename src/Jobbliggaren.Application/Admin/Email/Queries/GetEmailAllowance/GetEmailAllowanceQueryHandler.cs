using Jobbliggaren.Application.Common.Abstractions;
using Mediator;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Admin.Email.Queries.GetEmailAllowance;

public sealed class GetEmailAllowanceQueryHandler(
    IEmailProviderUsageSource source,
    IOptions<EmailAllowanceOptions> options,
    ISwedishCalendar calendar) : IQueryHandler<GetEmailAllowanceQuery, EmailAllowanceDto>
{
    public async ValueTask<EmailAllowanceDto> Handle(GetEmailAllowanceQuery query, CancellationToken cancellationToken)
    {
        // Both reads run at once, inside the read client's one timeout.
        var usage = source.ReadUsageAsync(cancellationToken).AsTask();
        var plan = source.ReadPlanAsync(cancellationToken).AsTask();
        await Task.WhenAll(usage, plan);
        return EmailAllowanceEvaluator.Evaluate(await usage, await plan, options.Value, calendar);
    }
}
