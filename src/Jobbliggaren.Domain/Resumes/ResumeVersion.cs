using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Resumes;

public sealed class ResumeVersion : Entity<ResumeVersionId>
{
    public ResumeVersionKind Kind { get; private set; } = null!;
    public ResumeContent Content { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    // EF Core constructor
    private ResumeVersion() { }

    private ResumeVersion(
        ResumeVersionId id,
        ResumeVersionKind kind,
        ResumeContent content,
        DateTimeOffset now) : base(id)
    {
        Kind = kind;
        Content = content;
        CreatedAt = now;
        UpdatedAt = now;
    }

    internal static ResumeVersion CreateMaster(ResumeContent content, IDateTimeProvider clock)
    {
        var now = clock.UtcNow;
        return new ResumeVersion(ResumeVersionId.New(), ResumeVersionKind.Master, content, now);
    }

    internal void UpdateContent(ResumeContent content, IDateTimeProvider clock)
    {
        Content = content;
        UpdatedAt = clock.UtcNow;
    }

    internal void SoftDelete(IDateTimeProvider clock)
    {
        if (DeletedAt.HasValue) return;
        DeletedAt = clock.UtcNow;
    }
}
