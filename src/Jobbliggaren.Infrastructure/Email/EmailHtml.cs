using System.Globalization;
using System.Net;
using System.Text;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// The HTML half of every template in <see cref="EmailTemplates"/> (#183, 2026-08-12). Renders the
/// shell and the primitives; the copy itself stays in <see cref="EmailTemplates"/> beside the plain
/// text it mirrors, so the two parts of a <c>multipart/alternative</c> message cannot drift apart in
/// separate files.
///
/// <para>
/// <b>ZERO REMOTE RESOURCES, AND THAT IS A GDPR CONTROL RATHER THAN A STYLE CHOICE.</b> The Art. 30
/// entry "Utgående transaktionell e-post" states as MEASURED FACT that SES's 60-day, recipient-level
/// open/click metrics do not arise for us. Until 2026-08-12 that rested partly on "the body is
/// Body.Text with no HTML part", which this file falsifies. The replacement ground is this file's
/// property: no <c>&lt;img&gt;</c>, no <c>&lt;link&gt;</c>, no <c>&lt;script&gt;</c>, no
/// <c>&lt;style&gt;</c>, no <c>@import</c>, and no absolute URL whose host lies outside
/// <c>EmailOptions.BaseUrl</c>. A remote resource here is a tracking capability regardless of
/// provider — the recipient's client fetches it and the host learns an IP address and an open time,
/// which under EDPB Guidelines 2/2023 on Art. 5(3) ePrivacy needs consent our copy never asks for.
/// <c>EmailHtmlNoRemoteResourceTests</c> pins it over all eight templates with a counterfactual per
/// detector. Adding one falsifies the register, so re-measure the register BEFORE such a change
/// ships, never after (security-auditor 2026-08-12, condition 1).
/// </para>
///
/// <para>
/// <b>Every interpolated value is HTML-encoded, and that is load-bearing.</b> Job titles and company
/// names reach these templates from JobTech ad data, which is third-party text this codebase does not
/// author. Unencoded, a crafted company name would inject markup into a mail we sign with our own
/// DKIM — including the very <c>&lt;img&gt;</c> the paragraph above forbids. Encoding runs in
/// <see cref="Encode"/>, and nothing writes an interpolated value to the buffer without it.
/// </para>
///
/// <para>
/// <b>Client compatibility (Klas-krav: no "does this look odd, click here").</b> Table layout, inline
/// CSS only, 600px maximum, no flexbox and no grid, no <c>&lt;style&gt;</c> block at all — so nothing
/// about the layout depends on CSS a client may strip. Outlook on Windows renders with the Word
/// engine and is the binding constraint: it ignores <c>border-radius</c> (buttons degrade to square,
/// which is acceptable) and honours <c>bgcolor</c> on a <c>&lt;td&gt;</c>, which is why the call to
/// action is a one-cell table rather than a styled anchor.
/// </para>
///
/// <para>
/// <b>Colours are DESIGN.md token VALUES, copied deliberately.</b> An email cannot read
/// <c>--jp-*</c> custom properties, so the hex literals below are the only possible form. They are
/// not new tokens and no token is redefined here — changing a colour is still a DESIGN.md change
/// first. The token each literal came from is named on its own line so a token edit can find its
/// copy. Fonts are the one place the app's own rule cannot be followed: Source Sans 3 is a webfont,
/// a webfont is a remote resource, and remote resources are forbidden above. System fonts are
/// therefore mandated by the security condition, not chosen over the design system.
/// </para>
/// </summary>
internal static class EmailHtml
{
    // ---- palette: DESIGN.md token values, copied because email cannot read custom properties ----

    /// <summary><c>--jp-canvas</c> — the page substrate behind the card.</summary>
    private const string Canvas = "#F4F6FA";

    /// <summary><c>--jp-surface</c> — the card itself ("paper").</summary>
    private const string Surface = "#FFFFFF";

    /// <summary><c>--jp-border</c> — the card outline. Depth comes from borders, never shadow.</summary>
    private const string Border = "#C9D2E0";

    /// <summary><c>--jp-border-soft</c> — the footer divider.</summary>
    private const string BorderSoft = "#E3E8F0";

    /// <summary><c>--jp-heading-2</c> (= <c>--jp-navy-800</c>) — the mail title. Navy is information, never interaction.</summary>
    private const string Heading = "#0A2647";

    /// <summary>
    /// <c>--jp-ink-1</c> — body text AND footer text. Deliberately the only text colour besides the
    /// title and links: Klas-direktiv "ingen grå text", so no <c>--jp-ink-2</c>/<c>-3</c> tier
    /// appears here even where an app surface would use one.
    /// </summary>
    private const string Ink = "#0C1A2E";

    /// <summary><c>--jp-accent-800</c> — button fill and link text. Never dark-shifted.</summary>
    private const string Accent = "#15603F";

    /// <summary><c>--jp-gold</c> — the seal's gold row, the brand signature. One 2px rule in the footer.</summary>
    private const string Gold = "#E8C77B";

    /// <summary>
    /// System fonts only. See the type-level note: the app's Source Sans 3 is a webfont and a webfont
    /// is a remote resource, which the GDPR ground above forbids. The named fallbacks are fallbacks,
    /// not the primary, so DESIGN.md's "never Arial/Roboto as primary" rule is intact.
    /// </summary>
    private const string FontStack =
        "-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif";

    private const string BodyStyle =
        $"margin:0 0 14px 0;font-family:{FontStack};font-size:16px;line-height:1.55;color:{Ink};";

    /// <summary>
    /// Wraps a rendered body in the shell. <paramref name="title"/> reaches both <c>&lt;title&gt;</c>
    /// and the visible heading; <paramref name="preheader"/> is the hidden line the inbox shows as a
    /// preview beside the subject.
    /// </summary>
    /// <param name="title">Visible heading and document title. Encoded here.</param>
    /// <param name="preheader">Inbox preview text. Encoded here.</param>
    /// <param name="body">Already-rendered body markup from the primitives below.</param>
    public static string Document(string title, string preheader, string body) =>
        $"""
        <!DOCTYPE html>
        <html lang="sv">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="color-scheme" content="light">
        <meta name="supported-color-schemes" content="light">
        <title>{Encode(title)}</title>
        </head>
        <body style="margin:0;padding:0;background-color:{Canvas};">
        <div style="display:none;max-height:0;max-width:0;overflow:hidden;opacity:0;font-size:1px;line-height:1px;color:{Canvas};">{Encode(preheader)}</div>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:{Canvas};">
        <tr><td align="center" style="padding:24px 12px;">
        <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="width:600px;max-width:600px;background-color:{Surface};border:1px solid {Border};border-radius:6px;">
        <tr><td style="height:4px;line-height:4px;font-size:4px;background-color:{Accent};border-radius:6px 6px 0 0;">&nbsp;</td></tr>
        <tr><td style="padding:28px 32px 0 32px;">
        <h1 style="margin:0 0 16px 0;font-family:{FontStack};font-size:22px;line-height:1.3;font-weight:700;color:{Heading};">{Encode(title)}</h1>
        </td></tr>
        <tr><td style="padding:0 32px 4px 32px;">
        {body}
        </td></tr>
        <tr><td style="padding:8px 32px 0 32px;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr><td style="height:1px;line-height:1px;font-size:1px;background-color:{BorderSoft};">&nbsp;</td></tr></table>
        </td></tr>
        <tr><td style="padding:18px 32px 26px 32px;">
        <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td style="width:28px;height:2px;line-height:2px;font-size:2px;background-color:{Gold};">&nbsp;</td></tr></table>
        <div style="margin:10px 0 0 0;font-family:{FontStack};font-size:17px;line-height:1.3;font-weight:700;letter-spacing:-0.01em;color:{Ink};">Jobbliggaren</div>
        <div style="margin:4px 0 0 0;font-family:{FontStack};font-size:13px;line-height:1.5;color:{Ink};">Jobbliggaren är helt gratis att använda.</div>
        </td></tr>
        </table>
        </td></tr>
        </table>
        </body>
        </html>
        """;

    /// <summary>A body paragraph. The text is encoded.</summary>
    public static string P(string text) =>
        $"""<p style="{BodyStyle}">{Encode(text)}</p>""";

    /// <summary>
    /// The call to action, as a one-cell table with <c>bgcolor</c> so Outlook's Word engine paints
    /// the fill. <paramref name="href"/> is a same-origin URL built by the caller from
    /// <c>EmailOptions.BaseUrl</c>; it is encoded for attribute context, which is what turns the
    /// query string's <c>&amp;</c> into <c>&amp;amp;</c>.
    /// </summary>
    public static string Button(string href, string label) =>
        $"""
        <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 18px 0;"><tr>
        <td bgcolor="{Accent}" style="background-color:{Accent};border-radius:6px;">
        <a href="{Encode(href)}" style="display:inline-block;padding:12px 22px;font-family:{FontStack};font-size:16px;line-height:1.2;font-weight:600;color:{Surface};text-decoration:none;">{Encode(label)}</a>
        </td></tr></table>
        """;

    /// <summary>
    /// A paragraph ending in an inline link, for the secondary routes (settings, help centre) that
    /// must be reachable but must not compete with the button. Both parts are encoded.
    /// </summary>
    public static string LinkParagraph(string leadingText, string href, string linkText) =>
        $"""
        <p style="{BodyStyle}">{Encode(leadingText)} <a href="{Encode(href)}" style="color:{Accent};text-decoration:underline;">{Encode(linkText)}</a></p>
        """;

    /// <summary>
    /// The ad list. <paramref name="items"/> carries third-party ad text (job title, company name),
    /// which is why every row goes through <see cref="Encode"/> — see the type-level note.
    /// </summary>
    public static string List(IEnumerable<string> items)
    {
        var rows = new StringBuilder();
        foreach (var item in items)
        {
            rows.Append(
                CultureInfo.InvariantCulture,
                $"""<li style="margin:0 0 6px 0;font-family:{FontStack};font-size:16px;line-height:1.5;color:{Ink};">{Encode(item)}</li>""");
        }

        return $"""<ul style="margin:0 0 14px 0;padding:0 0 0 20px;">{rows}</ul>""";
    }

    /// <summary>
    /// HTML-encodes text and URLs alike. <see cref="WebUtility.HtmlEncode"/> covers both contexts
    /// used here: element text and double-quoted attribute values (it escapes <c>&lt;</c>,
    /// <c>&gt;</c>, <c>&amp;</c> and <c>"</c>). Every value that reaches the buffer passes through
    /// here — there is deliberately no raw-passthrough overload for a caller to reach for.
    /// </summary>
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
