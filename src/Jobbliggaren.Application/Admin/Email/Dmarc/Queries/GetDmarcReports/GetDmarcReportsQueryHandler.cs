using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Email.Dmarc.Queries.GetDmarcReports;

/// <summary>Reads the window ending now in one read of the stored reports, and lets the evaluator decide what it shows.</summary>
public sealed class GetDmarcReportsQueryHandler(IDmarcReportsSource reports, IDateTimeProvider clock)
    : IQueryHandler<GetDmarcReportsQuery, EmailDmarcDto>
{
    public async ValueTask<EmailDmarcDto> Handle(GetDmarcReportsQuery query, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var (from, to) = DmarcReportsEvaluator.WindowOf(now);
        var read = await reports.ReadAsync(from, to, DmarcReportsEvaluator.MaxSources, cancellationToken);
        return DmarcReportsEvaluator.Evaluate(read, now);
    }
}
