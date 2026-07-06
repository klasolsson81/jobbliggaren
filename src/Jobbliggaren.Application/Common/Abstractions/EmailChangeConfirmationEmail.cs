namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Content for the change-email ownership-confirmation mail (#679), sent to the NEW address.
/// <para>
/// Unlike the notification-email contracts (which keep the recipient out of the content),
/// this record deliberately carries the recipient's own <see cref="NewEmail"/> and the
/// <see cref="UrlSafeToken"/>: both are required to build the confirmation link
/// (<c>{BaseUrl}/bekrafta-epost?uid=&amp;email=&amp;token=</c>). The new address is the recipient's
/// OWN address, delivered to that same inbox; the token is an opaque, single-use,
/// SecurityStamp-bound DataProtector token, Base64Url-encoded (CTO-bind #1/#2). No other user's
/// PII, no personnummer. The address is not switched until the link is opened.
/// </para>
/// </summary>
public sealed record EmailChangeConfirmationEmail(
    Guid UserId,
    string NewEmail,
    string UrlSafeToken);
