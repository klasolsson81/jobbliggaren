using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.GetAccountOverview;

public sealed record GetAccountOverviewQuery : IQuery<AccountOverviewDto>, IAdminRequest;
