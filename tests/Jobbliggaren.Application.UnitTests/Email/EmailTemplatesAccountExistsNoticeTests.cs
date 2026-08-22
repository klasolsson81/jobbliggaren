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
    public void AccountExistsNotice_ShouldHaveNothingToBranchOn_AndTheUnconfirmedDetailShouldStayAConstant()
    {
        // #1349 — the EXECUTABLE form of "this surface must never vary with account state". The copy
        // in both places now says only what its own trigger establishes, but copy can be rewritten;
        // what actually holds the property is that neither place has anything to branch ON.
        // AccountExistsNotice is handed a base URL and nothing else — not even a userId — and the 403
        // detail is a compile-time constant. Growing either is the change worth catching, and it is
        // catchable, where "do not be state-dependent" is not (senior-cto-advisor 2026-08-22).
        //
        // Branching there is not a style question: the duplicate-registration branch is reachable by
        // anyone who submits an address, so state-dependent copy on it is an account-existence oracle.
        var parameters = typeof(EmailTemplates)
            .GetMethod(nameof(EmailTemplates.AccountExistsNotice))!
            .GetParameters();

        parameters.Length.ShouldBe(1, "a second parameter is how account state would get in");
        parameters[0].ParameterType.ShouldBe(typeof(string));
        parameters[0].Name.ShouldBe("baseUrl");

        var detail = typeof(Jobbliggaren.Application.Auth.AuthErrorCodes)
            .GetField(nameof(Jobbliggaren.Application.Auth.AuthErrorCodes.EmailNotConfirmedMessage));

        detail.ShouldNotBeNull();
        detail.IsLiteral.ShouldBeTrue("a const cannot be computed from account state");
        detail.IsInitOnly.ShouldBeFalse("a static readonly could be assigned a computed value");
    }

    [Fact]
    public void AccountExistsNotice_ShouldLinkToLoginAndNameTheContactAddress()
    {
        // The help-centre route became the contact address on 2026-08-12 (Klas-beslut): the help
        // centre is a hub that links onward to /kontakt, and someone who cannot get into their
        // account should not have to navigate one.
        var rendered = EmailTemplates.AccountExistsNotice(BaseUrl);

        rendered.PlainTextBody.ShouldContain($"{BaseUrl}/logga-in");
        rendered.PlainTextBody.ShouldContain(EmailTemplates.ContactAddress);
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
            .Subject.ShouldBe("Adressen är redan registrerad hos Jobbliggaren");

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
        rendered.PlainTextBody.ShouldNotContain("se//glomt-losenord");
    }
}
