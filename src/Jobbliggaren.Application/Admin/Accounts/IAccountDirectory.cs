using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Application.Admin.Accounts;

/// <summary>
/// Every account Identity holds, with the profile facts that set its status (ADR 0151). Answering it takes
/// one bounded statement across the identity and app schemas, so it lives in Infrastructure behind this
/// port. Only the three admin account queries consume it; an architecture test pins that.
/// </summary>
public interface IAccountDirectory
{
    /// <summary>One page of the matching accounts, and how many match in all.</summary>
    Task<AccountDirectoryPage> SearchAsync(AccountDirectorySearch search, CancellationToken cancellationToken);

    /// <summary>How many accounts match the address term, in all and per status.</summary>
    Task<AccountStatusCounts> CountByStatusAsync(string? address, CancellationToken cancellationToken);

    /// <summary>The account with this id, or null when Identity holds none.</summary>
    Task<AccountDirectoryEntry?> FindAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>A page request. A blank address means no address filter, and a null status means every status.</summary>
public sealed record AccountDirectorySearch(
    string? Address,
    AccountStatus? Status,
    AccountSort Sort,
    int Page,
    int PageSize)
{
    public override string ToString() =>
        $"AccountDirectorySearch(address {(string.IsNullOrWhiteSpace(Address) ? "none" : "redacted")}, "
        + $"status {Status?.ToString() ?? "any"}, {Sort}, page {Page}/{PageSize})";
}

/// <summary>
/// One account as the directory reads it. <see cref="RegisteredAt"/> and <see cref="DeletedAt"/> come from the
/// profile, so an account without one has neither.
/// </summary>
public sealed record AccountDirectoryEntry(
    Guid UserId,
    string? Email,
    bool IsAdmin,
    bool EmailConfirmed,
    AccountStatus Status,
    JobSeekerId? JobSeekerId,
    DateTimeOffset? RegisteredAt,
    DateTimeOffset? DeletedAt)
{
    /// <summary>The earliest permanent deletion, while deletion is pending.</summary>
    public DateOnly? PermanentDeletionEarliest =>
        Status == AccountStatus.PendingDeletion && DeletedAt is { } deletedAt
            ? AccountRestoreWindow.PermanentDeletionEarliest(deletedAt)
            : null;

    public override string ToString() => $"AccountDirectoryEntry({UserId}, {Status})";
}

public sealed record AccountDirectoryPage(IReadOnlyList<AccountDirectoryEntry> Entries, int TotalCount);

public sealed record AccountStatusCounts(int Total, int Active, int PendingDeletion, int ProfileMissing);
