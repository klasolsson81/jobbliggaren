using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Admin.Accounts;

/// <summary>
/// An account's lifecycle state on the admin surface (ADR 0151). The rule is the login classifier's
/// (<c>LoginSubjectResolver</c>): no profile outranks a soft-deleted profile, which outranks a live one.
/// A parity test holds the two rules together. Suspension (#1976) slots in just above <see cref="Active"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccountStatus>))]
public enum AccountStatus
{
    Active,
    PendingDeletion,
    ProfileMissing,
}
