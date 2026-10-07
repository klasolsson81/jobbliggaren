namespace Jobbliggaren.Application.Auth.Access;

public sealed record AccountAccessProof(long FlowEpoch, Guid? UserId = null, long? AccessRevision = null)
{
    public static AccountAccessProof Legacy { get; } = new(0);
    public string? ExpectedEmail { get; init; }
    public long? ExpectedCutoff { get; init; }

    public bool Admits(AccountAccessSnapshot account) =>
        FlowEpoch >= 0 && FlowEpoch >= account.CredentialCutoff
        && ((UserId is null) == (AccessRevision is null)) && UserId != Guid.Empty
        && (UserId is null || UserId == account.UserId)
        && (AccessRevision is null || AccessRevision == account.AccessRevision)
        && (ExpectedCutoff is null || ExpectedCutoff == account.CredentialCutoff)
        && (ExpectedEmail is null || string.Equals(ExpectedEmail, account.Email, StringComparison.Ordinal))
        && account.CanAuthenticate;

    public AccountAccessProof Bind(AccountAccessSnapshot account) =>
        Admits(account)
            ? this with { UserId = account.UserId, AccessRevision = account.AccessRevision }
            : throw new InvalidOperationException("An obsolete proof cannot be rebound.");

    public override string ToString() => $"AccountAccessProof {{ FlowEpoch = {FlowEpoch}, UserId = {UserId}, AccessRevision = {AccessRevision} }}";
}
