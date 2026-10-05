using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Admin.Accounts;

/// <summary>
/// The account's role. Admin is the only role the system grants, so every other account is a user.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccountRole>))]
public enum AccountRole
{
    User,
    Admin,
}
