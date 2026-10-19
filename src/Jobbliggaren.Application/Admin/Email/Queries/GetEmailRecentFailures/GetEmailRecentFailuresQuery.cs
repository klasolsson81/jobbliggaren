using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.Email.Queries.GetEmailRecentFailures;

/// <summary>
/// The most recent mails the provider could not deliver in one period, with masked recipients. <see cref="Period"/> is
/// null when the query string named no period this API knows; <see cref="GetEmailRecentFailuresQueryValidator"/> turns
/// that into a 400.
/// </summary>
public sealed record GetEmailRecentFailuresQuery(EmailDeliveryPeriod? Period) : IQuery<EmailRecentFailuresDto>, IAdminRequest;
