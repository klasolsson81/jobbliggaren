using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Application.Auth.Access;

public sealed class CommittedSessionAuthorization
{
    private readonly AccountAccessSnapshot _transition;
    private bool _committed;

    private CommittedSessionAuthorization(AccountAccessSnapshot transition, SessionLifetime lifetime)
    {
        if (!Enum.IsDefined(lifetime))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        _transition = transition;
        Lifetime = lifetime;
    }

    internal static CommittedSessionAuthorization AfterFirstInboxProof(AccountAccessSnapshot transition) =>
        new(transition, SessionLifetime.Persistent);

    internal static CommittedSessionAuthorization AfterOwnAddressChange(
        AccountAccessSnapshot transition, SessionLifetime lifetime) => new(transition, lifetime);

    public Guid UserId => _transition.UserId;
    public long AccessRevision => _transition.AccessRevision;
    public SessionLifetime Lifetime { get; }

    internal void ConfirmCommit() => _committed = true;

    internal AccountAccessProof Authorize(SessionLifetime lifetime)
    {
        if (!_committed)
            throw new InvalidOperationException("The credential transition has not been confirmed committed.");
        if (lifetime != Lifetime)
            throw new InvalidOperationException("The replacement session must retain its authorized lifetime.");
        return new AccountAccessProof(_transition.CredentialCutoff, _transition.UserId, _transition.AccessRevision)
        {
            ExpectedEmail = _transition.Email,
            ExpectedCutoff = _transition.CredentialCutoff,
        };
    }

    public override string ToString() => "CommittedSessionAuthorization (credential contents redacted)";
}
