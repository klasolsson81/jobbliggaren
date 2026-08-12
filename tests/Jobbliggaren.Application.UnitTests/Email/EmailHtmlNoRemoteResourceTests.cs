using System.Text.RegularExpressions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// GROUND 2 of the Art. 30 retention claim for "Utgående transaktionell e-post", pinned over ALL
/// EIGHT templates (#183, 2026-08-12 — security-auditor condition 1).
///
/// <para>
/// <b>What this file defends, and why it is not a style rule.</b> The register states as MEASURED
/// FACT that SES's 60-day, RECIPIENT-LEVEL open/click metrics do not arise for us. Until 2026-08-12
/// that rested partly on "the body is Body.Text with no HTML part" — a ground the HTML templates
/// struck. The replacement is the property asserted here: the rendered HTML references no remote
/// resource at all. It was chosen because it has the same shape as the ground it replaces —
/// unattackable from outside this repo, and pinned rather than merely complied with — and it is
/// STRONGER than the SES question it stands in for: a remote resource in an HTML mail is a tracking
/// capability regardless of provider, because the recipient's client fetches it and the host learns
/// an IP address and an open time. Under EDPB Guidelines 2/2023 on Art. 5(3) ePrivacy, tracking
/// pixels in email are in scope and require consent, and this product's consent copy asks for
/// notification delivery, never for open tracking.
/// </para>
///
/// <para>
/// <b>A failure here is a register defect before it is a test failure.</b> If a template gains a
/// remote resource, the fix is not to relax the detector: it is to re-measure the register's
/// retention entry BEFORE that change ships, because the entry is written as measured fact and would
/// otherwise rot silently.
/// </para>
///
/// <para>
/// <b>The detector is proven to fail.</b> An "absence" assertion over a detector nobody has seen
/// reject anything is fail-open: it cannot tell "there is no remote resource" from "the detector is
/// broken". Every arm of <see cref="FindRemoteResources"/> therefore has a counterfactual below that
/// feeds it a document carrying exactly that violation and requires a hit, plus one control document
/// that must produce NO finding — without that control, every counterfactual would also pass against
/// a detector hard-wired to reject everything. <see cref="EmailHtml_TheTemplateSet_CoversAllEightPortMethods"/>
/// closes the third way this suite could pass vacuously: a fixture list that silently shrank.
/// </para>
/// </summary>
public class EmailHtmlNoRemoteResourceTests
{
    private const string BaseUrl = "https://jobbliggaren.se";

    // Same Base64Url shape ASP.NET Identity emits, and the same fixture SesEmailSenderTests uses.
    // Not a real token: no account exists that it could activate. gitleaks:allow
    private const string UrlSafeToken = "CfDJ8Nr-9xQvT0pLm2Zq_aB3cD4eF5gH6iJ7kL8mN9oP0qR"; // gitleaks:allow

    private static readonly Guid UserId = new("6e6b1f3a-3c2d-4a8f-9b1e-7d0c5a2e4f11");

    /// <summary>
    /// Every element that can make a mail client issue a network request, plus the CSS forms that do
    /// the same without an element. Shape-based, not name-based: the check is for the TAG OPENING, so
    /// attribute soup or an unusual attribute order cannot dodge it.
    /// </summary>
    private static readonly string[] FetchingElements =
        ["<img", "<script", "<style", "<link", "<iframe", "<video", "<audio", "<source", "<object",
         "<embed", "<picture", "<track", "<input"];

    /// <summary>
    /// CSS and attribute forms that fetch without one of the elements above: <c>@import</c>, any
    /// <c>url(...)</c> (background images), and the source attributes.
    /// </summary>
    private static readonly string[] FetchingConstructs =
        ["@import", "url(", "src=", "srcset=", "background=", "poster="];

    private static readonly Regex AbsoluteUrl = new(
        @"(?:https?:)?//[^\s""'<>()]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Every span that is live markup: <c>&lt;</c> through the next <c>&gt;</c>.</summary>
    private static readonly Regex TagSpan = new(
        @"<[^>]*>", RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns one line per violation, empty when the document references nothing remote. Public and
    /// static because <c>SesEmailSenderTests</c> runs the same detector against the HTML that
    /// actually reaches the <c>SendEmailRequest</c>, so the seam and the templates are judged by one
    /// definition rather than by two that can drift.
    ///
    /// <para>
    /// <b>Element checks run over the whole document; attribute and URL checks run over LIVE MARKUP
    /// ONLY, and that distinction is the detector's correctness rather than a loosening.</b> The
    /// property being defended is "this document cannot make the recipient's client fetch anything,
    /// and points nowhere off-host". A URL sitting in TEXT does neither: no client fetches it, and it
    /// renders as characters. Ad text reaches these templates from JobTech and is HTML-encoded on the
    /// way in, so a hostile <c>&lt;img src="…"&gt;</c> in a company name arrives as
    /// <c>&amp;lt;img src=…</c> — which still contains the literal characters <c>src=</c> and a URL,
    /// while being inert. Scanning the whole document for those two would report a finding about a
    /// string that cannot fetch, and the first honest fixture carrying an injected payload would then
    /// force whoever met it to weaken the detector to get green. That is how a GDPR pin turns into a
    /// pin nobody trusts.
    /// </para>
    /// <para>
    /// The element list stays whole-document because encoding already separates the cases for it: a
    /// live tag opening is <c>&lt;img</c> and an encoded one is <c>&amp;lt;img</c>, so the literal
    /// <c>&lt;img</c> appears if and only if the markup is live. Losing the encoding is therefore
    /// caught by the element arm, and
    /// <see cref="FindRemoteResources_WhenEncodingIsLost_ReportsIt"/> proves exactly that.
    /// </para>
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

        var liveMarkup = string.Join(
            "\n", TagSpan.Matches(html).Select(m => m.Value));

        foreach (var construct in FetchingConstructs)
        {
            if (liveMarkup.Contains(construct, StringComparison.OrdinalIgnoreCase))
                findings.Add($"fetching construct: {construct}");
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
    /// explicit scheme. Trims userinfo, port, path, query and fragment, so the comparison in
    /// <see cref="FindRemoteResources"/> is against a bare host and can be an equality test.
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

    // ---------- fixtures: one per template, each mirroring a production call site ----------

    /// <summary>
    /// All eight templates, rendered the way their production callers render them (the shapes are
    /// <c>BackgroundMatchingJob</c>'s, <c>DigestDispatchJob</c>'s, <c>RegisterCommandHandler</c>'s,
    /// <c>ChangeEmailCommandHandler</c>'s and the reset endpoints').
    /// <c>MatchNotification</c> appears twice because its two Kind branches build different subjects,
    /// intros and bodies, and the digest form is the only one that reaches the "och N till" tail;
    /// <c>FollowedCompanyNotification</c> appears twice because the filter disclosure is a separate
    /// rendering path that is absent when no filter is active.
    /// </summary>
    private static List<(string Name, string Html)> RenderAll() =>
    [
        ("MatchNotification/direct",
            EmailTemplates.MatchNotification(
                BaseUrl,
                new MatchNotificationEmail(
                    MatchNotificationKind.Direct,
                    Cadence: null,
                    Items: [new MatchNotificationItem("Backend-utvecklare", "Acme AB", "Toppmatch")],
                    TotalCount: 1)).HtmlBody),

        ("MatchNotification/digest",
            EmailTemplates.MatchNotification(
                BaseUrl,
                new MatchNotificationEmail(
                    MatchNotificationKind.Digest,
                    DigestCadence.Weekly,
                    Items:
                    [
                        new MatchNotificationItem("Backend-utvecklare", "Acme AB", "Stark matchning"),
                        new MatchNotificationItem("Systemutvecklare", "Bolaget & Söner AB", "Stark matchning"),
                    ],
                    TotalCount: 5)).HtmlBody),

        ("FollowedCompanyNotification/unfiltered",
            EmailTemplates.FollowedCompanyNotification(
                BaseUrl,
                new FollowedCompanyNotificationEmail(
                    DigestCadence.Weekly,
                    Items: [new FollowedCompanyAdItem("Backend-utvecklare", "Acme AB")],
                    TotalCount: 1)).HtmlBody),

        ("FollowedCompanyNotification/filtered",
            EmailTemplates.FollowedCompanyNotification(
                BaseUrl,
                new FollowedCompanyNotificationEmail(
                    DigestCadence.Daily,
                    Items: [new FollowedCompanyAdItem("Backend-utvecklare", "Acme AB")],
                    TotalCount: 4,
                    new FollowedCompanyFilterSummary(
                        OnlyMatchedActive: true, LocationFilterActive: true))).HtmlBody),

        ("EmailConfirmation",
            EmailTemplates.EmailConfirmation(
                BaseUrl, new EmailConfirmationEmail(UserId, UrlSafeToken)).HtmlBody),

        ("EmailChangeConfirmation",
            EmailTemplates.EmailChangeConfirmation(
                BaseUrl,
                new EmailChangeConfirmationEmail(
                    UserId, "ny.adress@example.com", UrlSafeToken)).HtmlBody),

        ("EmailChangedNotification", EmailTemplates.EmailChangedNotification(BaseUrl).HtmlBody),

        ("AccountExistsNotice", EmailTemplates.AccountExistsNotice(BaseUrl).HtmlBody),

        ("PasswordReset",
            EmailTemplates.PasswordReset(
                BaseUrl, new PasswordResetEmail(UserId, UrlSafeToken)).HtmlBody),

        ("PasswordChangedNotice", EmailTemplates.PasswordChangedNotice(BaseUrl).HtmlBody),
    ];

    public static TheoryData<string, string> AllTemplates()
    {
        var data = new TheoryData<string, string>();
        foreach (var (name, html) in RenderAll())
            data.Add(name, html);
        return data;
    }

    // ---------- the pin ----------

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void EmailHtml_ForEveryTemplate_ReferencesNoRemoteResource(string name, string html)
    {
        html.ShouldNotBeNullOrWhiteSpace($"{name} rendered an empty HTML part");

        FindRemoteResources(html, BaseUrl)
            .ShouldBeEmpty($"{name} references a remote resource, which falsifies the Art. 30 "
                + "retention entry's ground 2 — re-measure the register before shipping this");
    }

    [Fact]
    public void EmailHtml_TheTemplateSet_CoversAllEightPortMethods()
    {
        // Guards the Theory above against shrinking silently: a Theory over a list that lost entries
        // still passes, and would then report "no remote resources" about templates it never
        // rendered. Eight port methods, ten cases (two templates contribute a second branch each).
        var cases = RenderAll();

        cases.Count.ShouldBe(10);
        cases.Select(c => c.Name.Split('/')[0]).Distinct().Count().ShouldBe(
            8, "one case per IEmailSender.Send* method, and there are eight");
    }

    // ---------- the injection case: third-party ad text cannot smuggle markup in ----------

    [Fact]
    public void EmailHtml_WhenAdTextCarriesMarkup_EncodesItRatherThanEmittingIt()
    {
        // Job titles and company names arrive from JobTech ad data, which this codebase does not
        // author. Unencoded, a crafted company name injects markup into a mail we sign with our own
        // DKIM — including the very <img> ground 2 forbids, which would turn our own send into the
        // tracking capability the register says does not exist. This is the case that makes the
        // encoding load-bearing rather than tidy.
        var hostile = """<img src="https://evil.example/pixel.gif" onerror="alert(1)">""";

        var html = EmailTemplates.MatchNotification(
            BaseUrl,
            new MatchNotificationEmail(
                MatchNotificationKind.Direct,
                Cadence: null,
                Items: [new MatchNotificationItem(hostile, $"Acme {hostile} AB", "Toppmatch")],
                TotalCount: 1)).HtmlBody;

        FindRemoteResources(html, BaseUrl).ShouldBeEmpty();

        // No LIVE tag: the payload cannot fetch. Asserted as the absence of the unencoded opening,
        // which is the only form a client acts on.
        html.ShouldNotContain("<img");

        // And present as inert TEXT, so the encoding is proven to have HAPPENED rather than the
        // payload having been dropped: a template that silently discarded the field would pass the
        // two assertions above while proving nothing about encoding at all.
        html.ShouldContain("&lt;img");
        html.ShouldContain("evil.example");
    }

    [Fact]
    public void FindRemoteResources_WhenEncodingIsLost_ReportsIt()
    {
        // The counterfactual that makes the test above non-vacuous, and the single most important
        // arm in this file. `EmailHtml.Encode` is what stands between third-party ad text and live
        // markup in a mail we DKIM-sign. If it were ever removed, the same hostile company name
        // would reach the document as a live <img> — exactly the tracking pixel the register says
        // does not exist. This feeds the detector that un-encoded document and requires a finding,
        // so "the injection test is green" can never mean "the detector cannot see an injection".
        var hostile = """<img src="https://evil.example/pixel.gif">""";

        FindRemoteResources(Wrap($"<p>Acme {hostile} AB</p>"), BaseUrl).ShouldNotBeEmpty();
    }

    // ---------- counterfactuals: every detector arm is proven able to fail ----------

    [Theory]
    [InlineData("""<p><img src="https://tracker.example/p.gif"></p>""")]
    [InlineData("""<p><script src="https://cdn.example/a.js"></script></p>""")]
    [InlineData("""<link rel="stylesheet" href="https://cdn.example/a.css">""")]
    [InlineData("""<style>@import url("https://fonts.example/f.css");</style>""")]
    [InlineData("""<td background="https://cdn.example/bg.png">x</td>""")]
    [InlineData("""<div style="background-image:url('https://cdn.example/bg.png')">x</div>""")]
    [InlineData("""<iframe src="https://evil.example/"></iframe>""")]
    public void FindRemoteResources_WhenTheDocumentFetches_ReportsIt(string fragment)
    {
        FindRemoteResources(Wrap(fragment), BaseUrl).ShouldNotBeEmpty();
    }

    [Fact]
    public void FindRemoteResources_WhenAnAnchorPointsOffHost_ReportsIt()
    {
        // A bare off-host link fetches nothing on its own, and is still a finding: it is how a click
        // tracker or a redirector gets in, and "no absolute URL whose host lies outside BaseUrl" is
        // the form security-auditor's condition 1 names. Both the explicit-scheme and the
        // protocol-relative form must be caught, since the second is the one that looks like a path.
        FindRemoteResources(Wrap("""<a href="https://sponsor.example/x">x</a>"""), BaseUrl)
            .ShouldNotBeEmpty();
        FindRemoteResources(Wrap("""<a href="//sponsor.example/x">x</a>"""), BaseUrl)
            .ShouldNotBeEmpty();
    }

    [Fact]
    public void FindRemoteResources_WhenAHostMerelyLooksLikeOurs_ReportsIt()
    {
        // The host comparison is exact, not a suffix or prefix test: `jobbliggaren.se.evil.example`
        // and `evil-jobbliggaren.se` both contain the allowed host as a substring, and a detector
        // built on Contains would wave both through. The third form hides the real host behind
        // userinfo, which is the classic way a URL is read wrong by eye.
        FindRemoteResources(Wrap("""<a href="https://jobbliggaren.se.evil.example/x">x</a>"""), BaseUrl)
            .ShouldNotBeEmpty();
        FindRemoteResources(Wrap("""<a href="https://evil-jobbliggaren.se/x">x</a>"""), BaseUrl)
            .ShouldNotBeEmpty();
        FindRemoteResources(Wrap("""<a href="https://jobbliggaren.se@evil.example/x">x</a>"""), BaseUrl)
            .ShouldNotBeEmpty();
    }

    [Fact]
    public void FindRemoteResources_WhenEverythingIsOnHost_ReportsNothing()
    {
        // The control that keeps the counterfactuals above honest: the detector must not be one that
        // rejects every input. Without this, all nine arms would also "pass" against a detector
        // hard-wired to report a finding.
        FindRemoteResources(
            Wrap("""<a href="https://jobbliggaren.se/jobb">Öppna annonserna</a>"""), BaseUrl)
            .ShouldBeEmpty();
    }

    private static string Wrap(string fragment) =>
        $"<!DOCTYPE html><html lang=\"sv\"><body>{fragment}</body></html>";
}
