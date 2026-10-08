using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.GetAccountDetails;

/// <summary>One account's details, or null when Identity holds no account with the id (ADR 0151).</summary>
public sealed record GetAccountDetailsQuery(Guid UserId) : IQuery<AccountDetailsDto?>, IAdminRequest;

/// <summary>
/// The account panel's facts. The three counts are null unless the account is active, for the reason
/// <c>AccountListItemDto</c> gives. Last login and last activity have no source, so they are not fields.
/// </summary>
public sealed record AccountDetailsDto(
    Guid Id,
    string? Email,
    AccountRole Role,
    AccountStatus Status,
    bool EmailConfirmed,
    DateTimeOffset? RegisteredAt,
    DateOnly? DeletionEarliest,
    int? ApplicationCount,
    int? ResumeCount,
    int? SavedSearchCount,
    bool IsSuspended = false)
{
    public AccountDeletionTiming? Deletion { get; init; }
    public AccountDeletionTiming? DeletionPreview { get; init; }
    public override string ToString() => $"AccountDetailsDto({Id}, {Status})";
}
