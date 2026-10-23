using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.Email.Queries.GetEmailSendCounts;

/// <summary>
/// Our own counts per mail kind for one period (#1981 PR 3, ADR 0181). <see cref="Period"/> is null when the query
/// string named no period this API knows; <see cref="GetEmailSendCountsQueryValidator"/> turns that into a 400.
/// </summary>
public sealed record GetEmailSendCountsQuery(EmailDeliveryPeriod? Period) : IQuery<EmailSendCountsDto>, IAdminRequest;
