using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Infrastructure.Persistence;

public sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
