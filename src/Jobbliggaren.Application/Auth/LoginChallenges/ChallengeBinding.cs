namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>
/// What a bound challenge is for (ADR 0142 D5). The number is PERSISTED: it names the record's protector and sits
/// in its index key, so the values are explicit and never reused. Never 0 — an unset value must not name a
/// protector. 1 is not used: a login challenge is not a bound one.
/// </summary>
public enum ChallengePurpose
{
    Reauthentication = 2,
    ChangeEmail = 3,
}

/// <summary>
/// Whom a bound challenge belongs to and what it is for. The caller asserts it when it presents a code, and the
/// store refuses any other. A binding with an undefined purpose or the empty user id cannot be constructed, so the
/// store never has to answer for one.
/// </summary>
public sealed record ChallengeBinding
{
    public ChallengeBinding(ChallengePurpose purpose, Guid userId)
    {
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Not a bound challenge purpose.");

        if (userId == Guid.Empty)
            throw new ArgumentException("A bound challenge belongs to a user.", nameof(userId));

        Purpose = purpose;
        UserId = userId;
    }

    public ChallengePurpose Purpose { get; }

    public Guid UserId { get; }
}

/// <summary>A challenge for a signed-in user. It always carries a code and never a link.</summary>
public sealed record NewBoundChallenge(ChallengeId Id, string Recipient, ChallengeBinding Binding);
