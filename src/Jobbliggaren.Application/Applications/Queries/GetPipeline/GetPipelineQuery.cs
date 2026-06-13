using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Applications.Queries.GetPipeline;

public sealed record GetPipelineQuery : IQuery<IReadOnlyList<PipelineGroupDto>>, IAuthenticatedRequest;
