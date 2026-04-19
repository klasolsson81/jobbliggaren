using JobbPilot.Application.Common.Abstractions;

namespace JobbPilot.Infrastructure.Persistence;

public sealed class AnonymousCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public bool IsAuthenticated => false;
}
