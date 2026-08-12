using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// Already-encoded markup, so that a body cannot be assembled from raw strings by accident.
/// <para>
/// <b>What the compiler actually enforces, stated exactly — because the two previous versions of this
/// paragraph both claimed more than they held.</b> <see cref="EmailHtml.Document"/> takes
/// <see cref="Markup"/>, so no caller outside this file can hand it a <see cref="string"/>: THAT
/// boundary — between <c>EmailTemplates</c> and <c>EmailHtml</c> — is closed by the type system, and
/// it is the boundary that matters, since it is where template authors work. <b>Inside the assembly
/// the type guarantees nothing</b>: a positional <c>record struct</c> has a public primary
/// constructor, so <c>new Markup(rawHtml)</c> compiles anywhere in Infrastructure and in the test
/// assemblies reached by <c>InternalsVisibleTo</c>. What holds is therefore a GREPPABLE convention
/// with a small audit surface, not a compiler guarantee: <c>Markup</c> is constructed in exactly
/// seven places, all in this file, and every interpolation in those seven passes through
/// <see cref="EmailHtml.Encode"/>. Measured 2026-08-12: <c>grep "new Markup(" src/ tests/</c> returns
/// zero hits outside this file.
/// </para>
/// <para>
/// The history is the point. v1 had <c>Document</c> take a raw <see cref="string"/> body while the
/// doc claimed nothing reached the buffer unencoded — a seam a caller opened by passing an argument,
/// needing no overload (dotnet-architect Viktigt 1 / code-reviewer Major 1). v2 introduced this type
/// and claimed the compiler now enforced it, which three reviewers independently measured false
/// (code-reviewer Major 1 again, security-auditor Minor, dotnet-architect Nice-to-have). Both times
/// the code was safe and the SENTENCE was not, which is the exact defect class this PR is about.
/// </para>
/// </summary>
internal readonly record struct Markup(string Value)
{
    public static Markup Empty => new(string.Empty);

    public static Markup operator +(Markup left, Markup right) => new(left.Value + right.Value);

    public override string ToString() => Value;
}

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
/// property: nothing here makes the recipient's client issue a network request, and no absolute URL
/// IN LIVE MARKUP names a host outside <c>EmailOptions.BaseUrl</c>. The qualification is load-bearing
/// and was missing here after it was added to the runbook — encoded ad text can legitimately contain
/// an off-host URL as inert characters, which this file's own injection test asserts
/// (<c>ShouldContain("evil.example")</c>), so the unqualified sentence is false of a document the
/// suite deliberately produces. A remote resource here is a tracking capability
/// regardless of provider — the recipient's client fetches it and the host learns an IP address and
/// an open time, which under EDPB Guidelines 2/2023 on Art. 5(3) ePrivacy needs consent our copy
/// never asks for. <b>The exact forbidden set is the detector's own arrays in
/// <c>RemoteResourceDetector</c>, not this paragraph</b> — a rule with three prose homes is three
/// homes to revise (dotnet-architect Nice-to-have 4). Adding a remote resource falsifies the
/// register, so re-measure the register BEFORE such a change ships, never after (security-auditor
/// 2026-08-12, condition 1).
/// </para>
///
/// <para>
/// <b>Encoding is load-bearing, and now structural.</b> Job titles and company names reach these
/// templates from JobTech ad data, which is third-party text this codebase does not author —
/// <c>PlatsbankenJobSource</c> puts <c>hit.Employer?.Name?.Trim()</c> straight into
/// <c>CompanyName</c>, and the payload sanitizer never runs on it. Unencoded, a crafted company name
/// would inject markup into a mail we sign with our own DKIM, including the very <c>&lt;img&gt;</c>
/// the paragraph above forbids. Every interpolation in the seven <c>Markup</c> constructions below
/// passes through <see cref="Encode"/>; those seven are the whole audit surface, and
/// <see cref="Markup"/> explains why that is a convention rather than a compiler guarantee.
/// </para>
///
/// <para>
/// <b>Client compatibility (Klas-krav: no "does this look odd, click here").</b> Table layout, inline
/// CSS only, 600px maximum, no flexbox and no grid, no <c>&lt;style&gt;</c> block at all — so nothing
/// about the layout depends on CSS a client may strip. Outlook on Windows renders with the Word
/// engine and is the binding constraint. Word honours <c>bgcolor</c> on a <c>&lt;td&gt;</c> but
/// ignores BOTH <c>display:inline-block</c> and <c>padding</c> on an inline <c>&lt;a&gt;</c>, so a
/// one-cell table alone paints the fill and leaves the label jammed against its edges.
/// <b>Each engine therefore gets exactly one padding, and neither gets two:</b> the anchor carries
/// real <c>padding</c> for every other client, and the cell carries <c>mso-padding-alt</c>, which
/// only Word reads. Moving the padding to the cell for everyone was the first repair and it was
/// wrong in a way worth recording — it fixed Word and shrank the CLICKABLE area to the label's own
/// box in every client, so the affordance became ~2.3x the target on the primary action of all eight
/// mails, and a mail is read mostly on a phone (design-reviewer, 2026-08-12, correcting her own
/// prescription). <c>mso-padding-alt</c> is the accepted form and needs no VML.
/// <c>border-radius</c> is ignored in Word too and buttons degrade to square, which is acceptable.
/// </para>
///
/// <para>
/// <b>Design-system deviations live in DESIGN.md § E-post, not here.</b> Email cannot read
/// <c>--jp-*</c> custom properties, cannot load a webfont without making a remote request, and cannot
/// use the app's type scale as written. Those three deviations are ratified there; this file names
/// the source token per literal so a token edit can find its copy, and
/// <c>EmailPaletteMirrorsDesignTokensTests</c> pins the mirror against <c>globals.css</c> so it
/// cannot rot silently (design-reviewer Blocker 1 + Minor 1, 2026-08-12).
/// </para>
/// </summary>
internal static class EmailHtml
{
    // ---- palette: DESIGN.md token values, copied because email cannot read custom properties.
    // Pinned against globals.css by EmailPaletteMirrorsDesignTokensTests. ----

    /// <summary><c>--jp-canvas</c> — the page substrate behind the card.</summary>
    internal const string Canvas = "#F4F6FA";

    /// <summary><c>--jp-surface</c> — the card itself ("paper").</summary>
    internal const string Surface = "#FFFFFF";

    /// <summary><c>--jp-border</c> — the card outline. Depth comes from borders, never shadow.</summary>
    internal const string Border = "#C9D2E0";

    /// <summary><c>--jp-border-soft</c> — the footer divider.</summary>
    internal const string BorderSoft = "#E3E8F0";

    /// <summary><c>--jp-navy-800</c>, which <c>--jp-heading-2</c> resolves to — the mail title.</summary>
    internal const string Heading = "#0A2647";

    /// <summary>
    /// <c>--jp-ink-1</c> — body text AND footer text. Deliberately the only text colour besides the
    /// title and links: Klas-direktiv "ingen grå text", so no <c>--jp-ink-2</c>/<c>-3</c> tier
    /// appears here even where an app surface would use one.
    /// </summary>
    internal const string Ink = "#0C1A2E";

    /// <summary><c>--jp-accent-800</c> — button fill and link text. Never dark-shifted.</summary>
    internal const string Accent = "#15603F";

    /// <summary>
    /// System fonts. The app's Source Sans 3 is a webfont, a webfont is a remote request, and the
    /// GDPR ground above forbids one — so this is mandated, not preferred. It IS a real deviation
    /// rather than a technicality: the design skill's rule reads "never Inter/Roboto/Arial/system-ui
    /// as primary", and <c>-apple-system</c> is system-ui, so the rule bites and the deviation is
    /// ratified in DESIGN.md § E-post instead of argued away here (code-reviewer Major 2,
    /// 2026-08-12 — the earlier note cited DESIGN.md, where the rule does not live, and dropped the
    /// one clause that applies). Word skips the first two entries and lands on Segoe UI.
    /// </summary>
    private const string FontStack =
        "-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif";

    private const string BodyStyle =
        $"margin:0 0 14px 0;font-family:{FontStack};font-size:16px;line-height:1.55;color:{Ink};";

    /// <summary>
    /// Zero-width filler after the preheader. Without it Gmail runs the preview text straight into
    /// the first visible line, which here is the <c>&lt;h1&gt;</c> — identical to the subject — so
    /// the inbox preview repeats the subject back at itself (design-reviewer Minor 3, 2026-08-12).
    /// </summary>
    private const string PreheaderFiller =
        "&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;"
        + "&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;";

    /// <summary>
    /// Wraps a rendered body in the shell.
    /// <para>
    /// <b>What the shell introduces that the plain-text part has no counterpart for</b>, named
    /// exhaustively because the Art. 30 Datakategori argument rests on the list being complete
    /// (design-reviewer and security-auditor both measured the earlier list as short): the
    /// <c>&lt;title&gt;</c>, the <paramref name="preheader"/>, the visible <c>&lt;h1&gt;</c>, the
    /// wordmark set as text in the footer, and one footer line saying the service is free. The first
    /// three carry no information that is not already in the subject or the body; none of the five is
    /// a personal data field, which is the test that matters for the register.
    /// </para>
    /// </summary>
    /// <param name="title">Visible heading and document title. Encoded here.</param>
    /// <param name="preheader">Inbox preview text. Encoded here.</param>
    /// <param name="body">Body markup from the primitives below. <see cref="Markup"/>, not
    /// <see cref="string"/>, so this parameter cannot be a raw-markup seam.</param>
    public static string Document(string title, string preheader, Markup body) =>
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
        <div style="display:none;max-height:0;max-width:0;overflow:hidden;opacity:0;font-size:1px;line-height:1px;color:{Canvas};">{Encode(preheader)}{PreheaderFiller}</div>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="{Canvas}" style="background-color:{Canvas};">
        <tr><td align="center" style="padding:24px 12px;">
        <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" bgcolor="{Surface}" style="width:600px;max-width:600px;background-color:{Surface};border:1px solid {Border};border-radius:6px;">
        <tr><td height="4" bgcolor="{Accent}" style="height:4px;line-height:4px;font-size:4px;background-color:{Accent};">&nbsp;</td></tr>
        <tr><td style="padding:28px 32px 0 32px;">
        <h1 style="margin:0 0 16px 0;font-family:{FontStack};font-size:22px;line-height:1.3;font-weight:700;color:{Heading};">{Encode(title)}</h1>
        </td></tr>
        <tr><td style="padding:0 32px 4px 32px;">
        {body}
        </td></tr>
        <tr><td style="padding:8px 32px 0 32px;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr><td height="1" bgcolor="{BorderSoft}" style="height:1px;line-height:1px;font-size:1px;background-color:{BorderSoft};">&nbsp;</td></tr></table>
        </td></tr>
        <tr><td style="padding:18px 32px 26px 32px;">
        <div style="margin:0;font-family:{FontStack};font-size:16px;line-height:1.3;font-weight:700;color:{Ink};">Jobbliggaren</div>
        <div style="margin:4px 0 0 0;font-family:{FontStack};font-size:14px;line-height:1.5;color:{Ink};">Tjänsten är helt gratis att använda.</div>
        </td></tr>
        </table>
        </td></tr>
        </table>
        </body>
        </html>
        """;

    /// <summary>A body paragraph. The text is encoded.</summary>
    public static Markup P(string text) =>
        new($"""<p style="{BodyStyle}">{Encode(text)}</p>""");

    /// <summary>
    /// The sign-off, both lines in one paragraph exactly as the plain-text part sets them
    /// ("Vänliga hälsningar," / "Jobbliggaren"). They were split across the footer divider before,
    /// which left a comma-terminated line ending nothing (design-reviewer Major 2, 2026-08-12). The
    /// footer wordmark stays: it is brand chrome carrying the free-of-charge line, not the sign-off.
    /// </summary>
    public static Markup SignOff() =>
        new($"""<p style="{BodyStyle}">Vänliga hälsningar,<br>Jobbliggaren</p>""");

    /// <summary>
    /// The call to action. Padding sits on the CELL, not on the anchor — see the type-level note on
    /// the Word engine. <paramref name="href"/> is a same-origin URL built by the caller from
    /// <c>EmailOptions.BaseUrl</c>; encoding it for attribute context is what turns the query
    /// string's <c>&amp;</c> into <c>&amp;amp;</c>.
    /// </summary>
    public static Markup Button(string href, string label) =>
        new($"""
        <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 18px 0;"><tr>
        <td bgcolor="{Accent}" style="background-color:{Accent};border-radius:6px;mso-padding-alt:12px 22px;">
        <a href="{Encode(href)}" style="display:inline-block;padding:12px 22px;mso-padding-alt:0;font-family:{FontStack};font-size:16px;line-height:19px;font-weight:600;color:{Surface};text-decoration:none;">{Encode(label)}</a>
        </td></tr></table>
        """);

    /// <summary>
    /// A paragraph ending in an inline link, for the secondary routes (settings, help centre) that
    /// must be reachable but must not compete with the button. Underlined rather than colour-only,
    /// so the link is not identified by colour alone (WCAG 1.4.1). Both parts are encoded.
    /// </summary>
    public static Markup LinkParagraph(string leadingText, string href, string linkText) =>
        new($"""
        <p style="{BodyStyle}">{Encode(leadingText)} <a href="{Encode(href)}" style="color:{Accent};text-decoration:underline;">{Encode(linkText)}</a></p>
        """);

    /// <summary>
    /// The ad list. <paramref name="items"/> carries third-party ad text (job title, company name),
    /// which is why every row goes through <see cref="Encode"/> — see the type-level note.
    /// </summary>
    public static Markup List(IEnumerable<string> items)
    {
        var rows = new StringBuilder();
        foreach (var item in items)
        {
            // The one interpolation of an already-built fragment in this file. It is a local
            // StringBuilder whose every contribution was encoded one line above, never a parameter,
            // so it is not a seam a caller can reach.
            rows.Append(
                CultureInfo.InvariantCulture,
                $"""<li style="margin:0 0 6px 0;font-family:{FontStack};font-size:16px;line-height:1.5;color:{Ink};">{Encode(item)}</li>""");
        }

        return new($"""<ul style="margin:0 0 14px 0;padding:0 0 0 20px;">{rows}</ul>""");
    }

    /// <summary>
    /// The sole <see cref="string"/>-to-<see cref="Markup"/> conversion, and therefore the only way
    /// text reaches a document. Escapes every character that is dangerous in the two contexts used
    /// here — element text and double-quoted attribute values — so <c>&lt;</c>, <c>&gt;</c>,
    /// <c>&amp;</c>, <c>"</c> and <c>'</c> can never close a tag or an attribute.
    /// <para>
    /// <b><see cref="UnicodeRanges.All"/> is deliberate, and it is not a relaxation of the escaping.</b>
    /// The default encoder (like <see cref="WebUtility.HtmlEncode"/>) also rewrites every non-ASCII
    /// character as a numeric reference, so "så annonser" ships as "s&amp;#229; annonser". That
    /// renders correctly, and it still cost something real: the HTML part's copy stops being
    /// comparable to the plain-text part's, which is the parity property this codebase asserts in
    /// tests and greps for in review. Widening the allowed range changes which characters are left
    /// ALONE, never which are escaped — the HTML-sensitive set above is escaped either way — and it
    /// is the framework's documented approach for non-English content. CLAUDE.md §10 ("åäö must
    /// survive serialization") is met literally rather than through an entity.
    /// </para>
    /// </summary>
    private static string Encode(string value) => Encoder.Encode(value);

    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);
}
