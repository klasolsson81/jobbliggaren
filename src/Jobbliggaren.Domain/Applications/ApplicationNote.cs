using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Applications;

public sealed class ApplicationNote : Entity<ApplicationNoteId>
{
    public string Content { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    // EF Core constructor
    private ApplicationNote() { }

    private ApplicationNote(
        ApplicationNoteId id,
        string content,
        DateTimeOffset createdAt) : base(id)
    {
        Content = content;
        CreatedAt = createdAt;
    }

    internal static Result<ApplicationNote> Create(string? content, IDateTimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Result.Failure<ApplicationNote>(
                DomainError.Validation("ApplicationNote.ContentRequired", "Innehåll är obligatoriskt."));

        if (content.Length > 5000)
            return Result.Failure<ApplicationNote>(
                DomainError.Validation("ApplicationNote.ContentTooLong", "Anteckning får vara max 5 000 tecken."));

        return Result.Success(
            new ApplicationNote(ApplicationNoteId.New(), content.Trim(), clock.UtcNow));
    }

    public void SoftDelete(IDateTimeProvider clock) => DeletedAt = clock.UtcNow;
}
