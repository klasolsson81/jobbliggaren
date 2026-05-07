using JobbPilot.Application.Common.Abstractions;
using Mediator;

namespace JobbPilot.Application.Applications.Queries.GetPipeline;

public sealed record GetPipelineQuery : IQuery<IReadOnlyList<PipelineGroupDto>>, IAuthenticatedRequest;
