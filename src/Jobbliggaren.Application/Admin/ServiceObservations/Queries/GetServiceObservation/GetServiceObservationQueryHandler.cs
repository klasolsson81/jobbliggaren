using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Admin.ServiceObservations.Queries.GetServiceObservation;

/// <summary>
/// Reads the newest snapshot from memory and judges it against the API clock (ADR 0159). No I/O: the sources are
/// never touched by a request, so the cost of this read does not depend on who asks or how often.
/// </summary>
public sealed class GetServiceObservationQueryHandler(
    IServiceObservationReader reader,
    IDateTimeProvider clock,
    IOptions<ServiceObservationOptions> options) : IQueryHandler<GetServiceObservationQuery, ServiceObservationDto>
{
    public ValueTask<ServiceObservationDto> Handle(GetServiceObservationQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ServiceStatusEvaluator.Evaluate(reader.Current, clock.UtcNow.ToUniversalTime(), options.Value));
}
