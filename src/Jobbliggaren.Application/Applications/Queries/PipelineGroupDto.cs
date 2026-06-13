namespace Jobbliggaren.Application.Applications.Queries;

public sealed record PipelineGroupDto(
    string Status,
    int Count,
    IReadOnlyList<ApplicationDto> Applications);
