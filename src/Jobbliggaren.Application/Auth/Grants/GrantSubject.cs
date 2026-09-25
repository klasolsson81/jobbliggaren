using Jobbliggaren.Application.Auth.ExternalLogins;

namespace Jobbliggaren.Application.Auth.Grants;

/// <summary>
/// What a grant is for. The number is PERSISTED in the record, so the values are explicit and never reused:
/// a record written by an older build must not decode as a different purpose. Never 0 — an absent or zeroed
/// field must not decode at all.
/// </summary>
public enum GrantPurpose
{
    LoginComplete = 1,
    Reauthentication = 2,
    ChangeEmail = 3,
    LoginCompleteExternal = 4,
}

/// <summary>What a grant binds and carries (ADR 0142 D3). A closed set, one variant per purpose.</summary>
public abstract record GrantSubject
{
    private GrantSubject() { }

    public abstract GrantPurpose Purpose { get; }

    /// <summary>
    /// The address a login challenge proved, waiting for the terms to be accepted. Nothing here is asserted
    /// by the caller: <c>complete</c> holds only the token, so the address is what the redemption hands
    /// back, and the account is created on it whatever the browser's cookie says (ADR 0142 D1).
    /// </summary>
    public sealed record LoginComplete(string ProvenEmail) : GrantSubject
    {
        public override GrantPurpose Purpose => GrantPurpose.LoginComplete;
    }

    /// <summary>
    /// A signed-in user proved the account's own inbox again (ADR 0142 D5). It carries no address: the caller
    /// asserts its own user id, and a grant issued to another user is refused.
    /// </summary>
    public sealed record Reauthentication(Guid UserId) : GrantSubject
    {
        public override GrantPurpose Purpose => GrantPurpose.Reauthentication;
    }

    /// <summary>
    /// A signed-in user proved the inbox of the address the account is moving to (ADR 0142 D5). The caller
    /// asserts both halves, so the grant cannot be replayed against another address than the proven one.
    /// </summary>
    public sealed record ChangeEmail(Guid UserId, string NewEmail) : GrantSubject
    {
        public override GrantPurpose Purpose => GrantPurpose.ChangeEmail;
    }

    /// <summary>
    /// A provider-verified address with no account, waiting for the terms (ADR 0142 D3, D8), and the external
    /// login to attach once the account exists. Bearer-bound like <see cref="LoginComplete"/>: no row is written
    /// before the acceptance, so the identity waits here and expires with the grant.
    /// </summary>
    public sealed record LoginCompleteExternal(
        string ProvenEmail, ExternalProviderKey Provider, ExternalSubject Subject) : GrantSubject
    {
        public override GrantPurpose Purpose => GrantPurpose.LoginCompleteExternal;
    }
}

/// <summary>
/// What the caller asserts when it redeems a grant. Factories only, so redeeming without a binding is a
/// deliberate act on a purpose that allows it, never an argument someone left out.
/// </summary>
public sealed record GrantAssertion
{
    private GrantAssertion(IReadOnlyList<GrantPurpose> purposes, GrantSubject? binding)
    {
        Purposes = purposes;
        Binding = binding;
    }

    /// <summary>The purposes the stored grant may have; one, except for a bearer redemption of several.</summary>
    public IReadOnlyList<GrantPurpose> Purposes { get; }

    /// <summary>The binding the stored subject must EQUAL, or null for a bearer-bound purpose.</summary>
    public GrantSubject? Binding { get; }

    // Value equality over the purposes, not the list's reference: two assertions a caller builds alike are equal.
    public bool Equals(GrantAssertion? other) =>
        other is not null && Purposes.SequenceEqual(other.Purposes) && Equals(Binding, other.Binding);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var purpose in Purposes)
            hash.Add(purpose);
        hash.Add(Binding);
        return hash.ToHashCode();
    }

    /// <summary>The caller knows the binding and asserts it; the store refuses anything not equal to it.</summary>
    public static GrantAssertion Of(GrantSubject binding) => new([binding.Purpose], binding);

    /// <summary>
    /// The caller knows only the token, because carrying the binding out is the purpose's whole job. Every purpose
    /// named must be declared bearer-bound, so a new purpose is caller-asserted until someone says otherwise. A
    /// caller that accepts several — <c>complete</c> takes a code's grant or a provider's — names them all, and the
    /// store redeems the token once.
    /// </summary>
    public static GrantAssertion Bearer(GrantPurpose first, params GrantPurpose[] more)
    {
        GrantPurpose[] purposes = [first, .. more];
        foreach (var purpose in purposes)
        {
            if (!IsBearerBound(purpose))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(first), purpose, "This purpose's binding is caller-asserted; use GrantAssertion.Of.");
            }
        }

        return new(purposes.Distinct().ToArray(), null);
    }

    private static bool IsBearerBound(GrantPurpose purpose) => purpose switch
    {
        GrantPurpose.LoginComplete or GrantPurpose.LoginCompleteExternal => true,
        _ => false,
    };
}
