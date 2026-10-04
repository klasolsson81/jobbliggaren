namespace Jobbliggaren.Application.Admin.Accounts.Queries.SearchAccounts;

/// <summary>
/// One row of the account list. <see cref="ApplicationCount"/> is null unless the account is active: the two
/// deletion paths leave different rows live, and an account without a profile owns none.
/// <see cref="DeletionEarliest"/> is set only while deletion is pending.
/// </summary>
public sealed record AccountListItemDto(
    Guid Id,
    string? Email,
    AccountRole Role,
    AccountStatus Status,
    bool EmailConfirmed,
    DateTimeOffset? RegisteredAt,
    DateOnly? DeletionEarliest,
    int? ApplicationCount)
{
    public override string ToString() => $"AccountListItemDto({Id}, {Status})";
}
