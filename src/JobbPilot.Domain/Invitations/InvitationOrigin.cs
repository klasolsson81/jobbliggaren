using Ardalis.SmartEnum;

namespace JobbPilot.Domain.Invitations;

public sealed class InvitationOrigin : SmartEnum<InvitationOrigin>
{
    public static readonly InvitationOrigin DirectInvite = new("DirectInvite", 1);
    public static readonly InvitationOrigin WaitlistApproved = new("WaitlistApproved", 2);

    private InvitationOrigin(string name, int value) : base(name, value) { }
}
