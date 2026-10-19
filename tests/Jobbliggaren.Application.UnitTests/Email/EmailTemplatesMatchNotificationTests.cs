using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// ADR 0080 Vag 4 PR-4a — locks the invariants of <see cref="EmailTemplates.MatchNotification"/>
/// (Infrastructure-internal, reachable via InternalsVisibleTo). These tests are NOT RED-first;
/// the production template already compiles. They pin the GDPR + civic-tone + Goodhart
/// invariants so a future edit cannot silently regress them.
///
/// The single load-bearing GDPR invariant: the settings/unsubscribe link
/// (<c>{baseUrl}/mina-sidor/notiser</c>, Art. 7(3) — withdrawal must be as easy as giving consent)
/// is ALWAYS present in EVERY rendered body, for both kinds, regardless of item count. The
/// body must never carry PII (a recipient address, a personnummer, a score) — only public
/// ad fields + grade labels.
/// </summary>
public class EmailTemplatesMatchNotificationTests
{
    private const string BaseUrl = "https://jobbliggaren.se";

    private static readonly Guid AdId = Guid.Parse("0b7f3c1e-5a2d-4e8f-9c61-2d4a8e7b3f10");

    private static MatchNotificationItem Item(
        string title = "Backend-utvecklare",
        string company = "Acme AB",
        string grade = "Toppmatch",
        Guid? jobAdId = null) => new(jobAdId ?? AdId, title, company, grade);

    private static MatchNotificationEmail Direct(
        params MatchNotificationItem[] items) =>
        new(MatchNotificationKind.Direct, Cadence: null, Items: items, TotalCount: items.Length);

    private static MatchNotificationEmail Digest(
        int totalCount, DigestCadence cadence, params MatchNotificationItem[] items) =>
        new(MatchNotificationKind.Digest, cadence, Items: items, TotalCount: totalCount);

    // --- GDPR Art. 7(3): mandatory settings/unsubscribe link (HARD INVARIANT) ---

    [Fact]
    public void MatchNotification_ShouldAlwaysContainSettingsLink_ForDirectKind()
    {
        var content = Direct(Item());

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.PlainTextBody.ShouldContain($"{BaseUrl}/mina-sidor/notiser");
        email.HtmlBody.ShouldContain($"href=\"{BaseUrl}/mina-sidor/notiser\"");
    }

    [Fact]
    public void MatchNotification_ShouldAlwaysContainSettingsLink_ForDigestKind()
    {
        var content = Digest(3, DigestCadence.Weekly, Item(), Item("Frontend", "Beta AB", "Stark match"));

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.PlainTextBody.ShouldContain($"{BaseUrl}/mina-sidor/notiser");
        email.HtmlBody.ShouldContain($"href=\"{BaseUrl}/mina-sidor/notiser\"");
    }

    [Fact]
    public void MatchNotification_ShouldContainSettingsLink_EvenWhenSingleItem()
    {
        // Edge: a one-item Direct notice must still carry the withdrawal link.
        var content = Direct(Item());

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.PlainTextBody.ShouldContain($"{BaseUrl}/mina-sidor/notiser");
    }

    [Theory]
    [InlineData("https://jobbliggaren.se/")]
    [InlineData("https://jobbliggaren.se")]
    public void MatchNotification_ShouldNotDoubleSlashSettingsLink_WhenBaseUrlHasTrailingSlash(string baseUrl)
    {
        var email = EmailTemplates.MatchNotification(baseUrl, Direct(Item()));

        email.PlainTextBody.ShouldContain("https://jobbliggaren.se/mina-sidor/notiser");
        email.PlainTextBody.ShouldNotContain("se//mina-sidor");
    }

    // --- Row 14 of ADR 0144 Decision 4, as security-auditor signed it per mail type (#2107, V6) ---
    // The plain part is hard-wrapped, so both parts are compared paragraph by paragraph with whitespace
    // collapsed (MailText); the words and the punctuation stay verbatim.

    private const string DirectReasonPlain =
        "Du får detta för att du har slagit på matchningen och ”Toppmatchningar via e-post” är på. "
        + "Du kan stänga av mejlen i dina inställningar:";

    private const string DirectReasonHtml =
        "Du får detta för att du har slagit på matchningen och ”Toppmatchningar via e-post” är på. "
        + "Du kan stänga av mejlen:";

    private const string DigestReasonPlain =
        "Du får detta för att du har slagit på matchningen och ”Sammanfattning via e-post” är på. "
        + "Du kan ändra hur ofta du får den, eller stänga av den, i dina inställningar:";

    private const string DigestReasonHtml =
        "Du får detta för att du har slagit på matchningen och ”Sammanfattning via e-post” är på. "
        + "Du kan ändra hur ofta du får den, eller stänga av den:";

    private const string SettingsLinkText = "Ändra dina inställningar";

    [Fact]
    public void MatchNotification_ForADirectMail_CarriesTheSignedReasonVerbatimInBothParts()
    {
        var email = EmailTemplates.MatchNotification(BaseUrl, Direct(Item()));

        MailText.PlainParagraphs(email.PlainTextBody)
            .ShouldContain($"{DirectReasonPlain} {BaseUrl}/mina-sidor/notiser");
        MailText.HtmlParagraphs(email.HtmlBody).ShouldContain($"{DirectReasonHtml} {SettingsLinkText}");
        email.HtmlBody.ShouldContain(
            EmailHtml.LinkParagraph(DirectReasonHtml, $"{BaseUrl}/mina-sidor/notiser", SettingsLinkText).ToString());
    }

    [Fact]
    public void MatchNotification_ForADigest_CarriesTheSignedReasonVerbatimInBothParts()
    {
        var email = EmailTemplates.MatchNotification(
            BaseUrl, Digest(2, DigestCadence.Weekly, Item(), Item("Frontend", "Beta AB", "Stark match")));

        MailText.PlainParagraphs(email.PlainTextBody)
            .ShouldContain($"{DigestReasonPlain} {BaseUrl}/mina-sidor/notiser");
        MailText.HtmlParagraphs(email.HtmlBody).ShouldContain($"{DigestReasonHtml} {SettingsLinkText}");
        email.HtmlBody.ShouldContain(
            EmailHtml.LinkParagraph(DigestReasonHtml, $"{BaseUrl}/mina-sidor/notiser", SettingsLinkText).ToString());
    }

    // No cadence applies to a Toppmatch mail, so it never offers to change how often it comes.
    [Fact]
    public void MatchNotification_ForADirectMail_NeverMentionsHowOften()
    {
        foreach (var email in new[]
                 {
                     EmailTemplates.MatchNotification(BaseUrl, Direct(Item())),
                     EmailTemplates.MatchNotification(BaseUrl, BundledDirect()),
                 })
        {
            MailText.Unwrapped(email.PlainTextBody).ShouldNotContain("hur ofta");
            MailText.Unwrapped(email.HtmlBody).ShouldNotContain("hur ofta");
        }
    }

    [Theory]
    [InlineData(MatchNotificationKind.Direct, "Toppmatchningar via e-post")]
    [InlineData(MatchNotificationKind.Digest, "Sammanfattning via e-post")]
    public void MatchNotification_QuotesTheSwitchLabelWithU201DOnBothSidesInBothParts(
        MatchNotificationKind kind, string label)
    {
        var email = EmailTemplates.MatchNotification(
            BaseUrl, kind == MatchNotificationKind.Direct ? Direct(Item()) : Digest(1, DigestCadence.Daily, Item()));

        var reasons = new[]
        {
            MailText.PlainParagraphs(email.PlainTextBody).Single(paragraph => paragraph.StartsWith("Du får detta", StringComparison.Ordinal)),
            MailText.HtmlParagraphs(email.HtmlBody).Single(paragraph => paragraph.StartsWith("Du får detta", StringComparison.Ordinal)),
        };
        foreach (var reason in reasons)
        {
            reason.ShouldContain("\u201D" + label + "\u201D");
            string.Concat(reason.Where(IsQuotationMark)).ShouldBe("\u201D\u201D");
        }
    }

    private static bool IsQuotationMark(char c) => c is '"' or '\u201C' or '\u201D' or '\u201E' or '\u00AB' or '\u00BB';

    // --- The action link: a direct mail opens its ad, a digest the list (#2094) ---

    [Fact]
    public void MatchNotification_ForADirectMatch_LinksItsAdInBothParts()
    {
        var email = EmailTemplates.MatchNotification(BaseUrl, Direct(Item()));

        email.PlainTextBody.Replace("\r\n", "\n", StringComparison.Ordinal)
            .ShouldContain($"Öppna annonsen:\n{BaseUrl}/jobb/{AdId:D}");
        email.HtmlBody.ShouldContain($"href=\"{BaseUrl}/jobb/{AdId:D}\"");
        email.HtmlBody.ShouldContain(">Öppna annonsen<");
        email.PlainTextBody.ShouldNotContain($"{BaseUrl}/matchningar");
        email.HtmlBody.ShouldNotContain($"{BaseUrl}/matchningar");
    }

    [Fact]
    public void MatchNotification_ForADigest_LinksTheMatchesList()
    {
        var email = EmailTemplates.MatchNotification(
            BaseUrl, Digest(2, DigestCadence.Weekly, Item(), Item("Frontend", "Beta AB", "Stark match")));

        email.PlainTextBody.ShouldContain($"{BaseUrl}/matchningar");
        email.HtmlBody.ShouldContain($"href=\"{BaseUrl}/matchningar\"");
        email.HtmlBody.ShouldContain(">Öppna dina matchningar<");
        email.HtmlBody.ShouldNotContain($"{BaseUrl}/jobb/");
    }

    [Fact]
    public void MatchNotification_ForADirectMatch_LeadsTheBodyAndThePreviewWithTheMatchItself()
    {
        // A direct mail with one match leads with the match: its first line and its preheader, with no
        // intro repeating the subject (design-reviewer, #1825 form round, Major 3).
        const string Line = "Systemutvecklare, Volvo Cars (Toppmatch)";
        var email = EmailTemplates.MatchNotification(BaseUrl, Direct(Item("Systemutvecklare", "Volvo Cars", "Toppmatch")));

        email.PlainTextBody.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[0].ShouldBe(Line);
        email.HtmlBody.ShouldContain(">" + Line + EmailHtml.PreheaderFiller + "</div>");
        email.HtmlBody.Split(Line).Length.ShouldBe(3, "the line is both the preheader and the first paragraph");
        email.HtmlBody[email.HtmlBody.IndexOf("<p ", StringComparison.Ordinal)..].ShouldStartWith(EmailHtml.P(Line).ToString());
        email.PlainTextBody.ShouldNotContain("Bakgrundsmatchningen");
        email.HtmlBody.ShouldNotContain("Bakgrundsmatchningen");
        email.HtmlBody.ShouldNotContain("<ul");
    }

    [Fact]
    public void MatchNotification_ShouldRenderEveryItem_WhenMultipleItemsGiven()
    {
        var content = Digest(
            totalCount: 2,
            DigestCadence.Weekly,
            Item("Backend-utvecklare", "Acme AB", "Stark match"),
            Item("DevOps-ingenjör", "Beta AB", "Stark match"));

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.PlainTextBody.ShouldContain("- Backend-utvecklare, Acme AB (Stark match)");
        email.PlainTextBody.ShouldContain("- DevOps-ingenjör, Beta AB (Stark match)");
    }

    // --- A direct mail bundling several Top matches of one run (ADR 0161) ---

    [Fact]
    public void MatchNotification_ShouldCarryNoLinkOrAdIdInTheSubject_ForEitherKind()
    {
        foreach (var content in new[]
                 {
                     Direct(Item()),
                     BundledDirect(),
                     Digest(3, DigestCadence.Weekly, Item(), Item("Frontend", "Beta AB", "Stark match")),
                 })
        {
            MailSubject.ShouldCarryNoCodeLinkOrToken(EmailTemplates.MatchNotification(BaseUrl, content), AdId.ToString());
        }
    }

    private static MatchNotificationEmail BundledDirect() =>
        Direct(
            Item("Systemutvecklare", "Volvo Cars", "Toppmatch"),
            Item("Backend-utvecklare", "Acme AB", "Toppmatch", Guid.Parse("5c2e8d41-7b3a-4f9e-a1d6-3e8b2c7f4a90")));

    private const string BundledFirst = "Systemutvecklare, Volvo Cars";
    private const string BundledSecond = "Backend-utvecklare, Acme AB";

    [Fact]
    public void MatchNotification_ForABundledDirectMail_UsesThePluralSubject()
    {
        EmailTemplates.MatchNotification(BaseUrl, BundledDirect()).Subject
            .ShouldBe("Nya toppmatchningar på Jobbliggaren");
    }

    [Fact]
    public void MatchNotification_ForABundledDirectMail_OpensWithTheItemsAsBullets_WithNoLeadAndNoGradePerLine()
    {
        var email = EmailTemplates.MatchNotification(BaseUrl, BundledDirect());

        email.PlainTextBody.Replace("\r\n", "\n", StringComparison.Ordinal)
            .ShouldStartWith($"- {BundledFirst}\n- {BundledSecond}\n");
        var list = email.HtmlBody.IndexOf("<ul", StringComparison.Ordinal);
        list.ShouldBeLessThan(email.HtmlBody.IndexOf("<p ", StringComparison.Ordinal));
        email.HtmlBody[list..].ShouldStartWith(EmailHtml.List([BundledFirst, BundledSecond]).ToString());
        email.PlainTextBody.ShouldNotContain("(Toppmatch)");
        email.HtmlBody.ShouldNotContain("(Toppmatch)");
    }

    [Fact]
    public void MatchNotification_ForABundledDirectMail_PreviewsTheItemsSeparatedBySemicolons()
    {
        var email = EmailTemplates.MatchNotification(BaseUrl, BundledDirect());

        email.HtmlBody.ShouldContain($">{BundledFirst}; {BundledSecond}{EmailHtml.PreheaderFiller}</div>");
    }

    [Fact]
    public void MatchNotification_ForABundledDirectMail_LinksTheMatchesListAndNoSingleAd()
    {
        var email = EmailTemplates.MatchNotification(BaseUrl, BundledDirect());

        email.PlainTextBody.Replace("\r\n", "\n", StringComparison.Ordinal)
            .ShouldContain($"Öppna dina matchningar:\n{BaseUrl}/matchningar");
        email.HtmlBody.ShouldContain($"href=\"{BaseUrl}/matchningar\"");
        email.HtmlBody.ShouldContain(">Öppna dina matchningar<");
        email.PlainTextBody.ShouldNotContain($"{BaseUrl}/jobb/");
        email.HtmlBody.ShouldNotContain($"{BaseUrl}/jobb/");
    }

    // --- Subject lines: Direct vs Digest ---

    [Fact]
    public void MatchNotification_ShouldUseDirectSubject_WhenKindIsDirect()
    {
        var email = EmailTemplates.MatchNotification(BaseUrl, Direct(Item()));

        email.Subject.ShouldBe("Ny toppmatchning på Jobbliggaren");
    }

    [Fact]
    public void MatchNotification_ShouldUseDigestSubject_WhenKindIsDigest()
    {
        var content = Digest(2, DigestCadence.Weekly, Item(), Item());

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.Subject.ShouldBe("Din sammanfattning av nya matchningar");
    }

    // --- "och N till." overflow phrase ---

    [Fact]
    public void MatchNotification_ShouldAppendAndMore_WhenTotalCountExceedsItemCount()
    {
        // 5 total in the window, only 2 listed → "och 3 till."
        var content = Digest(
            totalCount: 5,
            DigestCadence.Weekly,
            Item("A", "Co1", "Stark match"),
            Item("B", "Co2", "Stark match"));

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.PlainTextBody.ShouldContain("och 3 till.");
    }

    [Fact]
    public void MatchNotification_ShouldNotAppendAndMore_WhenTotalCountEqualsItemCount()
    {
        var content = Digest(
            totalCount: 2,
            DigestCadence.Weekly,
            Item("A", "Co1", "Stark match"),
            Item("B", "Co2", "Stark match"));

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.PlainTextBody.ShouldNotContain("till.");
    }

    // --- Swedish pluralization (Digest intro) ---

    [Fact]
    public void MatchNotification_ShouldSaySingularPhrase_WhenTotalCountIsOne()
    {
        var content = Digest(totalCount: 1, DigestCadence.Weekly, Item("A", "Co1", "Stark match"));

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.PlainTextBody.ShouldContain("En ny matchning sedan sist:");
        email.PlainTextBody.ShouldNotContain("1 nya matchningar");
    }

    [Fact]
    public void MatchNotification_ShouldUsePluralPhrase_WhenTotalCountIsGreaterThanOne()
    {
        var content = Digest(
            totalCount: 4,
            DigestCadence.Weekly,
            Item("A", "Co1", "Stark match"),
            Item("B", "Co2", "Stark match"));

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.PlainTextBody.ShouldContain("4 nya matchningar");
    }

    // --- PII / civic-tone negative invariants ---

    [Fact]
    public void MatchNotification_ShouldNotContainEmailAddress_WhenRendered()
    {
        // The recipient address is passed separately to the sender and must never reach the body.
        var content = Direct(Item());

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.PlainTextBody.ShouldNotContain("@");
        email.Subject.ShouldNotContain("@");
    }

    [Fact]
    public void MatchNotification_ShouldNotContainExclamationMark_WhenRendered()
    {
        // Civic tone (CLAUDE.md §10) — no exclamation marks anywhere in the copy.
        var content = Digest(3, DigestCadence.Weekly, Item(), Item());

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.Subject.ShouldNotContain("!");
        email.PlainTextBody.ShouldNotContain("!");
    }

    [Fact]
    public void MatchNotification_ShouldNotContainEmDash_WhenRendered()
    {
        // Em-dash (U+2014) är förbjudet i svensk UI-copy (feedback_no_em_dash_in_ui_copy,
        // ESLint-spärrat på FE #138). E-postkroppen ÄR användarvänd copy → item-raden
        // använder komma, aldrig em-dash. Negativ regressionsvakt (code-review 2026-06-24).
        var content = Digest(
            totalCount: 3,
            DigestCadence.Weekly,
            Item("Systemutvecklare", "Volvo Cars", "Toppmatch"),
            Item("Backend-utvecklare", "Acme AB", "Stark match"));

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.Subject.ShouldNotContain("—");
        email.PlainTextBody.ShouldNotContain("—");
    }

    [Fact]
    public void MatchNotification_ShouldNotContainPercentOrScoreDigitsBeyondCounts_WhenRendered()
    {
        // Goodhart (ADR 0071/0080): grade is a named LABEL, never a number/percent. The only
        // digits in the body legitimately come from the honest "och N till" / count phrases —
        // a "%" sign would indicate a leaked score and must never appear.
        var content = Digest(
            totalCount: 9,
            DigestCadence.Weekly,
            Item("A", "Co1", "Stark match"),
            Item("B", "Co2", "Stark match"));

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.PlainTextBody.ShouldNotContain("%");
    }
}
