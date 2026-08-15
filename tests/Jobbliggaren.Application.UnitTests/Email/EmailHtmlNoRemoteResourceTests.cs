using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.DigestDispatch;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// GROUND 2 of the Art. 30 retention claim for "Utgående transaktionell e-post", pinned over ALL
/// EIGHT templates, plus the content facts that keep the absence assertion from being vacuous
/// (#183, 2026-08-12 — security-auditor condition 1).
///
/// <para>
/// <b>What this file defends.</b> The register states as MEASURED FACT that SES's 60-day,
/// RECIPIENT-LEVEL open/click metrics do not arise for us. Until 2026-08-12 that rested partly on
/// "the body is Body.Text with no HTML part" — a ground the HTML templates struck. The replacement is
/// the property asserted here: the rendered HTML references no remote resource. It was chosen because
/// it has the same shape as the ground it replaces (unattackable from outside the repo, pinned rather
/// than complied with) and it is STRONGER than the SES question it stands in for, since a remote
/// resource in an HTML mail is a tracking capability regardless of provider (EDPB Guidelines 2/2023
/// on Art. 5(3) ePrivacy). The forbidden set itself lives in <see cref="RemoteResourceDetector"/>.
/// </para>
///
/// <para>
/// <b>A failure here is a register defect before it is a test failure.</b> If a template gains a
/// remote resource, the fix is not to relax the detector: it is to re-measure the register's
/// retention entry BEFORE that change ships.
/// </para>
///
/// <para>
/// <b>The four ways this suite could have passed vacuously, and what closes each.</b> (1) A broken
/// detector — closed in <c>RemoteResourceDetectorTests</c>, which carries a probe for every literal
/// in every arm (isolated with on-host URLs and asserted on the finding STRING, so no probe can be
/// satisfied by a different arm), two control documents that must produce NO finding, and probes that
/// cross a tag boundary, the case the first version missed entirely.
/// (2) A shrinking fixture list — closed by <see cref="EmailHtml_TheTemplateSet_CoversEveryTemplateMethod"/>,
/// which compares against reflection over <see cref="EmailTemplates"/> rather than a hardcoded count,
/// so it also catches a template ADDED without a fixture (the direction a hardcoded count is blind to,
/// and the direction that has historically happened). (3) An empty or link-less HTML body — closed by
/// <see cref="EmailHtml_ForEveryTemplate_CarriesEveryLinkItsTextPartCarries"/>: an absence assertion
/// gets GREENER as content disappears, so it needs a presence assertion in front of it. (4) A
/// disclosure that appears in only one part — closed by the filter-parity facts.
/// </para>
/// </summary>
public class EmailHtmlNoRemoteResourceTests
{
    private const string BaseUrl = "https://jobbliggaren.se";

    // Same Base64Url shape ASP.NET Identity emits, and the same fixture ScalewayEmailSenderTests uses.
    // Not a real token: no account exists that it could activate. gitleaks:allow
    private const string UrlSafeToken = "CfDJ8Nr-9xQvT0pLm2Zq_aB3cD4eF5gH6iJ7kL8mN9oP0qR"; // gitleaks:allow

    private static readonly Guid UserId = new("6e6b1f3a-3c2d-4a8f-9b1e-7d0c5a2e4f11");

    /// <summary>The encoder production uses, so the oracle normalises identically.</summary>
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>
    /// The cap both dispatch paths apply, READ from the options type rather than copied: a number
    /// transcribed into a test is a live measurement in a tracked file and decays silently. It is why
    /// the "och N till" fixtures below fill the body exactly — a remainder can only exist once the
    /// body is full, so a 2-items-of-5 fixture describes a mail production cannot build
    /// (code-reviewer Major 4 and Minor, 2026-08-12).
    /// </summary>
    private static readonly int MaxItemsPerDigest = new DigestDispatchOptions().MaxItemsPerDigest;

    /// <summary>Absolute links as the plain-text parts write them, one per line.</summary>
    private static readonly Regex TextUrl = new(
        @"https://[^\s]+", RegexOptions.CultureInvariant);

    // ---------- fixtures: one per template, each mirroring a production call site ----------

    /// <summary>
    /// All eight templates rendered the way their production callers render them. The shapes are
    /// <c>BackgroundMatchingJob</c>'s, <c>DigestDispatchJob</c>'s, <c>RegisterCommandHandler</c>'s,
    /// <c>ChangeEmailCommandHandler</c>'s and the reset endpoints'. Grade labels come from
    /// <c>NotifiableMatchGradeLabels</c> verbatim — "Stark match", never "Stark matchning", which no
    /// production path emits.
    /// <para>
    /// Two templates contribute a second case because they have a second rendering path: the digest
    /// branch of <c>MatchNotification</c> (different subject, intro and the "och N till" tail) and the
    /// filtered branch of <c>FollowedCompanyNotification</c> (the disclosure block).
    /// </para>
    /// </summary>
    private static List<(string Name, EmailTemplates.EmailContent Content)> RenderAll() =>
    [
        ("MatchNotification/direct",
            EmailTemplates.MatchNotification(
                BaseUrl,
                new MatchNotificationEmail(
                    MatchNotificationKind.Direct,
                    Cadence: null,
                    Items: [new MatchNotificationItem("Backend-utvecklare", "Acme AB", "Toppmatch")],
                    TotalCount: 1))),

        ("MatchNotification/digest",
            EmailTemplates.MatchNotification(
                BaseUrl,
                new MatchNotificationEmail(
                    MatchNotificationKind.Digest,
                    DigestCadence.Weekly,
                    Items: FullMatchPage(),
                    TotalCount: MaxItemsPerDigest + 3))),

        ("FollowedCompanyNotification/unfiltered",
            EmailTemplates.FollowedCompanyNotification(
                BaseUrl,
                new FollowedCompanyNotificationEmail(
                    DigestCadence.Weekly,
                    Items: [new FollowedCompanyAdItem("Backend-utvecklare", "Acme AB")],
                    TotalCount: 1))),

        ("FollowedCompanyNotification/filtered",
            EmailTemplates.FollowedCompanyNotification(
                BaseUrl,
                new FollowedCompanyNotificationEmail(
                    DigestCadence.Daily,
                    Items: FullFollowPage(),
                    TotalCount: MaxItemsPerDigest + 2,
                    new FollowedCompanyFilterSummary(
                        OnlyMatchedActive: true, LocationFilterActive: true)))),

        ("EmailConfirmation",
            EmailTemplates.EmailConfirmation(
                BaseUrl, new EmailConfirmationEmail(UserId, UrlSafeToken))),

        ("EmailChangeConfirmation",
            EmailTemplates.EmailChangeConfirmation(
                BaseUrl,
                new EmailChangeConfirmationEmail(UserId, "ny.adress@example.com", UrlSafeToken))),

        ("EmailChangedNotification", EmailTemplates.EmailChangedNotification()),

        ("AccountExistsNotice", EmailTemplates.AccountExistsNotice(BaseUrl)),

        ("PasswordReset",
            EmailTemplates.PasswordReset(BaseUrl, new PasswordResetEmail(UserId, UrlSafeToken))),

        ("PasswordChangedNotice", EmailTemplates.PasswordChangedNotice(BaseUrl)),
    ];

    /// <summary>A body filled to the cap, which is the only state in which a remainder can exist.</summary>
    private static List<MatchNotificationItem> FullMatchPage() =>
        [.. Enumerable.Range(1, MaxItemsPerDigest)
            .Select(i => new MatchNotificationItem($"Backend-utvecklare {i}", "Acme AB", "Stark match"))];

    private static List<FollowedCompanyAdItem> FullFollowPage() =>
        [.. Enumerable.Range(1, MaxItemsPerDigest)
            .Select(i => new FollowedCompanyAdItem($"Utvecklare {i}", "Acme AB"))];

    public static TheoryData<string> AllTemplateNames()
    {
        // Carries the NAME only. Passing the rendered document made xUnit serialise ~4 kB of markup
        // into each of ten test display names, which makes the `total:` line CLAUDE.md §7 rests on
        // hard to read (dotnet-architect Nice-to-have 6, 2026-08-12).
        var data = new TheoryData<string>();
        foreach (var (name, _) in RenderAll())
            data.Add(name);
        return data;
    }

    private static EmailTemplates.EmailContent Case(string name) =>
        RenderAll().Single(c => c.Name == name).Content;

    // ---------- the pin ----------

    [Theory]
    [MemberData(nameof(AllTemplateNames))]
    public void EmailHtml_ForEveryTemplate_ReferencesNoRemoteResource(string name)
    {
        var html = Case(name).HtmlBody;
        html.ShouldNotBeNullOrWhiteSpace($"{name} rendered an empty HTML part");

        RemoteResourceDetector.FindRemoteResources(html, BaseUrl)
            .ShouldBeEmpty($"{name} references a remote resource, which falsifies the Art. 30 "
                + "retention entry's ground 2 — re-measure the register before shipping this");
    }

    [Theory]
    [MemberData(nameof(AllTemplateNames))]
    public void EmailHtml_ForEveryTemplate_CarriesEveryLinkItsTextPartCarries(string name)
    {
        // The presence half, without which the absence assertion above is fail-open: a body that lost
        // its Button, or a Document rendered with an empty body, satisfies "not whitespace" AND makes
        // "no remote resources" MORE true. Nothing else in this PR would have noticed.
        //
        // It matters most for the Art. 7(3) unsubscribe route. The settings link is pinned in
        // ScalewayEmailSenderTests against the TEXT part; if the HTML part dropped it, a notification read
        // in an HTML client — which is nearly every recipient — would carry no way to turn the mails
        // off, with a green suite.
        //
        // The oracle is the text part rather than a hand-written list, so the two parts are compared
        // against each other and a link added to one is required in the other.
        var content = Case(name);
        var textLinks = TextUrl.Matches(content.PlainTextBody).Select(m => m.Value).ToList();

        // A ROUTE is an https link OR the contact address — not only a URL. EmailChangedNotification
        // carries no site link at all since 2026-08-12: its only route out is the contact address, by
        // design, because it reaches an address that may no longer control the account and every link
        // is a surface an attacker gets to place in front of the real owner. A URL-only oracle would
        // have gone VACUOUS on exactly that template rather than failing, which is why the
        // non-vacuity guard counts routes and not matches.
        var routes = textLinks.Count
            + (content.PlainTextBody.Contains(EmailTemplates.ContactAddress, StringComparison.Ordinal)
                ? 1 : 0);

        routes.ShouldBeGreaterThan(
            0, $"{name}: the text part offers no way to reach us at all, so this fact would be vacuous");

        if (content.PlainTextBody.Contains(EmailTemplates.ContactAddress, StringComparison.Ordinal))
        {
            content.HtmlBody.ShouldContain(
                EmailTemplates.ContactAddress,
                customMessage: $"{name}: the text part names the contact address and the HTML part "
                    + "does not");
        }

        foreach (var link in textLinks)
        {
            // Encoded with the SAME encoder production uses. WebUtility.HtmlEncode agrees on every
            // link this codebase can build (BaseUrl + Base64Url token + EscapeDataString values are
            // pure ASCII), but the two normalisers differ on `'`, `+` and everything from U+00A0 up,
            // and a rule with two normalisers is two rules (dotnet-architect + code-reviewer,
            // 2026-08-12).
            content.HtmlBody.ShouldContain(
                Encoder.Encode(link),
                customMessage: $"{name}: the text part links to {link} and the HTML part does not");
        }
    }

    [Fact]
    public void EmailHtml_TheTemplateSet_CoversEveryTemplateMethod()
    {
        // Compared against REFLECTION, not against a hardcoded count. A count closes shrinkage only:
        // add a ninth template with no fixture and a `ShouldBe(10)` stays green while the Theory above
        // never renders it, so the register would claim a measured property over a template nothing
        // measured. That is the direction that actually happened once already — PasswordReset and
        // PasswordChangedNotice reached production without an Art. 30 entry (dotnet-architect Viktigt 2
        // / code-reviewer Major 7, 2026-08-12).
        var templateMethods = typeof(EmailTemplates)
            // NonPublic included: EmailTemplates is itself internal, so an internal template method
            // is one access modifier away and would otherwise be invisible to this guard — the exact
            // growth direction it exists to catch (dotnet-architect, 2026-08-12).
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(EmailTemplates.EmailContent))
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        templateMethods.ShouldNotBeEmpty("reflection found no templates, so this fact is vacuous");

        RenderAll()
            .Select(c => c.Name.Split('/')[0])
            .ToHashSet(StringComparer.Ordinal)
            .ShouldBe(templateMethods, ignoreOrder: true,
                "every EmailTemplates method must have at least one fixture case here");
    }

    // ---------- the filter disclosure must appear in BOTH parts, or in neither ----------

    [Fact]
    public void EmailHtml_WhenAWatchIsFiltered_DisclosesItInBothParts()
    {
        // BuildFilterDisclosureHtml's doc says the two parts "must fall silent together", because a
        // disclosure carried by only one part is one the recipient may never see. Nothing pinned that
        // (code-reviewer Major 5, 2026-08-12).
        var content = Case("FollowedCompanyNotification/filtered");

        foreach (var part in new[] { content.PlainTextBody, content.HtmlBody })
        {
            // ONE sentence covering both axes since 2026-08-12 (Klas-beslut). It names no axis, which
            // is what makes it true under the summary's ANY-semantics over every active watch.
            part.ShouldContain("Några annonser kan saknas");
            part.ShouldContain("Ändra filtren under Företag");
        }
    }

    [Fact]
    public void EmailHtml_WhenNoWatchIsFiltered_DisclosesNothingInEitherPart()
    {
        var content = Case("FollowedCompanyNotification/unfiltered");

        // These two strings were the OLD per-axis copy, which this change deleted from production —
        // so after it, the assertions could never fail and the fact was vacuously green while the
        // positive sibling had already been updated. It is the one place in the repo that pins the
        // HTML part's SILENCE when no filter contributed, and "the two parts must fall silent
        // together" rested on it (design-reviewer Major 4 / code-reviewer Major 3, 2026-08-12).
        foreach (var part in new[] { content.PlainTextBody, content.HtmlBody })
        {
            part.ShouldNotContain("Några annonser kan saknas");
            part.ShouldNotContain("Ändra filtren under Företag");
        }
    }

    [Theory]
    [InlineData("EmailChangedNotification")]
    [InlineData("AccountExistsNotice")]
    [InlineData("PasswordChangedNotice")]
    public void EmailHtml_ForTheTokenFreeNotices_CarriesNoToken(string name)
    {
        // The HTML counterpart of the text part's own no-token assertions. These three are security
        // notices sent to an address that may not have requested anything, so a link that grants
        // access must not appear in either part.
        Case(name).HtmlBody.ShouldNotContain(UrlSafeToken);
    }

    // ---------- the injection case: third-party ad text cannot smuggle markup in ----------

    [Fact]
    public void EmailHtml_WhenAdTextCarriesMarkup_EncodesItRatherThanEmittingIt()
    {
        // Job titles and company names arrive from JobTech, which this codebase does not author:
        // PlatsbankenJobSource puts hit.Employer?.Name?.Trim() straight into CompanyName and the
        // payload sanitizer never runs on it, so this premise is producible (code-reviewer measured
        // that path, 2026-08-12). Unencoded, a crafted company name injects markup into a mail we sign
        // with our own DKIM — including the very <img> ground 2 forbids.
        var hostile = """<img src="https://evil.example/pixel.gif" onerror="alert(1)">""";

        var html = EmailTemplates.MatchNotification(
            BaseUrl,
            new MatchNotificationEmail(
                MatchNotificationKind.Direct,
                Cadence: null,
                Items: [new MatchNotificationItem(hostile, $"Acme {hostile} AB", "Toppmatch")],
                TotalCount: 1)).HtmlBody;

        RemoteResourceDetector.FindRemoteResources(html, BaseUrl).ShouldBeEmpty();

        // No LIVE tag: the payload cannot fetch. The unencoded opening is the only form a client acts on.
        html.ShouldNotContain("<img");

        // And present as inert TEXT, so the encoding is proven to have HAPPENED rather than the
        // payload having been dropped: a template that silently discarded the field would pass the
        // two assertions above while proving nothing about encoding.
        html.ShouldContain("&lt;img");
        html.ShouldContain("evil.example");
    }

    [Fact]
    public void EmailHtml_TheCallToAction_KeepsPaddingOnTheAnchorAndGivesWordItsOwn()
    {
        // The click target collapsed once already and NO test went red (design-reviewer, 2026-08-12).
        // Round one moved the button's padding to the cell so Word would paint it; Word does honour
        // cell padding, and the anchor then had padding nowhere, so the clickable box shrank to the
        // label's own text box while the painted button stayed ~43px tall. Moving it back to the cell
        // today would still pass CI green, which is precisely why this fact exists — the PR's own
        // thesis is "a ground a test can hold", and it applies to the repair as much as to the rule.
        //
        // The invariant: each engine gets exactly ONE padding. The anchor carries real padding for
        // every client; the cell carries mso-padding-alt, which only Word reads.
        var html = Case("EmailConfirmation").HtmlBody;

        html.ShouldContain(
            "padding:12px 22px;mso-padding-alt:0",
            customMessage: "the anchor lost its padding, so the clickable area is the label's text "
                + "box while the painted button is ~43px tall");
        html.ShouldContain(
            "mso-padding-alt:12px 22px",
            customMessage: "the cell lost its Word padding, so Outlook paints a fill with the label "
                + "jammed against its edges");

        // And the cell must NOT carry ordinary padding as well: two paddings in Word is the other
        // failure mode, and it is invisible in every client that ignores mso-*.
        html.ShouldNotContain("border-radius:6px;padding:");
    }

    [Fact]
    public void EmailHtml_WhenAValueWouldReachAnAttribute_EscapesTheQuote()
    {
        // Asserts the PRIMITIVE's transform, not a claim about any mail: no production path puts
        // third-party text into an attribute today (security-auditor read all 14 call sites,
        // 2026-08-12). This is what keeps that safe if one ever does — an unescaped double quote
        // would close the href and turn the rest of the value into attributes.
        var markup = EmailHtml.Button($"{BaseUrl}/jobb\" onmouseover=\"alert(1)", "Öppna").ToString();

        markup.ShouldNotContain("onmouseover=\"alert");
        markup.ShouldContain("&quot;");
    }
}
