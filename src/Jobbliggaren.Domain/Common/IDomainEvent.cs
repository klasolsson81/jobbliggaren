namespace Jobbliggaren.Domain.Common;

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
