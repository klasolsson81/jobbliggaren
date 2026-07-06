using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #679 — locks the invariants of <see cref="EmailTemplates.EmailChangeConfirmation"/>
/// (Infrastructure-internal, reachable via InternalsVisibleTo). Load-bearing invariants: the confirm
/// link is built as <c>{baseUrl}/bekrafta-epost?uid={uid:N}&amp;email={percent-encoded}&amp;token={raw}</c>;
/// the new address is percent-encoded (so plus-addressing survives the query round-trip) while the
/// Base64Url token passes through UNescaped (escaping <c>-</c>/<c>_</c> would corrupt the single-use
/// token); the base URL is not double-slashed; civic tone (no exclamation marks, no em-dash); and the
/// 24-hour validity is stated.
/// </summary>
public class EmailTemplatesEmailChangeConfirmationTests
{
    private const string BaseUrl = "https://jobbliggaren.se";

    // Base64Url alphabet only ([A-Za-z0-9_-]); the '-' and '_' must survive the link unescaped.
    private const string Base64UrlToken = "Q2ZESjhL-nP_ab12CD"; // gitleaks:allow

    private static EmailChangeConfirmationEmail Content(
        Guid? userId = null, string newEmail = "ny.adress@example.se", string token = Base64UrlToken)
        => new(userId ?? Guid.NewGuid(), newEmail, token);

    [Fact]
    public void EmailChangeConfirmation_ShouldBuildConfirmLink_WithCompactUidEncodedEmailAndRawToken()
    {
        var userId = Guid.NewGuid();
        const string email = "ny.adress@example.se";

        var rendered = EmailTemplates.EmailChangeConfirmation(
            BaseUrl, new EmailChangeConfirmationEmail(userId, email, Base64UrlToken));

        // The whole link in one assertion pins uid:N, the percent-encoded email, and the raw token.
        rendered.PlainTextBody.ShouldContain(
            $"{BaseUrl}/bekrafta-epost?uid={userId:N}&email={Uri.EscapeDataString(email)}&token={Base64UrlToken}");
    }

    [Fact]
    public void EmailChangeConfirmation_ShouldPercentEncodePlusAddressedEmail()
    {
        // '+' (plus-addressing) and '@' must be percent-encoded, or the receiving page would decode
        // '+' to a space and break the email query param.
        var rendered = EmailTemplates.EmailChangeConfirmation(
            BaseUrl, Content(newEmail: "kalle+jobb@example.se"));

        rendered.PlainTextBody.ShouldContain("email=kalle%2Bjobb%40example.se");
        rendered.PlainTextBody.ShouldNotContain("email=kalle+jobb@example.se");
    }

    [Fact]
    public void EmailChangeConfirmation_ShouldPassBase64UrlTokenThroughUnescaped()
    {
        // A Base64Url token uses only [A-Za-z0-9_-]; none of those need escaping, so it appears
        // verbatim. Escaping '-'/'_' would corrupt the single-use token → a valid link would 400.
        const string token = "abc-DEF_123-xyz_789"; // gitleaks:allow

        var rendered = EmailTemplates.EmailChangeConfirmation(
            BaseUrl, Content(newEmail: "ny@example.se", token: token));

        rendered.PlainTextBody.ShouldContain($"token={token}");
        rendered.PlainTextBody.ShouldNotContain("%2D"); // '-' escaped
        rendered.PlainTextBody.ShouldNotContain("%5F"); // '_' escaped
    }

    [Fact]
    public void EmailChangeConfirmation_ShouldUseConfirmationSubject()
        => EmailTemplates.EmailChangeConfirmation(BaseUrl, Content())
            .Subject.ShouldBe("Bekräfta din nya e-postadress");

    [Fact]
    public void EmailChangeConfirmation_ShouldStateTheLinkExpiresIn24Hours()
        => EmailTemplates.EmailChangeConfirmation(BaseUrl, Content())
            .PlainTextBody.ShouldContain("24 timmar");

    [Fact]
    public void EmailChangeConfirmation_ShouldNotContainExclamationOrEmDash()
    {
        // Civic tone (CLAUDE.md §10 + feedback_no_em_dash_in_ui_copy). The email body IS user-facing copy.
        var rendered = EmailTemplates.EmailChangeConfirmation(BaseUrl, Content());

        rendered.Subject.ShouldNotContain("!");
        rendered.PlainTextBody.ShouldNotContain("!");
        rendered.PlainTextBody.ShouldNotContain("—"); // em-dash
    }

    [Theory]
    [InlineData("https://jobbliggaren.se/")]
    [InlineData("https://jobbliggaren.se")]
    public void EmailChangeConfirmation_ShouldNotDoubleSlashLink_WhenBaseUrlHasTrailingSlash(string baseUrl)
    {
        var rendered = EmailTemplates.EmailChangeConfirmation(baseUrl, Content(newEmail: "ny@example.se"));

        rendered.PlainTextBody.ShouldContain("https://jobbliggaren.se/bekrafta-epost");
        rendered.PlainTextBody.ShouldNotContain("se//bekrafta-epost");
    }
}
