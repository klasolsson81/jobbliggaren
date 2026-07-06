using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #679 (CTO-bind #4) — locks the invariants of <see cref="EmailTemplates.EmailChangedNotification"/>
/// (Infrastructure-internal, reachable via InternalsVisibleTo), the old-address "your email was
/// changed" security notice. Load-bearing invariants: it carries the help-centre link
/// (<c>{baseUrl}/hjalpcenter</c>) so the previous owner can react; it reveals NEITHER the new address
/// NOR a token/confirmation link (it must not become a second attack surface); civic tone (no
/// exclamation marks, no em-dash); the base URL is not double-slashed.
/// </summary>
public class EmailTemplatesEmailChangedNotificationTests
{
    private const string BaseUrl = "https://jobbliggaren.se";

    [Fact]
    public void EmailChangedNotification_ShouldContainHelpCentreLink()
        => EmailTemplates.EmailChangedNotification(BaseUrl)
            .PlainTextBody.ShouldContain($"{BaseUrl}/hjalpcenter");

    [Fact]
    public void EmailChangedNotification_ShouldNotRevealNewAddressOrToken()
    {
        // The notice tells the previous owner the address changed — it must not leak the new address
        // (no '@' / recipient PII) and must carry no confirmation token or link.
        var rendered = EmailTemplates.EmailChangedNotification(BaseUrl);

        rendered.PlainTextBody.ShouldNotContain("@");
        rendered.PlainTextBody.ShouldNotContain("token");
        rendered.PlainTextBody.ShouldNotContain("bekrafta-epost");
    }

    [Fact]
    public void EmailChangedNotification_ShouldUseChangedSubject()
        => EmailTemplates.EmailChangedNotification(BaseUrl)
            .Subject.ShouldBe("Din e-postadress har ändrats");

    [Fact]
    public void EmailChangedNotification_ShouldNotContainExclamationOrEmDash()
    {
        var rendered = EmailTemplates.EmailChangedNotification(BaseUrl);

        rendered.Subject.ShouldNotContain("!");
        rendered.PlainTextBody.ShouldNotContain("!");
        rendered.PlainTextBody.ShouldNotContain("—"); // em-dash
    }

    [Theory]
    [InlineData("https://jobbliggaren.se/")]
    [InlineData("https://jobbliggaren.se")]
    public void EmailChangedNotification_ShouldNotDoubleSlashHelpLink_WhenBaseUrlHasTrailingSlash(string baseUrl)
    {
        var rendered = EmailTemplates.EmailChangedNotification(baseUrl);

        rendered.PlainTextBody.ShouldContain("https://jobbliggaren.se/hjalpcenter");
        rendered.PlainTextBody.ShouldNotContain("se//hjalpcenter");
    }
}
