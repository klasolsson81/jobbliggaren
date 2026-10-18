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
        var read = await source.ReadUsageAsync(cancellationToken);
        return EmailAllowanceEvaluator.Evaluate(read, options.Value, calendar);
    }
}
