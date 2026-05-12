using Ardalis.SmartEnum;

namespace JobbPilot.Domain.Invitations;

public sealed class InvitationStatus : SmartEnum<InvitationStatus>
{
    public static readonly InvitationStatus Pending = new("Pending", 1);
    public static readonly InvitationStatus Redeemed = new("Redeemed", 2);
    public static readonly InvitationStatus Expired = new("Expired", 3);
    public static readonly InvitationStatus Revoked = new("Revoked", 4);

    private InvitationStatus(string name, int value) : base(name, value) { }
}
