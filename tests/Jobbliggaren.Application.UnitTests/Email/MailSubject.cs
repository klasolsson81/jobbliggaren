using System.Net;
using System.Text.RegularExpressions;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #1981 PR 2 (security-auditor, probe 9, Minor 1) — the subject is the one content-bearing field the provider read key
/// reaches, so a mail whose body carries a code, a link or a token must carry none of them in its subject: a subject
/// holding a live code or sign-in link would make that key a way past the sign-in. Every template's tests call this
/// with their own rendered mail and the secrets it carries.
/// </summary>
internal static class MailSubject
{
    /// <summary>A path segment this long is treated as an identifier or a token, not a word of the route.</summary>
    private const int TokenLikeLength = 16;

    private static readonly Regex Link = new(@"https?://[^\s""'<>]+", RegexOptions.CultureInvariant);

    /// <summary>
    /// The subject holds no URL at all, none of the body's links, none of their query values or token-like path
    /// segments, and none of <paramref name="secrets"/> (the code, the raw token).
    /// </summary>
    public static void ShouldCarryNoCodeLinkOrToken(EmailTemplates.EmailContent mail, params string[] secrets)
    {
        var subject = mail.Subject;
        subject.ShouldNotContain("://");
        subject.ShouldNotContain("www.", Case.Insensitive);

        var links = Link.Matches(mail.PlainTextBody + "\n" + WebUtility.HtmlDecode(mail.HtmlBody))
            .Select(match => match.Value.TrimEnd('.', ',', ')'))
            .Distinct(StringComparer.Ordinal);
        foreach (var link in links)
        {
            subject.ShouldNotContain(link);
            var uri = new Uri(link);
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(segment => segment.Length >= TokenLikeLength)
                .Concat(uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(pair => Uri.UnescapeDataString(pair.Split('=', 2)[^1])));
            foreach (var part in parts.Where(part => part.Length > 0))
            {
                subject.ShouldNotContain(part, Case.Insensitive, $"the subject must not carry part of {link}");
            }
        }

        foreach (var secret in secrets)
        {
            subject.ShouldNotContain(secret, Case.Insensitive);
        }
    }
}
