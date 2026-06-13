namespace Jobbliggaren.Infrastructure.Auth.Sessions;

public sealed class SessionStoreOptions
{
    public const string SectionName = "Session";

    public TimeSpan Ttl { get; init; } = TimeSpan.FromDays(14);
}
