using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.Email.Queries.GetEmailAllowance;

public sealed record GetEmailAllowanceQuery : IQuery<EmailAllowanceDto>, IAdminRequest;
