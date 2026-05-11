using JobbPilot.Application.Common.Abstractions;

namespace JobbPilot.Infrastructure.Persistence;

public sealed class AnonymousCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public bool IsAuthenticated => false;
    public string? Jti => null;
    public string? Email => null;
    public SessionId? SessionId => null;

    public bool IsInRole(string role) => false;
}
