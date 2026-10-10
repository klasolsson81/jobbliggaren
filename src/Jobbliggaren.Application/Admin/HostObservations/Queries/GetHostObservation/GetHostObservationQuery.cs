using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.HostObservations.Queries.GetHostObservation;

public sealed record GetHostObservationQuery : IQuery<HostObservationDto>, IAdminRequest;
