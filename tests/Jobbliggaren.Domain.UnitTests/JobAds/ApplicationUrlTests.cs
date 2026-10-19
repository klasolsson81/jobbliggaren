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
