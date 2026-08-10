using System.Globalization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.Infrastructure.Identity;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #1171 — locks the invariants of <see cref="EmailTemplates.PasswordReset"/> and
/// <see cref="EmailTemplates.PasswordChangedNotice"/> (parity with
/// <c>EmailTemplatesEmailConfirmationTests</c>).
/// <para>
/// The one that is not a copy of its siblings is
/// <see cref="PasswordReset_ShouldStateTheLifespan_ReadFromTheProviderConstant"/>: the body's promise
/// and the provider that enforces it must not be able to drift apart, so the test reads the same
/// constant the template does rather than spelling a number. A test asserting "60 minuter" literally
/// would pass while someone changed the provider's lifespan and left the mail lying.
/// </para>
/// </summary>
public class EmailTemplatesPasswordResetTests
{
    private const string BaseUrl = "https://jobbliggaren.se";

    // Base64Url alphabet only ([A-Za-z0-9_-]); the '-' and '_' must survive the link unescaped.
    private const string Base64UrlToken = "Q2ZESjhL-nP_ab12CD"; // gitleaks:allow

    [Fact]
    public void PasswordReset_ShouldBuildResetLink_WithDashedUidAndRawToken()
    {
        var userId = Guid.NewGuid();

        var rendered = EmailTemplates.PasswordReset(
            BaseUrl, new PasswordResetEmail(userId, Base64UrlToken));

        // uid:D is load-bearing — /reset-password binds Uid through STJ's Guid converter, which accepts
        // only the dashed form; a compact 'N' uid 400s on every click (#981).
        rendered.PlainTextBody.ShouldContain(
            $"{BaseUrl}/aterstall-losenord?uid={userId:D}&token={Base64UrlToken}");
    }

    [Fact]
    public void PasswordReset_ShouldPassBase64UrlTokenThroughUnescaped()
    {
        const string token = "abc-DEF_123-xyz_789"; // gitleaks:allow

        var rendered = EmailTemplates.PasswordReset(
            BaseUrl, new PasswordResetEmail(Guid.NewGuid(), token));

        rendered.PlainTextBody.ShouldContain($"token={token}");
    }

    [Fact]
    public void PasswordReset_ShouldNotDoubleSlash_WhenBaseUrlHasTrailingSlash()
    {
        var rendered = EmailTemplates.PasswordReset(
            $"{BaseUrl}/", new PasswordResetEmail(Guid.NewGuid(), Base64UrlToken));

        rendered.PlainTextBody.ShouldNotContain("//aterstall-losenord");
        rendered.PlainTextBody.ShouldContain($"{BaseUrl}/aterstall-losenord");
    }

    [Fact]
    public void PasswordReset_ShouldStateTheLifespan_ReadFromTheProviderConstant()
    {
        // Read the constant, never a literal: the template interpolates the same value the token
        // provider enforces, so promise and enforcement cannot drift. Asserting "60" here would let a
        // lifespan change pass while the mail kept claiming the old one.
        var rendered = EmailTemplates.PasswordReset(
            BaseUrl, new PasswordResetEmail(Guid.NewGuid(), Base64UrlToken));

        rendered.PlainTextBody.ShouldContain(
            PasswordResetTokenProviderOptions.LifespanMinutes.ToString(CultureInfo.InvariantCulture));
        rendered.PlainTextBody.ShouldContain("minuter");
    }

    [Fact]
    public void PasswordReset_ShouldTellAnUninvolvedRecipient_ThatNothingChangedAndIgnoringIsSafe()
    {
        // Not decoration. The request endpoint answers a uniform 202 for every well-formed address, so
        // anyone can cause this mail to reach an address they do not own. It must say plainly that the
        // password is unchanged until the link is opened.
        var rendered = EmailTemplates.PasswordReset(
            BaseUrl, new PasswordResetEmail(Guid.NewGuid(), Base64UrlToken));

        rendered.PlainTextBody.ShouldContain("Om det inte var du");
        rendered.PlainTextBody.ShouldContain("oförändrat");
    }

    [Fact]
    public void PasswordChangedNotice_ShouldCarryNoTokenAndPointAtRecovery()
    {
        // The breach-detection control: it must give a recipient who did NOT do this something to do,
        // and it must not itself be a credential.
        var rendered = EmailTemplates.PasswordChangedNotice(BaseUrl);

        rendered.PlainTextBody.ShouldContain($"{BaseUrl}/glomt-losenord");
        rendered.PlainTextBody.ShouldContain($"{BaseUrl}/hjalpcenter");
        rendered.PlainTextBody.ShouldNotContain("token=");
        rendered.PlainTextBody.ShouldNotContain("uid=");
    }

    [Fact]
    public void AccountExistsNotice_ShouldOfferTheResetPath()
    {
        // #1171 — the notice's own doc used to say a reset link could not be offered because the flow
        // did not exist. Someone registering an address they already own has most often forgotten the
        // password, and the link carries no token, so it grants nothing the login link does not.
        var rendered = EmailTemplates.AccountExistsNotice(BaseUrl);

        rendered.PlainTextBody.ShouldContain($"{BaseUrl}/glomt-losenord");
    }

    [Theory]
    [InlineData("!")]
    [InlineData("—")]
    public void PasswordResetTemplates_ShouldKeepCivicTone(string forbidden)
    {
        var reset = EmailTemplates.PasswordReset(
            BaseUrl, new PasswordResetEmail(Guid.NewGuid(), Base64UrlToken));
        var notice = EmailTemplates.PasswordChangedNotice(BaseUrl);

        reset.Subject.ShouldNotContain(forbidden);
        reset.PlainTextBody.ShouldNotContain(forbidden);
        notice.Subject.ShouldNotContain(forbidden);
        notice.PlainTextBody.ShouldNotContain(forbidden);
    }
}
