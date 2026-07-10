using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #714 — locks the invariants of <see cref="EmailTemplates.AccountExistsNotice"/> (Infrastructure-
/// internal, reachable via InternalsVisibleTo). This is the out-of-band mail sent to a TAKEN address
/// when someone attempts to register it; because the HTTP response is an identical 202 for a taken or a
/// fresh address, this mail is the ONLY differentiator and it reaches only the real owner's inbox.
/// Load-bearing invariants: it carries a login link and a help link built from the base URL, but NO
/// token and NO access-granting activation link (it must never let a non-owner in); the base URL is not
/// double-slashed; civic tone (no exclamation marks, no em-dash).
/// </summary>
public class EmailTemplatesAccountExistsNoticeTests
{
    private const string BaseUrl = "https://jobbliggaren.se";

    [Fact]
    public void AccountExistsNotice_ShouldLinkToLoginAndHelp()
    {
        var rendered = EmailTemplates.AccountExistsNotice(BaseUrl);

        rendered.PlainTextBody.ShouldContain($"{BaseUrl}/logga-in");
        rendered.PlainTextBody.ShouldContain($"{BaseUrl}/hjalpcenter");
    }

    [Fact]
    public void AccountExistsNotice_ShouldNotCarryAnyTokenOrActivationLink()
    {
        // The notice grants NO access: no token, and no /bekrafta-konto activation link. Its only job is
        // a login-nudge to the real owner (Klas decision) while leaking no account existence to a
        // non-owner (the HTTP response stays an identical 202).
        var rendered = EmailTemplates.AccountExistsNotice(BaseUrl);

        rendered.PlainTextBody.ShouldNotContain("token=");
        rendered.PlainTextBody.ShouldNotContain("bekrafta-konto");
    }

    [Fact]
    public void AccountExistsNotice_ShouldUseAccountExistsSubject()
        => EmailTemplates.AccountExistsNotice(BaseUrl)
            .Subject.ShouldBe("Du har redan ett konto hos Jobbliggaren");

    [Fact]
    public void AccountExistsNotice_ShouldNotContainExclamationOrEmDash()
    {
        // Civic tone (CLAUDE.md §10 + feedback_no_em_dash_in_ui_copy).
        var rendered = EmailTemplates.AccountExistsNotice(BaseUrl);

        rendered.Subject.ShouldNotContain("!");
        rendered.PlainTextBody.ShouldNotContain("!");
        rendered.PlainTextBody.ShouldNotContain("—"); // em-dash
    }

    [Theory]
    [InlineData("https://jobbliggaren.se/")]
    [InlineData("https://jobbliggaren.se")]
    public void AccountExistsNotice_ShouldNotDoubleSlashLinks_WhenBaseUrlHasTrailingSlash(string baseUrl)
    {
        var rendered = EmailTemplates.AccountExistsNotice(baseUrl);

        rendered.PlainTextBody.ShouldContain("https://jobbliggaren.se/logga-in");
        rendered.PlainTextBody.ShouldNotContain("se//logga-in");
        rendered.PlainTextBody.ShouldNotContain("se//hjalpcenter");
    }
}
