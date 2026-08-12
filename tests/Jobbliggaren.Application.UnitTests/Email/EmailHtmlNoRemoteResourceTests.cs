using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Common.Abstractions;
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
/// detector — closed by counterfactuals per arm PLUS a control document that must produce NO finding,
/// PLUS one that crosses a tag boundary, the case the first version of this file missed entirely.
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

    // Same Base64Url shape ASP.NET Identity emits, and the same fixture SesEmailSenderTests uses.
    // Not a real token: no account exists that it could activate. gitleaks:allow
    private const string UrlSafeToken = "CfDJ8Nr-9xQvT0pLm2Zq_aB3cD4eF5gH6iJ7kL8mN9oP0qR"; // gitleaks:allow

    private static readonly Guid UserId = new("6e6b1f3a-3c2d-4a8f-9b1e-7d0c5a2e4f11");

    /// <summary>
    /// The cap both dispatch paths apply (<c>DigestDispatchOptions.MaxItemsPerDigest</c>, default 20).
    /// It is why the "och N till" fixtures below carry exactly twenty items: a remainder can only
    /// exist once the body is full, so a 2-items-of-5 fixture describes a mail production cannot
    /// build (code-reviewer Major 4, 2026-08-12).
    /// </summary>
    private const int MaxItemsPerDigest = 20;

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

        ("EmailChangedNotification", EmailTemplates.EmailChangedNotification(BaseUrl)),

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
        // SesEmailSenderTests against the TEXT part; if the HTML part dropped it, a notification read
        // in an HTML client — which is nearly every recipient — would carry no way to turn the mails
        // off, with a green suite.
        //
        // The oracle is the text part rather than a hand-written list, so the two parts are compared
        // against each other and a link added to one is required in the other.
        var content = Case(name);
        var textLinks = TextUrl.Matches(content.PlainTextBody).Select(m => m.Value).ToList();

        textLinks.ShouldNotBeEmpty($"{name}: the text part carries no link, so this fact would be "
            + "vacuous — every template in this codebase carries at least one");

        foreach (var link in textLinks)
        {
            // Encoded, because the HTML part carries these in attribute context where `&` is `&amp;`.
            content.HtmlBody.ShouldContain(
                WebUtility.HtmlEncode(link),
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
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
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
            part.ShouldContain("så annonser du inte matchar visas inte här");
            part.ShouldContain("så annonser i andra orter visas inte här");
        }
    }

    [Fact]
    public void EmailHtml_WhenNoWatchIsFiltered_DisclosesNothingInEitherPart()
    {
        var content = Case("FollowedCompanyNotification/unfiltered");

        foreach (var part in new[] { content.PlainTextBody, content.HtmlBody })
        {
            part.ShouldNotContain("visas inte här");
            part.ShouldNotContain("Du ser och ändrar filtren");
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
