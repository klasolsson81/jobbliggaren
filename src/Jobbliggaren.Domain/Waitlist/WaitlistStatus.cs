using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Waitlist;

public sealed class WaitlistStatus : SmartEnum<WaitlistStatus>
{
    public static readonly WaitlistStatus Pending = new("Pending", 1);
    public static readonly WaitlistStatus Approved = new("Approved", 2);
    public static readonly WaitlistStatus Rejected = new("Rejected", 3);

    private WaitlistStatus(string name, int value) : base(name, value) { }
}
