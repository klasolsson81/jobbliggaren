using System.Text.RegularExpressions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #679 — locks the invariants of <see cref="EmailTemplates.EmailChangeConfirmation"/>
/// (Infrastructure-internal, reachable via InternalsVisibleTo). Load-bearing invariants: the confirm
/// link is built as <c>{baseUrl}/bekrafta-epost?uid={uid:D}&amp;email={percent-encoded}&amp;token={raw}</c>;
/// the new address is percent-encoded (so plus-addressing survives the query round-trip) while the
/// Base64Url token passes through UNescaped (escaping <c>-</c>/<c>_</c> would corrupt the single-use
/// token); the base URL is not double-slashed; civic tone (no exclamation marks, no em-dash); and the
/// 24-hour validity is stated.
///
/// <para>
/// #183 — the template also carries an Art. 14 notice, because it reaches recipient class (3): an
/// address that by construction sits on no account. The properties pinned below are that the notice
/// reaches BOTH parts of the <c>multipart/alternative</c> message, that no input suppresses it, that
/// Art. 14(2)(f) is answered with a CATEGORY and never with the account holder's identity, and that
/// the contact route, the legal ground and the processor are named. The template's own doc owns the
/// reasoning; these are the facts that hold it.
/// </para>
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
    public void EmailChangeConfirmation_ShouldBuildConfirmLink_WithDashedUidEncodedEmailAndRawToken()
    {
        var userId = Guid.NewGuid();
        const string email = "ny.adress@example.se";

        var rendered = EmailTemplates.EmailChangeConfirmation(
            BaseUrl, new EmailChangeConfirmationEmail(userId, email, Base64UrlToken));

        // The whole link in one assertion pins uid:D (the confirm endpoint's STJ Guid binder accepts only
        // the dashed 'D' form; a compact 'N' uid 400s, #981), the percent-encoded email, and the raw token.
        rendered.PlainTextBody.ShouldContain(
            $"{BaseUrl}/bekrafta-epost?uid={userId:D}&email={Uri.EscapeDataString(email)}&token={Base64UrlToken}");
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

    // ---------- the Art. 14 notice ----------

    [Theory]
    [InlineData("ny.adress@example.se")]
    [InlineData("kalle+jobb@example.se")]
    public void EmailChangeConfirmation_ShouldNameTheSourceAsACategory_InBothParts(string newEmail)
    {
        // Art. 14(2)(f) over recipient class (3). Naming the account holder would be a disclosure in
        // the other direction, so the source is a category. Pinned per input because the notice cannot
        // be conditioned on anything: at send time nobody knows whether the recipient is the holder or
        // a stranger, so a future branch here is a defect and must go red.
        var rendered = EmailTemplates.EmailChangeConfirmation(BaseUrl, Content(newEmail: newEmail));

        foreach (var part in BothPartsFlattened(rendered))
        {
            part.ShouldContain("från en användare som angav den");
            part.ShouldContain("Vi berättar inte vem det är");
        }
    }

    [Theory]
    [InlineData("ny.adress@example.se")]
    [InlineData("kalle+jobb@example.se")]
    public void EmailChangeConfirmation_ShouldNameTheLegalGroundAndTheProcessor_InBothParts(
        string newEmail)
    {
        // Art. 14(1)(c) and (1)(e), bound as the two short fragments that carry them. A longer quote
        // would break on an editorial pass while pinning nothing these two do not.
        var rendered = EmailTemplates.EmailChangeConfirmation(BaseUrl, Content(newEmail: newEmail));

        foreach (var part in BothPartsFlattened(rendered))
        {
            part.ShouldContain("berättigat intresse");
            part.ShouldContain("Scaleway");
        }
    }

    [Theory]
    [InlineData("ny.adress@example.se")]
    [InlineData("kalle+jobb@example.se")]
    public void EmailChangeConfirmation_ShouldOfferTheContactAddress_InBothParts(string newEmail)
    {
        // The Art. 14(2)(c) route out. The recipient has no account, so this address is the only way
        // they can object, ask or have the address erased.
        var rendered = EmailTemplates.EmailChangeConfirmation(BaseUrl, Content(newEmail: newEmail));

        rendered.PlainTextBody.ShouldContain(EmailTemplates.ContactAddress);
        rendered.HtmlBody.ShouldContain($"mailto:{EmailTemplates.ContactAddress}");
    }

    [Fact]
    public void EmailChangeConfirmation_ShouldNotCarryTheUserId_OutsideTheConfirmLinkUid()
    {
        // The category answer above is only worth as much as the absence of an identity beside it.
        // UserId is the sole account-holder identity this template is handed, and the confirm link
        // needs it; anywhere else in the body it identifies the account to a recipient who holds none.
        var userId = Guid.NewGuid();
        var rendered = EmailTemplates.EmailChangeConfirmation(BaseUrl, Content(userId));

        foreach (var part in new[] { rendered.PlainTextBody, rendered.HtmlBody })
        {
            // Presence first: without it the count below would be satisfied by a body that lost the
            // link entirely, which is the direction an absence assertion gets greener in.
            Occurrences(part, $"uid={userId:D}").ShouldBe(1);
            Occurrences(part, userId.ToString("D")).ShouldBe(1);
            part.ShouldNotContain(userId.ToString("N"));
        }
    }

    [Fact]
    public void EmailChangeConfirmation_ShouldNotContainExclamationOrEmDash_InTheHtmlPart()
    {
        // The sibling fact above covers the text part only, and the Art. 14 paragraph is copy in both.
        // Asserted on the TEXT of the HTML part: the document declaration is markup rather than copy
        // and carries the only '!' any rendered body has.
        var html = EmailTemplates.EmailChangeConfirmation(BaseUrl, Content()).HtmlBody;
        var copy = Flatten(Tag.Replace(html, " "));

        copy.ShouldContain("Vi berättar inte vem det är"); // else a stripper that ate the copy passes
        copy.ShouldNotContain("!");
        copy.ShouldNotContain("—"); // em-dash
    }

    // ---------- oracles ----------

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

    private static readonly Regex Tag = new("<[^>]*>", RegexOptions.CultureInvariant);

    /// <summary>
    /// Both parts of the <c>multipart/alternative</c> message, whitespace-flattened. The text part
    /// hard-wraps its lines and the HTML part folds the same sentences across string concatenations,
    /// so a fragment is pinned against the sentence and not against where either part breaks it.
    /// </summary>
    private static string[] BothPartsFlattened(EmailTemplates.EmailContent rendered) =>
        [Flatten(rendered.PlainTextBody), Flatten(rendered.HtmlBody)];

    private static string Flatten(string body) => Whitespace.Replace(body, " ");

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}
