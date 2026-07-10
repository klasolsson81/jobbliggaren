namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Content for the registration email-confirmation mail (#714), sent to the account's OWN address.
/// <para>
/// Carries the recipient's <see cref="UserId"/> (a compact link surrogate) and the
/// <see cref="UrlSafeToken"/> — both required to build the activation link
/// (<c>{BaseUrl}/bekrafta-konto?uid=&amp;token=</c>). Unlike the change-email confirmation there is NO
/// pending new address, and the recipient's email is NOT part of the link or content (it is the
/// account's current address, delivered to that same inbox). The token is an opaque,
/// SecurityStamp-bound DataProtector token, Base64Url-encoded. No other user's PII, no personnummer.
/// </para>
/// </summary>
public sealed record EmailConfirmationEmail(
    Guid UserId,
    string UrlSafeToken);
