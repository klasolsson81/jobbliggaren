namespace Jobbliggaren.Domain.Invitations;

public readonly record struct InvitationId(Guid Value)
{
    public static InvitationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
