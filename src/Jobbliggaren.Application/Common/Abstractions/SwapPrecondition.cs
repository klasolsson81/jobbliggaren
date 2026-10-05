namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// What must still hold, on the account as <see cref="IUserAccountService.SwapConfirmedAddressAsync"/> loads it, for
/// the swap to go ahead. It is checked on the loaded instance, whose concurrency stamp then guards the first write, so
/// a change that lands after the load fails that write (Fowler's Optimistic Offline Lock).
/// </summary>
public abstract record SwapPrecondition
{
    private SwapPrecondition()
    {
    }

    /// <summary>Self-service: the session holder proved both inboxes, so nothing about the current address is expected.</summary>
    public static SwapPrecondition None { get; } = new Unconditional();

    /// <summary>
    /// An administrator-initiated change (#1975): the account must still hold the address the change was started from,
    /// and must not hold Admin.
    /// </summary>
    public static SwapPrecondition AdminInitiated(ExpectedCurrentAddress expected) =>
        new FromAdministrator(expected ?? throw new ArgumentNullException(nameof(expected)));

    public sealed record Unconditional : SwapPrecondition;

    public sealed record FromAdministrator(ExpectedCurrentAddress Expected) : SwapPrecondition;
}

/// <summary>
/// The address an administrator-initiated change was started from, kept as its fingerprint (#1975): the pending change
/// holds this and never the address (Art. 5(1)(c)). The change's store produces it and the account service compares
/// it; nothing else reads it.
/// </summary>
public sealed record ExpectedCurrentAddress(string Fingerprint)
{
    public override string ToString() => "ExpectedCurrentAddress(redacted)";
}
