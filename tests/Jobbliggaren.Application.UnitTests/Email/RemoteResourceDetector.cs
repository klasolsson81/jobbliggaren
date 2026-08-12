using System.Text.RegularExpressions;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// The single definition of "this document references something remote", used by
/// <see cref="EmailHtmlNoRemoteResourceTests"/> over every template and by
/// <c>SesEmailSenderTests</c> against the HTML that actually reaches the <c>SendEmailRequest</c> — so
/// the templates and the send seam are judged by ONE definition rather than two that can drift.
///
/// <para>
/// <b>This is the SSOT for the forbidden set.</b> Prose in <c>EmailHtml</c> and in
/// <c>release-checklist.md</c> points here rather than enumerating, because a rule with three prose
/// homes is three homes to revise (dotnet-architect Nice-to-have 4, 2026-08-12).
/// </para>
///
/// <para>
/// <b>Element checks run over the whole document; attribute, CSS and URL checks run over LIVE MARKUP
/// ONLY, and that distinction is correctness rather than a loosening.</b> The property defended is
/// "this document cannot make the recipient's client fetch anything, and points nowhere off-host". A
/// URL sitting in TEXT does neither. Ad text arrives from JobTech HTML-encoded, so a hostile
/// <c>&lt;img src="…"&gt;</c> in a company name lands as <c>&amp;lt;img src=…</c> — still containing
/// the literal characters <c>src=</c> and a URL, while being inert. Scanning the whole document for
/// those would report a finding about a string that cannot fetch, and the first honest fixture
/// carrying a payload would force whoever met it to weaken a GDPR pin to get green. The element list
/// needs no such scoping: encoding already separates a live <c>&lt;img</c> from an encoded
/// <c>&amp;lt;img</c>.
/// </para>
///
/// <para>
/// <b>Findings are categorised, and the category is load-bearing.</b> Not everything on the forbidden
/// list can fetch by itself: <c>&lt;style&gt;</c> is banned because it is the only vehicle for
/// <c>@import</c> AND because this codebase's email layout may not depend on a style block at all. A
/// <c>&lt;style&gt;</c> hit reported as "the Art. 30 entry is false" would be a false alarm that
/// pressures the reader to weaken the detector — the very failure mode the paragraph above guards
/// against — so it is reported as its own category (dotnet-architect Nice-to-have 3, 2026-08-12).
/// </para>
/// </summary>
internal static class RemoteResourceDetector
{
    /// <summary>
    /// Elements that cause a network request, on their own or through their own source attributes.
    /// Shape-based, not name-based: the check is for the TAG OPENING, so attribute soup or an
    /// unusual attribute order cannot dodge it.
    /// <para>
    /// <c>&lt;svg</c> is on the list although the fetch is issued by its CHILDREN
    /// (<c>&lt;image href&gt;</c>, <c>&lt;use href&gt;</c>): those use <c>href</c>, which cannot be a
    /// blanket forbidden attribute since every mail here carries anchors, so the container is the
    /// checkable boundary. No template uses SVG, and none can — an inline seal would be markup a
    /// template cannot produce (security-auditor measured all three of these passing, 2026-08-12).
    /// </para>
    /// </summary>
    internal static readonly string[] FetchingElements =
        ["<img", "<script", "<link", "<iframe", "<video", "<audio", "<source", "<object",
         "<embed", "<picture", "<track", "<input", "<svg"];

    /// <summary>
    /// Forbidden but unable to fetch on its own. <c>@import</c> deliberately has NO arm of its own:
    /// its only vehicle is a <c>&lt;style&gt;</c> block (it is not valid in an inline <c>style</c>
    /// attribute), so an <c>@import</c> arm restricted to live markup would be structurally
    /// unreachable — a dead arm whose "counterfactual" is really felled by this one
    /// (code-reviewer Major 3, 2026-08-12).
    /// </summary>
    internal static readonly string[] ForbiddenElements = ["<style"];

    /// <summary>
    /// Source attributes, checked inside live markup only. <c>href=</c> is deliberately ABSENT: an
    /// anchor's href is not a fetch, it is a user action, and every mail here carries anchors. The
    /// distinction the whole arm turns on is FORM (a request issued while rendering) and not host.
    /// <c>http-equiv=</c> is here because <c>&lt;meta http-equiv="refresh"&gt;</c> navigates without
    /// one; the shell's own <c>&lt;meta&gt;</c> tags carry <c>charset</c> and <c>name</c>, never this.
    /// </summary>
    internal static readonly string[] FetchingAttributes =
        ["src=", "srcset=", "background=", "poster=", "http-equiv="];

    /// <summary>CSS that fetches from inside a <c>style</c> attribute.</summary>
    internal static readonly string[] FetchingCss = ["url("];

    /// <summary>
    /// The only schemes an <c>href</c> may use. <c>href=</c> cannot be a forbidden attribute — every
    /// mail here carries anchors — so the scheme is the checkable boundary instead.
    /// <para>
    /// Added 2026-08-12 (security-auditor Minor): <c>mailto:</c> was the first non-<c>https</c> scheme
    /// to reach <c>LinkParagraph</c>, and nothing in the repo would have caught a <c>javascript:</c> or
    /// <c>data:</c> href arriving the same way. Harmless today — every href is a constant built from
    /// <c>EmailOptions.BaseUrl</c> or the contact address, and all of them are encoded — which is
    /// precisely when a rail is cheap to add.
    /// </para>
    /// </summary>
    internal static readonly string[] AllowedHrefSchemes = ["https://", "mailto:"];

    private static readonly Regex HrefValue = new(
        @"href\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AbsoluteUrl = new(
        @"(?:https?:)?//[^\s""'<>()]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// One live-markup span: <c>&lt;</c> through the next <c>&gt;</c> that is NOT inside a quoted
    /// attribute value.
    /// <para>
    /// <b>The naive <c>&lt;[^&gt;]*&gt;</c> was wrong, and it was wrong in the direction that matters
    /// (security-auditor Major 1, 2026-08-12).</b> It stops at the first <c>&gt;</c> even inside a
    /// quoted value, so <c>&lt;td title="a&gt;b" background="https://evil.example/bg.png"&gt;</c>
    /// leaves everything after <c>"a&gt;</c> outside live markup and the fetch goes unreported. She
    /// constructed six such documents while every probe then in the suite stayed green — none of them
    /// crossed the tag boundary, and a probe that never crosses the control it tests measures nothing
    /// about the control.
    /// </para>
    /// <para>
    /// <b>TWO known and declared limits remain. Both are stated at full width, because the first
    /// version of this paragraph understated the first one and that is the defect class this file
    /// exists to catch (code-reviewer, 2026-08-12).</b>
    /// </para>
    /// <para>
    /// <b>(1) Odd quote parity.</b> Quotes are paired positionally, so a tag carrying a stray quote
    /// inside an unquoted attribute value — which the HTML5 tokenizer tolerates — never reaches a
    /// <c>&gt;</c> outside a quoted run and falls out of live markup entirely. <b>All THREE
    /// live-markup arms go blind on such a tag: attribute, CSS, and the off-host URL arm</b>, which
    /// reads the same <c>liveMarkup</c> string. So it is not only a fetch that can hide there — an
    /// off-host URL can too, which is register ground 2's SECOND sentence. What remains covered is
    /// exactly the ELEMENT-BORNE fetch, because that arm scans the whole document: a
    /// <c>&lt;img&gt;</c>, <c>&lt;script&gt;</c>, <c>&lt;iframe&gt;</c> or <c>&lt;svg&gt;</c> is caught
    /// through the same evasion. Note what that does NOT say: "the tracking pixel is covered" would be
    /// too broad, since a pixel is delivered just as well by <c>background=</c> or
    /// <c>background-image:url(...)</c>, both of which are in the blinded set.
    /// </para>
    /// <para>
    /// <b>(2) The attribute arm is NAME-based where the element arm is SHAPE-based.</b> It matches the
    /// literal <c>http-equiv=</c>, while HTML5 permits <c>http-equiv = "refresh"</c> with spaces
    /// around the equals sign; that spelling passes. The same holds for the other four attribute
    /// literals.
    /// </para>
    /// <para>
    /// Both are pinned as limits in <c>RemoteResourceDetectorTests</c> rather than chased: a regex
    /// never becomes a tokenizer, and trading one undeclared residual for another is the
    /// round-multiplying move (security-auditor, 2026-08-12). Nothing in this repo can produce either
    /// shape — <c>Encode</c> turns <c>"</c> into <c>&amp;quot;</c> and <c>&lt;</c> into
    /// <c>&amp;lt;</c> — so what these bound is the pin's reach against a hypothetical document, not
    /// against ours. If they ever need closing, the answer is a parser, not a longer regex.
    /// </para>
    /// </summary>
    private static readonly Regex TagSpan = new(
        @"<(?:[^>""']|""[^""]*""|'[^']*')*>", RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns one line per violation, empty when the document references nothing remote.
    /// </summary>
    /// <param name="html">The rendered document.</param>
    /// <param name="baseUrl">The only host any absolute URL may name (<c>EmailOptions.BaseUrl</c>).</param>
    public static IReadOnlyList<string> FindRemoteResources(string html, string baseUrl)
    {
        var findings = new List<string>();

        foreach (var element in FetchingElements)
        {
            if (html.Contains(element, StringComparison.OrdinalIgnoreCase))
                findings.Add($"fetching element: {element}");
        }

        foreach (var element in ForbiddenElements)
        {
            if (html.Contains(element, StringComparison.OrdinalIgnoreCase))
                findings.Add($"forbidden element (cannot fetch by itself): {element}");
        }

        var liveMarkup = string.Join("\n", TagSpan.Matches(html).Select(m => m.Value));

        foreach (var attribute in FetchingAttributes)
        {
            if (liveMarkup.Contains(attribute, StringComparison.OrdinalIgnoreCase))
                findings.Add($"fetching attribute: {attribute}");
        }

        foreach (var css in FetchingCss)
        {
            if (liveMarkup.Contains(css, StringComparison.OrdinalIgnoreCase))
                findings.Add($"fetching CSS: {css}");
        }

        foreach (Match href in HrefValue.Matches(liveMarkup))
        {
            var value = href.Groups[1].Value;

            if (!AllowedHrefSchemes.Any(
                scheme => value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add($"href with a scheme outside the allow-list: {value}");
            }
        }

        var allowedHost = new Uri(baseUrl).Host;
        foreach (Match match in AbsoluteUrl.Matches(liveMarkup))
        {
            var host = HostOf(match.Value);
            if (!string.Equals(host, allowedHost, StringComparison.OrdinalIgnoreCase))
                findings.Add($"absolute URL outside {allowedHost}: {match.Value}");
        }

        return findings;
    }

    /// <summary>
    /// Host of a matched URL, handling the protocol-relative <c>//host/path</c> form as well as an
    /// explicit scheme. Trims userinfo, port, path, query and fragment, so the comparison above is
    /// against a bare host and can be an EQUALITY test — a <c>Contains</c> would wave through both
    /// <c>jobbliggaren.se.evil.example</c> and <c>evil-jobbliggaren.se</c>.
    /// </summary>
    private static string HostOf(string url)
    {
        var afterScheme = url[(url.IndexOf("//", StringComparison.Ordinal) + 2)..];
        var authority = afterScheme.Split('/', '?', '#')[0];
        var hostAndPort = authority.Contains('@', StringComparison.Ordinal)
            ? authority[(authority.LastIndexOf('@') + 1)..]
            : authority;
        return hostAndPort.Split(':')[0];
    }
}
