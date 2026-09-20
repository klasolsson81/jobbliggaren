using System.Diagnostics.CodeAnalysis;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>Which credentials a challenge record carries. Chosen by the plan, minted by the store.</summary>
public enum ChallengeCredentials
{
    /// <summary>Neither: the mail carries no credential (closed registration, pending deletion).</summary>
    None,

    /// <summary>A link only: an existing account whose code budget is spent (Klas, 2026-09-19, (A)).</summary>
    LinkOnly,

    /// <summary>A code and a link: an existing account within its code budget.</summary>
    CodeAndLink,
}

/// <summary>
/// A challenge to write. <see cref="ReplacesLiveChallenge"/> is true when the request's code budget admitted
/// the mint: only then does the new record burn the address's previous one. It is decided on the request
/// path, which knows nothing of the account, so whether an earlier challenge was burned can never tell a
/// prober whether the address has one (security-auditor Q-S1, 2026-09-19). <see cref="Recipient"/> is the
/// address the challenge is addressed to — the account's own spelling when a row holds the address, the
/// submitted one otherwise — and so the address a proof of this record proves.
/// </summary>
public sealed record NewLoginChallenge(
    ChallengeId Id,
    string Recipient,
    ChallengeCredentials Credentials,
    bool ReplacesLiveChallenge);

/// <summary>What the store minted for a challenge, for the mail to carry. Null where not asked for.</summary>
public sealed record IssuedCredentials(LoginCode? Code, LoginLinkToken? Link);

/// <summary>A consumed challenge's proof: the inbox of <see cref="ProvenEmail"/> received it.</summary>
public sealed record LoginChallengeProof(string ProvenEmail);

/// <summary>What a presented code did to its challenge. A missing and an expired record are one state.</summary>
public enum ChallengeOutcome
{
    Verified,
    Wrong,
    Burned,
    Missing,
}

/// <summary>
/// The answer to a presented code. Constructed only through its factories, so <see cref="Proof"/> is non-null
/// exactly when the outcome is <see cref="ChallengeOutcome.Verified"/> and <see cref="AttemptsRemaining"/> is
/// positive exactly when it is <see cref="ChallengeOutcome.Wrong"/>.
/// </summary>
public sealed record ChallengeVerdict
{
    private ChallengeVerdict(ChallengeOutcome outcome, LoginChallengeProof? proof, int attemptsRemaining)
    {
        Outcome = outcome;
        Proof = proof;
        AttemptsRemaining = attemptsRemaining;
    }

    public ChallengeOutcome Outcome { get; }

    public LoginChallengeProof? Proof { get; }

    /// <summary>Wrong codes the challenge still absorbs before its code is burned; 0 unless Wrong.</summary>
    public int AttemptsRemaining { get; }

    [MemberNotNullWhen(true, nameof(Proof))]
    public bool IsVerified => Outcome == ChallengeOutcome.Verified;

    public static ChallengeVerdict Verified(LoginChallengeProof proof) =>
        new(ChallengeOutcome.Verified, proof ?? throw new ArgumentNullException(nameof(proof)), 0);

    public static ChallengeVerdict Wrong(int attemptsRemaining)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptsRemaining, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(attemptsRemaining, LoginChallengePolicy.MaxAttempts - 1);
        return new(ChallengeOutcome.Wrong, null, attemptsRemaining);
    }

    public static ChallengeVerdict Burned { get; } = new(ChallengeOutcome.Burned, null, 0);

    public static ChallengeVerdict Missing { get; } = new(ChallengeOutcome.Missing, null, 0);
}

/// <summary>
/// The login challenge's store (ADR 0142 D1). It exposes the invariants, never the verbs: minting, hashing,
/// protecting and comparing all live in the adapter, so no caller can assemble a non-atomic read-then-write.
/// </summary>
public interface ILoginChallengeStore
{
    /// <summary>
    /// Writes a challenge that lives <see cref="LoginChallengePolicy.ChallengeTtl"/>, minting the credentials
    /// <see cref="NewLoginChallenge.Credentials"/> asks for. When
    /// <see cref="NewLoginChallenge.ReplacesLiveChallenge"/> is set, the address's previous replacing
    /// challenge is burned.
    /// </summary>
    Task<IssuedCredentials> PutAsync(NewLoginChallenge challenge, CancellationToken ct);

    /// <summary>
    /// Presents a code. The attempt counter is incremented BEFORE the compare; a hit deletes the record, so a
    /// second consume finds it missing; the <see cref="LoginChallengePolicy.MaxAttempts"/>-th miss burns the
    /// code (the link stays usable until the record's TTL); a dummy compare runs where no record exists; a
    /// record carrying no code answers like one whose code is wrong.
    /// </summary>
    Task<ChallengeVerdict> ConsumeCodeAsync(ChallengeId id, LoginCode presented, CancellationToken ct);

    /// <summary>
    /// Presents a link token. Never touches the attempt counter. A hit deletes the record; any failure —
    /// malformed, unknown, expired, used, or a record without a link — is <see langword="null"/>.
    /// </summary>
    Task<LoginChallengeProof?> ConsumeLinkAsync(LoginLinkToken token, CancellationToken ct);
}
