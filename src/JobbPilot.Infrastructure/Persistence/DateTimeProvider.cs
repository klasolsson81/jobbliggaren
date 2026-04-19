using JobbPilot.Domain.Common;

namespace JobbPilot.Infrastructure.Persistence;

public sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
