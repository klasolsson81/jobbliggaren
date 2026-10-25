using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

public class ApplicationUrlTests
{
    [Theory]
    [InlineData("https://career.example.se/jobs/4567890-integration-developer")]
    [InlineData("http://jobb.example.se/ansok?id=123")]
    [InlineData("HTTPS://EXAMPLE.SE/APPLY")]
    public void ApplicationUrl_FromSource_HttpOrHttps_ReturnsValue(string raw)
    {
        var url = ApplicationUrl.FromSource(raw);

        url.ShouldNotBeNull();
        url.Value.ShouldBe(raw);
    }

    [Fact]
    public void ApplicationUrl_FromSource_SurroundingWhitespace_IsTrimmed()
    {
        var url = ApplicationUrl.FromSource("  https://example.se/apply \n");

        url.ShouldNotBeNull();
        url.Value.ShouldBe("https://example.se/apply");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplicationUrl_FromSource_Blank_ReturnsNull(string? raw) =>
        ApplicationUrl.FromSource(raw).ShouldBeNull();

    [Theory]
    [InlineData("mailto:rekryterare@example.se?subject=Job")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("ftp://example.se/apply")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/jobs/123")]
    [InlineData("www.example.se/apply")]
    [InlineData("Skicka din ansökan via vår hemsida")]
    [InlineData("https://")]
    [InlineData("http:///path")]
    [InlineData("https://%66acebook.com/somepage")]
    [InlineData("https://facebook.com\\.jobb.example.se/x")]
    [InlineData("https://www.linkedin.com\\in\\someone")]
    public void ApplicationUrl_FromSource_NotAnAbsoluteHttpUrl_ReturnsNull(string raw) =>
        ApplicationUrl.FromSource(raw).ShouldBeNull();

    [Theory]
    [InlineData("https://anna@example.se/apply")]
    [InlineData("https://example.se/apply?email=anna@example.se")]
    [InlineData("https://example.se/anna@example.se")]
    [InlineData("https://example.se/apply?email=anna%40example.se")]
    public void ApplicationUrl_FromSource_EmbeddedEmailOrUserInfo_ReturnsNull(string raw) =>
        ApplicationUrl.FromSource(raw).ShouldBeNull();

    [Fact]
    public void ApplicationUrl_FromSource_ValidPersonnummerInUrl_ReturnsNull()
    {
        // 811218-9876 is the repo's canonical synthetic, Luhn-valid personnummer.
        ApplicationUrl.FromSource("https://example.se/ansok?pnr=811218-9876").ShouldBeNull();
    }

    // #2214 PR 1b: a token behind an escape or a form-encoded '+' is the same token to whoever reads the href.
    [Theory]
    [InlineData("https://example.se/ansok/811218%2D9876")]
    [InlineData("https://example.se/ansok/811218%2d9876")]
    [InlineData("https://example.se/ansok?pnr=811218%209876")]
    [InlineData("https://example.se/ansok?pnr=811218%5F9876")]
    [InlineData("https://example.se/ansok?pnr=811218+-+9876")]
    [InlineData("https://example.se/ansok/811218%2D9876%0A1")]
    [InlineData("https://example.se/ansok?pnr=811218%209876%0A1")]
    [InlineData("https://example.se/ansok?pnr=811218%2D9876%09%31")]
    [InlineData("https://example.se/ansok?pnr=1%0A811218%2D9876")]
    public void ApplicationUrl_FromSource_ValidPersonnummerOnlyInADecodedReading_ReturnsNull(string raw) =>
        ApplicationUrl.FromSource(raw).ShouldBeNull();

    // #2214 PR 1b: a line break or a tab escaped inside a token is read as if it were not there.
    [Theory]
    [InlineData("https://example.se/ansok?pnr=811218%0A9876")]
    [InlineData("https://example.se/ansok?pnr=811218%0d9876")]
    [InlineData("https://example.se/ansok?pnr=811218%099876")]
    public void ApplicationUrl_FromSource_ValidPersonnummerSplitByAnEscapedTabOrLineBreak_ReturnsNull(string raw) =>
        ApplicationUrl.FromSource(raw).ShouldBeNull();

    // #2214 PR 1b: a browser drops a raw tab or line break before it reads the address, so the link it follows
    // is not the stored string.
    [Theory]
    [InlineData("https://example.se/ansok?pnr=8112\n189876")]
    [InlineData("https://example.se/ansok?tel=0701\n234567")]
    [InlineData("https://www.linkedin.com/jobs/.\t./in/someone")]
    [InlineData("https://www.linkedin.com/jobs/..\r/in/someone")]
    [InlineData("https://example.se/ansok\u0007/1")]
    public void ApplicationUrl_FromSource_ControlCharacter_ReturnsNull(string raw) =>
        ApplicationUrl.FromSource(raw).ShouldBeNull();

    [Theory]
    [InlineData("https://face\u200Dbook.com/x")]
    [InlineData("https://face\u200Cbook.com/x")]
    [InlineData("https://facebook.com\u200D/x")]
    public void ApplicationUrl_FromSource_HostIdnCannotMap_NeverThrowsAndReturnsNull(string raw) =>
        Should.NotThrow(() => ApplicationUrl.FromSource(raw)).ShouldBeNull();

    [Fact]
    public void ApplicationUrl_FromSource_HostWithALabelTooLongToMap_ReturnsNull() =>
        ApplicationUrl.FromSource("https://" + new string('ä', 70) + ".ｆａｃｅｂｏｏｋ.com/somepage").ShouldBeNull();

    [Fact]
    public void ApplicationUrl_FromSource_LuhnInvalidNumberBehindAnEscape_ReturnsValue() =>
        ApplicationUrl.FromSource("https://example.se/ansok/811218%2D9877").ShouldNotBeNull();

    [Fact]
    public void ApplicationUrl_Readings_AreTheStoredThePercentDecodedAndTheFormDecodedForms()
    {
        ApplicationUrl.Readings("https://example.se/a?q=b+c%20d").ShouldBe(
            ["https://example.se/a?q=b+c%20d", "https://example.se/a?q=b+c d", "https://example.se/a?q=b c d"]);
    }

    [Fact]
    public void ApplicationUrl_Readings_AddTheDecodedFormsWithAnEscapedTabOrLineBreakDropped() =>
        ApplicationUrl.Readings("https://example.se/a%0Ab%09c").ShouldBe(
            ["https://example.se/a%0Ab%09c", "https://example.se/a\nb\tc", "https://example.se/abc"]);

    [Fact]
    public void ApplicationUrl_Readings_OfAnAddressWithNothingToDecode_IsTheAddressOnce() =>
        ApplicationUrl.Readings("https://example.se/apply").ShouldBe(["https://example.se/apply"]);

    [Theory]
    [InlineData("https://example.se/a%")]
    [InlineData("https://example.se/a%G0")]
    [InlineData("https://example.se/a%E2%82")]
    public void ApplicationUrl_FromSource_InvalidEscape_NeverThrowsAndKeepsTheAddress(string raw)
    {
        var url = Should.NotThrow(() => ApplicationUrl.FromSource(raw));

        url.ShouldNotBeNull().Value.ShouldBe(raw);
    }

    [Fact]
    public void ApplicationUrl_FromSource_LongNumericIdThatIsNoPersonnummer_ReturnsValue()
    {
        // A 10-digit ATS id that fails the date check: an id must not cost the user the link.
        var url = ApplicationUrl.FromSource("https://example.se/jobs/9999999999");

        url.ShouldNotBeNull();
    }

    [Fact]
    public void ApplicationUrl_FromSource_LongerThanMaxLength_ReturnsNull()
    {
        var raw = "https://example.se/" + new string('a', ApplicationUrl.MaxLength);

        ApplicationUrl.FromSource(raw).ShouldBeNull();
    }

    [Fact]
    public void ApplicationUrl_FromSource_ExactlyMaxLength_ReturnsValue()
    {
        const string prefix = "https://example.se/";
        var raw = prefix + new string('a', ApplicationUrl.MaxLength - prefix.Length);

        ApplicationUrl.FromSource(raw).ShouldNotBeNull();
    }

    [Fact]
    public void ApplicationUrl_FromTrusted_KeepsTheValue() =>
        ApplicationUrl.FromTrusted("https://example.se/apply").Value.ShouldBe("https://example.se/apply");

    [Fact]
    public void ApplicationUrl_SameValue_AreEqual() =>
        ApplicationUrl.FromSource("https://example.se/apply")
            .ShouldBe(ApplicationUrl.FromSource("https://example.se/apply"));
}
