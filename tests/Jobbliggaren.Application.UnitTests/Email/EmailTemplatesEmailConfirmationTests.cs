using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #714 — locks the invariants of <see cref="EmailTemplates.EmailConfirmation"/> (Infrastructure-
/// internal, reachable via InternalsVisibleTo; parity with <c>EmailTemplatesEmailChangeConfirmationTests</c>).
/// Load-bearing invariants: the activation link is built as
/// <c>{baseUrl}/bekrafta-konto?uid={uid:D}&amp;token={raw}</c>; the Base64Url token passes through
/// UNescaped (escaping <c>-</c>/<c>_</c> would corrupt the token so a valid link would 400); there is
/// NO email query param (unlike the change-email confirm, the address is unchanged and never in the
/// link); the base URL is not double-slashed; the 24-hour validity is stated; and the body keeps civic
/// tone (no exclamation marks, no em-dash).
/// </summary>
public class EmailTemplatesEmailConfirmationTests
{
    private const string BaseUrl = "https://jobbliggaren.se";

    // Base64Url alphabet only ([A-Za-z0-9_-]); the '-' and '_' must survive the link unescaped.
    private const string Base64UrlToken = "Q2ZESjhL-nP_ab12CD"; // gitleaks:allow

    private static EmailConfirmationEmail Content(Guid? userId = null, string token = Base64UrlToken)
        => new(userId ?? Guid.NewGuid(), token);

    [Fact]
    public void EmailConfirmation_ShouldBuildActivationLink_WithDashedUidAndRawToken()
    {
        var userId = Guid.NewGuid();

        var rendered = EmailTemplates.EmailConfirmation(
            BaseUrl, new EmailConfirmationEmail(userId, Base64UrlToken));

        // The whole link in one assertion pins uid:D (the confirm endpoint's STJ Guid binder accepts only
        // the dashed 'D' form; a compact 'N' uid 400s, #981) and the raw (unescaped) token.
        rendered.PlainTextBody.ShouldContain($"{BaseUrl}/bekrafta-konto?uid={userId:D}&token={Base64UrlToken}");
    }

    [Fact]
    public void EmailConfirmation_ShouldPassBase64UrlTokenThroughUnescaped()
    {
        // A Base64Url token uses only [A-Za-z0-9_-]; none of those need escaping, so it appears
        // verbatim. Escaping '-'/'_' would corrupt the token → a valid link would 400.
        const string token = "abc-DEF_123-xyz_789"; // gitleaks:allow

        var rendered = EmailTemplates.EmailConfirmation(BaseUrl, Content(token: token));

        rendered.PlainTextBody.ShouldContain($"token={token}");
        rendered.PlainTextBody.ShouldNotContain("%2D"); // '-' escaped
        rendered.PlainTextBody.ShouldNotContain("%5F"); // '_' escaped
    }

    [Fact]
    public void EmailConfirmation_ShouldNotCarryAnEmailQueryParam()
        // Unlike the change-email confirm there is NO pending new address and the account's own address
        // is never part of the link or body (it is delivered TO that same inbox).
        => EmailTemplates.EmailConfirmation(BaseUrl, Content())
            .PlainTextBody.ShouldNotContain("email=");

    [Fact]
    public void EmailConfirmation_ShouldUseConfirmationSubject()
        => EmailTemplates.EmailConfirmation(BaseUrl, Content())
            .Subject.ShouldBe("Bekräfta din e-postadress");

    [Fact]
    public void EmailConfirmation_ShouldStateTheLinkExpiresIn24Hours()
        => EmailTemplates.EmailConfirmation(BaseUrl, Content())
            .PlainTextBody.ShouldContain("24 timmar");

    [Fact]
    public void EmailConfirmation_ShouldNotContainExclamationOrEmDash()
    {
        // Civic tone (CLAUDE.md §10 + feedback_no_em_dash_in_ui_copy). The email body IS user-facing copy.
        var rendered = EmailTemplates.EmailConfirmation(BaseUrl, Content());

        rendered.Subject.ShouldNotContain("!");
        rendered.PlainTextBody.ShouldNotContain("!");
        rendered.PlainTextBody.ShouldNotContain("—"); // em-dash
    }

    [Fact]
    public void EmailConfirmation_ShouldNotCarryTheStruckAccountOrLoginClaims_InBothRenderings()
    {
        // #1349 — the mail may assert neither that an account exists nor that confirming the address is
        // enough to log in. Both are false for a profile-less Identity row, and the resend path delivers
        // this same mail to one: /resend-confirmation is deliberately existence-independent, so an orphan
        // holder receives it. "Du kan logga in när adressen är bekräftad" said confirmation was
        // SUFFICIENT; LoginCommandHandler refuses such a row (OrphanedIdentityActivationTests).
        //
        // BOTH renderings, because they are held word-for-word alike BY HAND and every other assertion in
        // this class reads PlainTextBody only — so a half-strike would pass the suite unseen.
        var rendered = EmailTemplates.EmailConfirmation(BaseUrl, Content());

        foreach (var body in new[] { rendered.PlainTextBody, rendered.HtmlBody })
        {
            // COUNTERFACTUAL. The negatives below also pass against an empty body or the wrong
            // template, so pin first that this IS the confirmation mail.
            body.ShouldContain("Tack för att du har registrerat dig på Jobbliggaren.");
            body.ShouldContain("Bekräfta att adressen är din genom att öppna länken nedan.");

            // Literal substrings, not a form-based guard: a REWORDED promise would pass. Two
            // literals rather than one widens the net (the second also catches "du har ett konto
            // på Jobbliggaren"); neither collides with the Klas-required closing line, which says
            // "skapat NÅGOT konto" and names no service.
            body.ShouldNotContain("skapat ett konto");
            body.ShouldNotContain("konto på Jobbliggaren");
            body.ShouldNotContain("logga in när adressen är bekräftad");
        }
    }

    [Theory]
    [InlineData("https://jobbliggaren.se/")]
    [InlineData("https://jobbliggaren.se")]
    public void EmailConfirmation_ShouldNotDoubleSlashLink_WhenBaseUrlHasTrailingSlash(string baseUrl)
    {
        var rendered = EmailTemplates.EmailConfirmation(baseUrl, Content());

        rendered.PlainTextBody.ShouldContain("https://jobbliggaren.se/bekrafta-konto");
        rendered.PlainTextBody.ShouldNotContain("se//bekrafta-konto");
    }
}
