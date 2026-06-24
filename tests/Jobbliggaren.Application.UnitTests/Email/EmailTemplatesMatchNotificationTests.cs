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
/// (<c>{baseUrl}/installningar</c>, Art. 7(3) — withdrawal must be as easy as giving consent)
/// is ALWAYS present in EVERY rendered body, for both kinds, regardless of item count. The
/// body must never carry PII (a recipient address, a personnummer, a score) — only public
/// ad fields + grade labels.
/// </summary>
public class EmailTemplatesMatchNotificationTests
{
    private const string BaseUrl = "https://jobbliggaren.se";

    private static MatchNotificationItem Item(
        string title = "Backend-utvecklare",
        string company = "Acme AB",
        string grade = "Toppmatch") => new(title, company, grade);

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

        email.PlainTextBody.ShouldContain($"{BaseUrl}/installningar");
    }

    [Fact]
    public void MatchNotification_ShouldAlwaysContainSettingsLink_ForDigestKind()
    {
        var content = Digest(3, DigestCadence.Weekly, Item(), Item("Frontend", "Beta AB", "Stark match"));

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.PlainTextBody.ShouldContain($"{BaseUrl}/installningar");
    }

    [Fact]
    public void MatchNotification_ShouldContainSettingsLink_EvenWhenSingleItem()
    {
        // Edge: a one-item Direct notice must still carry the withdrawal link.
        var content = Direct(Item());

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.PlainTextBody.ShouldContain($"{BaseUrl}/installningar");
    }

    [Theory]
    [InlineData("https://jobbliggaren.se/")]
    [InlineData("https://jobbliggaren.se")]
    public void MatchNotification_ShouldNotDoubleSlashSettingsLink_WhenBaseUrlHasTrailingSlash(string baseUrl)
    {
        var email = EmailTemplates.MatchNotification(baseUrl, Direct(Item()));

        email.PlainTextBody.ShouldContain("https://jobbliggaren.se/installningar");
        email.PlainTextBody.ShouldNotContain("se//installningar");
    }

    // --- The /matchningar deep link ---

    [Fact]
    public void MatchNotification_ShouldContainMatchesLink_WhenRendered()
    {
        var email = EmailTemplates.MatchNotification(BaseUrl, Direct(Item()));

        email.PlainTextBody.ShouldContain($"{BaseUrl}/matchningar");
    }

    // --- Item rendering: "- {JobTitle}, {CompanyName} ({GradeLabel})" (komma, EJ em-dash) ---

    [Fact]
    public void MatchNotification_ShouldRenderItemWithTitleCompanyAndGradeLabel_WhenRendered()
    {
        var content = Direct(Item("Systemutvecklare", "Volvo Cars", "Toppmatch"));

        var email = EmailTemplates.MatchNotification(BaseUrl, content);

        email.PlainTextBody.ShouldContain("- Systemutvecklare, Volvo Cars (Toppmatch)");
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

        email.PlainTextBody.ShouldContain("en ny matchning");
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
