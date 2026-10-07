using Jobbliggaren.Application.Common.Validation;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Auth.Access;

public sealed record AccountAccessSnapshot(
    Guid UserId,
    string? Email,
    bool IsSuspended,
    long AccessRevision,
    long CredentialCutoff,
    bool HasProfile,
    DateTimeOffset? DeletedAt,
    bool IsAdmin)
{
    public bool InboxConfirmed { get; init; } = true;
    public bool HasLiveProfile => HasProfile && DeletedAt is null;
    public bool CanAuthenticate => HasLiveProfile && !IsSuspended && EmailAddressRules.IsUsableInboxAddress(Email);
    public bool IsEffectiveAdmin => IsAdmin && CanAuthenticate;
    public override string ToString() => $"AccountAccessSnapshot {{ UserId = {UserId}, AccessRevision = {AccessRevision} }}";
}

public interface IAccountAccessReader
{
    Task<long> ReadEpochAsync(CancellationToken cancellationToken);
    Task<AccountAccessSnapshot?> ReadAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IAccountAccessScope : IAsyncDisposable
{
    bool OwnsCommit { get; }
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IAccountAccessCoordinator
{
    bool HasActiveScope { get; }
    bool HasLifecycleScope { get; }
    Task<IAccountAccessScope> BeginAsync(
        IReadOnlyCollection<Guid> userIds,
        bool lifecycle,
        CancellationToken cancellationToken);
    bool Holds(Guid userId);
}

/// <summary>The handler owns its protected commits, audit and any split-phase transport.</summary>
public interface IOwnsAccountTransaction;

public sealed record AccountAccessChanged(
    Guid UserId,
    bool IsSuspended,
    long AccessRevision,
    bool PendingDeletion);

public interface IAccountAccessWriter
{
    Task<AccountAccessSnapshot> AdvanceCredentialsAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result<AccountAccessChanged>> ChangeAsync(
        Guid actorId,
        Guid targetId,
        bool suspended,
        CancellationToken cancellationToken);
    Task<bool> CanRemoveAccessAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IAccountAccessCleanup
{
    Task CompleteAsync(AccountAccessChanged change, CancellationToken cancellationToken);
}

public interface IAccountAccessMutation
{
    Guid? TargetUserId { get; }
}

public static class AccountAccessErrors
{
    public const string AlreadySuspended = "Admin.AccountAlreadySuspended";
    public const string AlreadyReinstated = "Admin.AccountAlreadyReinstated";
    public const string SelfSuspension = "Admin.SelfSuspension";
    public const string LastAdministrator = "Admin.LastAdministrator";
    public const string ProfileUnavailable = "Admin.ProfileUnavailable";
    public const string AccountNotFound = "Admin.AccountNotFound";
}

public static class AuthenticatedSessionKeys
{
    public const string AccessRevision = "AccountAccessRevision";
}
