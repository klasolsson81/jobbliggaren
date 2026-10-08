using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.GetAccountOverview;

public sealed class GetAccountOverviewQueryHandler(
    IAccountDirectory directory,
    IDateTimeProvider clock,
    ISwedishCalendar calendar) : IQueryHandler<GetAccountOverviewQuery, AccountOverviewDto>
{
    private const int TrendDays = 90;

    public async ValueTask<AccountOverviewDto> Handle(
        GetAccountOverviewQuery query, CancellationToken cancellationToken)
    {
        var sampledAt = clock.UtcNow.ToUniversalTime();
        var today = calendar.DayOf(sampledAt);
        var windows = Enumerable.Range(0, TrendDays)
            .Select(index => calendar.DayWindow(today.AddDays(index - TrendDays + 1)))
            .Select(window => window.Day == today ? window with { End = sampledAt } : window)
            .ToArray();
        var result = await directory.GetOverviewAsync(windows, cancellationToken);
        var todayStart = windows[^1].Start;

        AccountRegistrationPeriod Period(int days) => new(
            result.Days.Where(day => day.Date >= today.AddDays(1 - days)).Sum(day => day.NewAccounts),
            windows[^days].Start,
            sampledAt);

        var newAccounts = new AccountNewRegistrations(
            Period(1),
            new AccountRegistrationPeriod(result.Days.Single(day => day.Date == today.AddDays(-1)).NewAccounts,
                windows[^2].Start, todayStart),
            Period(7),
            Period(30));
        return new AccountOverviewDto(sampledAt, result.Counts, newAccounts, result.Days);
    }
}
