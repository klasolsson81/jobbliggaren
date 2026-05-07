namespace JobbPilot.Application.Applications.Queries;

public sealed record NoteDto(
    Guid Id,
    string Content,
    DateTimeOffset CreatedAt);
