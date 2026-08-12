using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #679 (CTO-bind #4) — locks the invariants of <see cref="EmailTemplates.EmailChangedNotification"/>
/// (Infrastructure-internal, reachable via InternalsVisibleTo), the old-address "your email was
/// changed" security notice. Load-bearing invariants: it gives the previous owner a way to reach us so
/// they can react; it reveals NEITHER the new address NOR a token/confirmation link (it must not become
/// a second attack surface); civic tone (no exclamation marks, no em-dash).
///
/// <para>
/// <b>Rewritten 2026-08-12 (Klas-beslut).</b> The route was <c>{baseUrl}/hjalpcenter</c> and is now
/// the contact address. The help centre still exists, but it is a hub that links onward to
/// <c>/kontakt</c>, and this mail reaches someone who may have just lost the account — a hub is one
/// step too many, and a <c>mailto:</c> works from any client. The template consequently takes no base
/// URL at all, which is why the double-slash theory that used to live here is gone rather than
/// weakened: it guarded a URL this template no longer builds.
/// </para>
/// </summary>
public class EmailTemplatesEmailChangedNotificationTests
{
    [Fact]
    public void EmailChangedNotification_ShouldContainTheContactAddress()
        => EmailTemplates.EmailChangedNotification()
            .PlainTextBody.ShouldContain(EmailTemplates.ContactAddress);

    [Fact]
    public void EmailChangedNotification_ShouldNotRevealNewAddressOrToken()
    {
        // The notice tells the previous owner the address changed — it must not leak the NEW address
        // and must carry no confirmation token or link.
        //
        // This used to assert the body contained no '@' at all, a cheap and effective proxy while the
        // mail named no address of any kind. Our own contact address now appears, so the proxy had to
        // go. What replaces it is the property itself — "no address OTHER than ours", one '@'-bearing
        // token at a time.
        //
        // NOT "strictly stronger", which an earlier version of this comment claimed: the two forms are
        // INCOMPARABLE. A body containing only our contact address passes the new form and fails the
        // old one. And the new form is a LOOP, so it runs zero times if the body ever loses the
        // address entirely — its liveness comes from the ShouldContainTheContactAddress fact above,
        // and that dependency is named here rather than left to be discovered (code-reviewer Major 5,
        // 2026-08-12).
        var rendered = EmailTemplates.EmailChangedNotification();

        foreach (var token in rendered.PlainTextBody.Split(
            [' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!token.Contains('@', StringComparison.Ordinal)) continue;

            token.Trim('.', ',', ':', ')').ShouldBe(
                EmailTemplates.ContactAddress,
                $"the notice names an address other than the contact address: {token}");
        }

        rendered.PlainTextBody.ShouldNotContain("token");
        rendered.PlainTextBody.ShouldNotContain("bekrafta-epost");

        // The HTML part too. The mail has carried one since #1325, and a leak into either part is a
        // leak — the sibling fact ShouldCarryNoSiteLink already reads both, so reading one here was an
        // asymmetry rather than a decision (security-auditor Minor, 2026-08-12). Asserted as an
        // absence of the address SHAPE rather than token-by-token, because the HTML part splits
        // differently and the property is the same either way: no second address anywhere.
        rendered.HtmlBody.Replace(EmailTemplates.ContactAddress, string.Empty, StringComparison.Ordinal)
            .ShouldNotContain("@");
        rendered.HtmlBody.ShouldNotContain("token");
        rendered.HtmlBody.ShouldNotContain("bekrafta-epost");
    }

    [Fact]
    public void EmailChangedNotification_ShouldCarryNoSiteLink()
    {
        // The successor to the double-slash theory, and a fact in its own right rather than a
        // consolation prize: this notice deliberately offers no clickable route into the site. It
        // reaches an address that may no longer control the account, so every link it carries is a
        // surface an attacker who triggered the change gets to place in front of the real owner.
        var rendered = EmailTemplates.EmailChangedNotification();

        rendered.PlainTextBody.ShouldNotContain("https://");
        rendered.HtmlBody.ShouldNotContain("href=\"https://");
    }

    [Fact]
    public void EmailChangedNotification_ShouldUseChangedSubject()
        => EmailTemplates.EmailChangedNotification()
            .Subject.ShouldBe("Din e-postadress har ändrats");

    [Fact]
    public void EmailChangedNotification_ShouldNotContainExclamationOrEmDash()
    {
        var rendered = EmailTemplates.EmailChangedNotification();

        rendered.Subject.ShouldNotContain("!");
        rendered.PlainTextBody.ShouldNotContain("!");
        rendered.PlainTextBody.ShouldNotContain("—"); // em-dash
    }
}
