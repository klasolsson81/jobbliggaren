namespace Jobbliggaren.Application.Common.Authorization;

/// <summary>
/// Kanoniska AuthorizationPolicy-namn för Jobbliggaren. Refereras av
/// AddAuthorization-konfig (Api) och endpoint-RequireAuthorization-anrop.
///
/// Skiljs medvetet från <see cref="Roles"/>: en policy är konceptuellt distinkt
/// från en roll (policies kan kombinera flera roll-krav + claim-krav + custom
/// requirements). Att binda policy-namn till roll-namn ger false equivalence
/// som bryter i Fas 6 när "AdminWithMfa"-policies tillkommer
/// (senior-cto-advisor 2026-05-11 Viktigt #1).
/// </summary>
public static class AuthorizationPolicies
{
    public const string Admin = "AdminPolicy";
}
