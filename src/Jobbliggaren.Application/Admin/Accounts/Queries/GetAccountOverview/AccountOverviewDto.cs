namespace Jobbliggaren.Application.Admin.Accounts.Queries.GetAccountOverview;

public sealed record AccountOverviewDto(
    DateTimeOffset SampledAt,
    AccountStatusCounts Counts,
    AccountNewRegistrations NewAccounts,
    IReadOnlyList<AccountRegistrationDay> Days);

public sealed record AccountNewRegistrations(
    AccountRegistrationPeriod Today,
    AccountRegistrationPeriod Yesterday,
    AccountRegistrationPeriod Last7Days,
    AccountRegistrationPeriod Last30Days);

public sealed record AccountRegistrationPeriod(int Count, DateTimeOffset From, DateTimeOffset Before);
