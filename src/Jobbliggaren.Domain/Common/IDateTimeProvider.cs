namespace Jobbliggaren.Domain.Common;

public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
