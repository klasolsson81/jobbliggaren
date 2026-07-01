using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// ADR 0087 D5 (#311 PR-4) — locks the invariants of
/// <see cref="EmailTemplates.FollowedCompanyNotification"/> (Infrastructure-internal, reachable via
/// InternalsVisibleTo). The load-bearing invariants: the settings/unsubscribe link
/// (<c>{baseUrl}/installningar</c>, GDPR Art. 7(3)) is ALWAYS present; the body carries ONLY public
/// ad fields (title + company) and NEVER an org.nr, a grade label, a score, or a recipient address
/// (ADR 0087 D8 / CLAUDE.md §5); civic tone (no exclamation marks, no em-dash).
/// </summary>
public class EmailTemplatesFollowedCompanyNotificationTests
{
    private const string BaseUrl = "https://jobbliggaren.se";

    private static FollowedCompanyAdItem Item(
        string title = "Backend-utvecklare", string company = "Acme AB") => new(title, company);

    private static FollowedCompanyNotificationEmail Content(
        int totalCount, DigestCadence cadence, params FollowedCompanyAdItem[] items) =>
        new(cadence, Items: items, TotalCount: totalCount);

    [Fact]
    public void FollowedCompanyNotification_ShouldAlwaysContainSettingsLink()
    {
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl, Content(1, DigestCadence.Weekly, Item()));

        email.PlainTextBody.ShouldContain($"{BaseUrl}/installningar");
    }

    [Fact]
    public void FollowedCompanyNotification_ShouldListEachItemsTitleAndCompany()
    {
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl,
            Content(2, DigestCadence.Daily, Item("Frontend", "Beta AB"), Item("DevOps", "Gamma AB")));

        email.PlainTextBody.ShouldContain("Frontend, Beta AB");
        email.PlainTextBody.ShouldContain("DevOps, Gamma AB");
    }

    [Fact]
    public void FollowedCompanyNotification_ShouldRenderAndMore_WhenCapped()
    {
        // TotalCount (5) exceeds the displayed items (2) → "och 3 till."
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl, Content(5, DigestCadence.Weekly, Item(), Item("Frontend", "Beta AB")));

        email.PlainTextBody.ShouldContain("och 3 till");
    }

    [Fact]
    public void FollowedCompanyNotification_ShouldNotRenderAndMore_WhenAllShown()
    {
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl, Content(1, DigestCadence.Weekly, Item()));

        email.PlainTextBody.ShouldNotContain("till.");
    }

    [Fact]
    public void FollowedCompanyNotification_ShouldUseSingularCountPhrase_ForOneAd()
    {
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl, Content(1, DigestCadence.Weekly, Item()));

        email.PlainTextBody.ShouldContain("en ny annons");
    }

    [Fact]
    public void FollowedCompanyNotification_ShouldNotContainExclamationOrEmDash()
    {
        // Civic tone (CLAUDE.md §10 + feedback_no_em_dash_in_ui_copy).
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl, Content(2, DigestCadence.Weekly, Item(), Item("Frontend", "Beta AB")));

        email.PlainTextBody.ShouldNotContain("!");
        email.PlainTextBody.ShouldNotContain("—"); // em-dash
        email.Subject.ShouldNotContain("!");
    }

    [Fact]
    public void FollowedCompanyNotification_ShouldNotContainRecipientOrOrgNumber()
    {
        // The content contract carries no recipient and no org.nr; the body must never surface either
        // (ADR 0087 D8 — the personnummer-shaped-org.nr guard; the follow email shows company NAME).
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl, Content(1, DigestCadence.Weekly, Item("Snickare", "Firma Karlsson")));

        email.PlainTextBody.ShouldNotContain("5592804784");
        email.PlainTextBody.ShouldNotContain("@");
    }
}
