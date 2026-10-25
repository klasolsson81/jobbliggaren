using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Publication;

/// <summary>
/// The public gate on the application link (#2214 PR 1b, ADR 0166 D10 trigger 19).
/// </summary>
public class PublicApplicationLinkTests
{
    private static ApplicationUrl Stored(string raw) => ApplicationUrl.FromSource(raw).ShouldNotBeNull(
        $"the premise must be a link the import stores: {raw}");

    [Theory]
    [InlineData("https://jobb.example.se/ansok/123")]
    [InlineData("https://career.example.se/jobs/4567890-integration-developer?utm_source=platsbanken")]
    [InlineData("HTTPS://JOBB.EXAMPLE.SE/Ansok")]
    [InlineData("https://karriär.example.se/ansok")]
    [InlineData("https://jobb2.example.se/ansok")]
    [InlineData("https://mitt-bolag.example.se/ansok")]
    public void Admit_EmployerApplicationPage_IsTheStoredValueUnchanged(string raw) =>
        PublicApplicationLink.Admit(Stored(raw)).ShouldBe(raw);

    [Fact]
    public void Admit_NoLink_IsNull() =>
        PublicApplicationLink.Admit(null).ShouldBeNull();

    // Tier A in every reading: as stored, percent-decoded and form-decoded.
    [Theory]
    [InlineData("https://jobb.example.se/ansok?tel=0701234567")]
    [InlineData("https://jobb.example.se/kontakt/08-123%20456%2078")]
    [InlineData("https://jobb.example.se/ansok?p=%2B46701234567")]
    [InlineData("https://jobb.example.se/ansok?p=%2b46701234567")]
    [InlineData("https://jobb.example.se/ansok?p=070%20123%2045%2067")]
    [InlineData("https://jobb.example.se/ansok?p=070+123+45+67")]
    [InlineData("https://jobb.example.se/ansok?p=+46%2070%20123%2045%2067")]
    [InlineData("https://jobb.example.se/ansok?p=0%0A701234567")]
    [InlineData("https://jobb.example.se/ansok?p=070%20123%2045%2067%0A12")]
    [InlineData("https://jobb.example.se/ansok?p=070+123+45+67%0A12")]
    [InlineData("https://jobb.example.se/ansok?p=%2B46%2070%20123%2045%2067%0A12")]
    [InlineData("https://jobb.example.se/ansok?p=1%0A070%20123%2045%2067")]
    [InlineData("https://jobb.example.se/kontakt/x%09+46701234567")]
    [InlineData("https://jobb.example.se/ansok/ref%0A0701234567")]
    public void Admit_PhoneNumberInAnyReading_IsNull(string raw) =>
        PublicApplicationLink.Admit(Stored(raw)).ShouldBeNull();

    // Tier A's own envelope and anchor: 7-11 digits, and an anchor glued to a letter, digit or hyphen is an id.
    [Theory]
    [InlineData("https://jobb.example.se/ansok?p=070123")]
    [InlineData("https://jobb.example.se/ansok?p=070123456789")]
    [InlineData("https://jobb.example.se/ansok/ref0701234567")]
    [InlineData("https://ats.example.se/job/9999999999")]
    public void Admit_DigitRunTierADoesNotReadAsAPhone_IsTheStoredValue(string raw) =>
        PublicApplicationLink.Admit(Stored(raw)).ShouldBe(raw);

    [Theory]
    [InlineData("https://www.facebook.com/somepage")]
    [InlineData("https://FACEBOOK.COM/somepage")]
    [InlineData("https://facebook.com./somepage")]
    [InlineData("https://m.facebook.com/somepage")]
    [InlineData("https://www.instagram.com/someone/")]
    [InlineData("https://wa.me/46701234567")]
    [InlineData("https://t.me/someone")]
    [InlineData("https://calendly.com/someone/intervju")]
    [InlineData("https://bit.ly/abc")]
    [InlineData("https://lnkd.in/abc")]
    [InlineData("https://t.co/abc")]
    [InlineData("https://ｆａｃｅｂｏｏｋ.com/somepage")]
    public void Admit_ContactHostOrShortener_IsNull(string raw) =>
        PublicApplicationLink.Admit(Stored(raw)).ShouldBeNull();

    [Fact]
    public void Admit_EveryListedHostAndItsSubdomains_IsNull()
    {
        foreach (var host in PublicApplicationLink.ContactHosts.Concat(PublicApplicationLink.Shorteners))
        {
            PublicApplicationLink.Admit(Stored($"https://{host}/x")).ShouldBeNull(host);
            PublicApplicationLink.Admit(Stored($"https://www.{host}/x")).ShouldBeNull($"www.{host}");
        }
    }

    [Theory]
    [InlineData("https://notfacebook.com/jobb")]
    [InlineData("https://facebook.com.example.se/jobb")]
    [InlineData("https://www.netflix.com/jobs/1")]
    [InlineData("https://notbit.ly/abc")]
    [InlineData("https://bit.ly.example.se/abc")]
    [InlineData("https://at.co/jobb")]
    public void Admit_HostThatOnlyLooksLikeAListedOne_IsTheStoredValue(string raw) =>
        PublicApplicationLink.Admit(Stored(raw)).ShouldBe(raw);

    [Theory]
    [InlineData("https://www.linkedin.com/in/someone")]
    [InlineData("https://www.linkedin.com/IN/someone")]
    [InlineData("https://www.linkedin.com/%69n/someone")]
    [InlineData("https://www.linkedin.com//in/someone")]
    [InlineData("https://www.linkedin.com/pub/someone/1/2/3")]
    [InlineData("https://www.linkedin.com/posts/someone_jobb-activity-1")]
    [InlineData("https://www.linkedin.com/")]
    [InlineData("https://www.linkedin.com/comm/jobs/view/1")]
    // A dot segment, plain or escaped, is resolved before the path is read, as a browser resolves it.
    [InlineData("https://www.linkedin.com/jobs/%2e%2e/in/someone")]
    [InlineData("https://www.linkedin.com/jobs/.%2e/in/someone")]
    [InlineData("https://www.linkedin.com/jobs/%2e./in/someone")]
    [InlineData("https://www.linkedin.com/jobs/%2E%2E/in/someone")]
    [InlineData("https://www.linkedin.com/jobs/%2e%2E/in/someone")]
    [InlineData("https://www.linkedin.com/jobs/../in/someone")]
    public void Admit_LinkedInPageThatIsNotAJobOrCompanyPage_IsNull(string raw) =>
        PublicApplicationLink.Admit(Stored(raw)).ShouldBeNull();

    [Theory]
    [InlineData("https://www.linkedin.com/jobs/view/1234567890")]
    [InlineData("https://se.linkedin.com/company/exempelbolaget")]
    [InlineData("https://www.linkedin.com/Jobs/view/1")]
    public void Admit_LinkedInJobOrCompanyPage_IsTheStoredValue(string raw) =>
        PublicApplicationLink.Admit(Stored(raw)).ShouldBe(raw);

    [Theory]
    [InlineData("https://face_book.com/somepage")]
    [InlineData("https://mitt_bolag.example.se/ansok")]
    public void Admit_HostThatIsNotLdhAscii_IsNull(string raw) =>
        PublicApplicationLink.Admit(Stored(raw)).ShouldBeNull();

    [Fact]
    public void Admit_HostWithALabelTooLongToMap_NeverReachesThePublicRead()
    {
        var raw = "https://" + new string('ä', 70) + ".ｆａｃｅｂｏｏｋ.com/somepage";

        PublicApplicationLink.Admit(ApplicationUrl.FromSource(raw)).ShouldBeNull();
    }

    // Rows stored before #2214 PR 1b were written by a FromSource that scanned the stored form alone, kept a tab or a
    // line break and did not map the host; EF materialises them through FromTrusted. The current writer refuses each
    // (ApplicationUrlTests: ApplicationUrl_FromSource_ValidPersonnummerOnlyInADecodedReading_ReturnsNull,
    // ApplicationUrl_FromSource_ControlCharacter_ReturnsNull, ApplicationUrl_FromSource_HostIdnCannotMap_NeverThrowsAndReturnsNull),
    // and the gate runs it again on the read.
    [Theory]
    [InlineData("https://jobb.example.se/ansok/811218%2D9876")]
    [InlineData("https://jobb.example.se/ansok?pnr=8112\n189876")]
    [InlineData("https://www.linkedin.com/jobs/.\t./in/someone")]
    [InlineData("https://face\u200Dbook.com/x")]
    public void Admit_RowStoredBeforeThisRuleTightened_NeverThrowsAndIsNull(string stored) =>
        Should.NotThrow(() => PublicApplicationLink.Admit(ApplicationUrl.FromTrusted(stored))).ShouldBeNull();

    [Theory]
    [InlineData("https://jobb.example.se/a%")]
    [InlineData("https://jobb.example.se/a%G0")]
    [InlineData("https://jobb.example.se/a%E2%82")]
    public void Admit_InvalidEscape_NeverThrowsAndKeepsTheLink(string raw) =>
        Should.NotThrow(() => PublicApplicationLink.Admit(Stored(raw))).ShouldBe(raw);

    // The gate narrowed by removing a host is trigger 19: the lists are pinned whole.
    [Fact]
    public void ContactHosts_AreExactlyThese() =>
        PublicApplicationLink.ContactHosts.Order(StringComparer.Ordinal).ToArray().ShouldBe(
        [
            "bsky.app", "calendly.com", "facebook.com", "fb.com", "fb.me", "instagr.am", "instagram.com", "m.me",
            "messenger.com", "signal.group", "signal.me", "snapchat.com", "t.me", "telegram.me", "threads.com",
            "threads.net", "tiktok.com", "twitter.com", "wa.link", "wa.me", "whatsapp.com", "x.com",
        ]);

    [Fact]
    public void Shorteners_AreExactlyThese() =>
        PublicApplicationLink.Shorteners.Order(StringComparer.Ordinal).ToArray().ShouldBe(
        [
            "bit.ly", "buff.ly", "cutt.ly", "goo.gl", "is.gd", "lnkd.in", "ow.ly", "rb.gy", "rebrand.ly", "s.id",
            "shorturl.at", "t.co", "t.ly", "tiny.cc", "tinyurl.com",
        ]);

    // Here a segment ADDED widens the gate: the allowlist is pinned whole too.
    [Fact]
    public void AllowedFirstSegments_AreExactlyLinkedInsJobAndCompanyPaths()
    {
        PublicApplicationLink.AllowedFirstSegments.Keys.ToArray().ShouldBe(["linkedin.com"]);
        PublicApplicationLink.AllowedFirstSegments["linkedin.com"].Order(StringComparer.Ordinal).ToArray().ShouldBe(["company", "jobs"]);
    }
}
