namespace Jobbliggaren.Domain.Matching;

public readonly record struct UserJobAdMatchId(Guid Value)
{
    public static UserJobAdMatchId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
