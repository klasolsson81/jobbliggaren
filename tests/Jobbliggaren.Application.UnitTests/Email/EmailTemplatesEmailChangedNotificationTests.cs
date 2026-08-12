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
        // This used to assert the body contained no '@' at all, which was a cheap and effective proxy
        // while the mail named no address of any kind. Our own contact address now appears, so the
        // proxy would have to be deleted or the fact weakened to a substring check. Neither: the
        // property being defended is "no address OTHER than ours", so that is what is asserted, one
        // '@'-bearing token at a time. It is strictly stronger than the old form, which would have
        // passed for any body carrying zero addresses and said nothing about which.
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
