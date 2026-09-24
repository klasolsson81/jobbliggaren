using System.Net;
using System.Text.RegularExpressions;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>A mail part as its reader meets it: words rather than line breaks, paragraphs rather than tags.</summary>
internal static class MailText
{
    private static readonly Regex Paragraph =
        new("<p[^>]*>(.*?)</p>", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex Tag = new("<[^>]*>", RegexOptions.CultureInvariant);

    // The plain body is hard-wrapped; a sentence is asserted on its words, not on where a line breaks.
    public static string Unwrapped(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static List<string> PlainParagraphs(string body) =>
        [.. body.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n")
            .Select(Unwrapped)
            .Where(paragraph => paragraph.Length > 0)];

    public static List<string> HtmlParagraphs(string body) =>
        [.. Paragraph.Matches(body)
            .Select(match => Unwrapped(WebUtility.HtmlDecode(Tag.Replace(match.Groups[1].Value, " "))))];
}
