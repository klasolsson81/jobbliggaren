using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.ServiceObservations.Queries.GetServiceObservation;

public sealed record GetServiceObservationQuery : IQuery<ServiceObservationDto>, IAdminRequest;
