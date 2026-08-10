namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Content for the password-reset link mail (#1171). Mirrors <see cref="EmailConfirmationEmail"/>:
/// the recipient address travels separately in <c>IEmailSender.SendPasswordResetAsync</c>'s
/// <c>toEmail</c>, so this record carries no PII of its own — a userId and an opaque token.
/// </summary>
/// <param name="UserId">
/// The account the token was minted for. The template renders it as <c>uid={UserId:D}</c>, and the
/// DASHED form is required: System.Text.Json's Guid converter accepts only <c>D</c>, so a <c>:N</c>
/// link 400s at the binder on every click (#981).
/// </param>
/// <param name="UrlSafeToken">
/// The Identity reset token, Base64Url-encoded so it survives a query string unescaped. Never
/// logged: it is a bearer credential for the account until it is used or expires.
/// </param>
public sealed record PasswordResetEmail(Guid UserId, string UrlSafeToken);
