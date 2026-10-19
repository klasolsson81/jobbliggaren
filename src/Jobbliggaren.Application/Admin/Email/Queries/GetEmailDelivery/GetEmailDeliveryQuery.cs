using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.Email.Queries.GetEmailDelivery;

/// <summary>
/// The provider's sent and failed counts for one period. <see cref="Period"/> is null when the query string named no
/// period this API knows; <see cref="GetEmailDeliveryQueryValidator"/> turns that into a 400.
/// </summary>
public sealed record GetEmailDeliveryQuery(EmailDeliveryPeriod? Period) : IQuery<EmailDeliveryTotalsDto>, IAdminRequest;
