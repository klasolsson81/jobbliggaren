using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Admin.Accounts;

/// <summary>
/// The account list's orderings. Each is closed: the directory maps a member to constant SQL text,
/// because an ordering cannot be a bound parameter. Every ordering ends on the user id, so a page is
/// deterministic when its keys tie.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccountSort>))]
public enum AccountSort
{
    RegisteredNewest,
    RegisteredOldest,
    AddressAscending,
    AddressDescending,
}
