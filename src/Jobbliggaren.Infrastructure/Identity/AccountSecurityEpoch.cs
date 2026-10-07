namespace Jobbliggaren.Infrastructure.Identity;

public sealed class AccountSecurityEpoch
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;

    public long Value { get; private set; }

    internal long Advance() => Value = checked(Value + 1);
}
