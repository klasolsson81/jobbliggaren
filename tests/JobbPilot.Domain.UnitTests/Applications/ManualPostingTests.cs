using JobbPilot.Domain.Applications;
using Shouldly;

namespace JobbPilot.Domain.UnitTests.Applications;

// RÖD svit (TDD — implementation följer). Spec: dotnet-architect-design
// 2026-05-17-fas3-stopp3a-architect-design.md §1 (ManualPosting VO).
// Source-parameter STRUKEN (Klas STOPP 3a-villkor, plan §59).
// TD-80 URL scheme-whitelist är IDENTISK med JobAd.ValidateCore (JobAd.cs:177-183).
public class ManualPostingTests
{
    private static readonly DateTimeOffset ExpiresAt =
        new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------
    // Title — obligatorisk non-empty
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithValidTitleAndCompany_ReturnsSuccess()
    {
        var result = ManualPosting.Create("Backend-utvecklare", "Klarna", null, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Title.ShouldBe("Backend-utvecklare");
        result.Value.Company.ShouldBe("Klarna");
        result.Value.Url.ShouldBeNull();
        result.Value.ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public void Create_WithEmptyTitle_ReturnsFailure()
    {
        var result = ManualPosting.Create("", "Klarna", null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ManualPosting.TitleRequired");
    }

    [Fact]
    public void Create_WithWhitespaceTitle_ReturnsFailure()
    {
        var result = ManualPosting.Create("   ", "Klarna", null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ManualPosting.TitleRequired");
    }

    [Fact]
    public void Create_WithNullTitle_ReturnsFailure()
    {
        var result = ManualPosting.Create(null, "Klarna", null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ManualPosting.TitleRequired");
    }

    [Fact]
    public void Create_WithTitleExceedingMaxLength_ReturnsFailure()
    {
        var result = ManualPosting.Create(new string('A', 301), "Klarna", null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ManualPosting.TitleTooLong");
    }

    [Fact]
    public void Create_TrimsTitle()
    {
        var result = ManualPosting.Create("  Backend-utvecklare  ", "Klarna", null, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Title.ShouldBe("Backend-utvecklare");
    }

    // ---------------------------------------------------------------
    // Company — obligatorisk non-empty (skärpt mot skiss: non-nullable)
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithEmptyCompany_ReturnsFailure()
    {
        var result = ManualPosting.Create("Backend-utvecklare", "", null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ManualPosting.CompanyRequired");
    }

    [Fact]
    public void Create_WithWhitespaceCompany_ReturnsFailure()
    {
        var result = ManualPosting.Create("Backend-utvecklare", "   ", null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ManualPosting.CompanyRequired");
    }

    [Fact]
    public void Create_WithNullCompany_ReturnsFailure()
    {
        var result = ManualPosting.Create("Backend-utvecklare", null, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ManualPosting.CompanyRequired");
    }

    [Fact]
    public void Create_WithCompanyExceedingMaxLength_ReturnsFailure()
    {
        var result = ManualPosting.Create("Backend-utvecklare", new string('B', 201), null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ManualPosting.CompanyTooLong");
    }

    [Fact]
    public void Create_TrimsCompany()
    {
        var result = ManualPosting.Create("Backend-utvecklare", "  Klarna  ", null, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Company.ShouldBe("Klarna");
    }

    // ---------------------------------------------------------------
    // Url — frivillig; null OK; giltig http(s) OK
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithNullUrl_ReturnsSuccess()
    {
        var result = ManualPosting.Create("Backend-utvecklare", "Klarna", null, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Url.ShouldBeNull();
    }

    [Fact]
    public void Create_WithWhitespaceUrl_ReturnsSuccessWithNullUrl()
    {
        // Whitespace-URL behandlas som "ingen URL" (IsNullOrWhiteSpace-gren),
        // ej som ogiltig — speglar JobAd-frihet för optional fält.
        var result = ManualPosting.Create("Backend-utvecklare", "Klarna", "   ", null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Url.ShouldBeNull();
    }

    [Theory]
    [InlineData("https://example.com/jobs/123")]
    [InlineData("http://example.com/jobb")]
    public void Create_WithValidHttpScheme_ReturnsSuccess(string url)
    {
        var result = ManualPosting.Create("Backend-utvecklare", "Klarna", url, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Url.ShouldBe(url);
    }

    [Fact]
    public void Create_TrimsUrl()
    {
        var result = ManualPosting.Create(
            "Backend-utvecklare", "Klarna", "  https://example.com/jobb  ", null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Url.ShouldBe("https://example.com/jobb");
    }

    // ---------------------------------------------------------------
    // Url — TD-80 scheme-whitelist (IDENTISK med JobAd.cs:177-183)
    // Förbjuder javascript:/data:/vbscript:/file:/ftp: (XSS/OWASP A01)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://attacker.example.com/")]
    public void Create_WithNonHttpScheme_ReturnsUrlInvalid(string url)
    {
        var result = ManualPosting.Create("Backend-utvecklare", "Klarna", url, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ManualPosting.UrlInvalid");
    }

    [Fact]
    public void Create_WithRelativeUrl_ReturnsUrlInvalid()
    {
        // Inte absolut → ej giltig http(s)-URL (samma som JobAd "not-a-url").
        var result = ManualPosting.Create("Backend-utvecklare", "Klarna", "not-a-url", null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ManualPosting.UrlInvalid");
    }

    [Fact]
    public void Create_WithUrlExceedingMaxLength_ReturnsFailure()
    {
        var longUrl = "https://example.com/" + new string('a', 2000);

        var result = ManualPosting.Create("Backend-utvecklare", "Klarna", longUrl, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ManualPosting.UrlTooLong");
    }

    // ---------------------------------------------------------------
    // ExpiresAt — frivillig, INGEN framtidsvalidering (J1, ingen PublishedAt)
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithNullExpiresAt_ReturnsSuccess()
    {
        var result = ManualPosting.Create("Backend-utvecklare", "Klarna", null, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public void Create_WithFutureExpiresAt_ReturnsSuccess()
    {
        var result = ManualPosting.Create("Backend-utvecklare", "Klarna", null, ExpiresAt);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ExpiresAt.ShouldBe(ExpiresAt);
    }

    [Fact]
    public void Create_WithPastExpiresAt_ReturnsSuccess()
    {
        // Retroaktiv registrering av redan-sökt ansökan — legitimt tillstånd.
        // INGEN framtidsvalidering (architect-design §1, J1-semantik).
        var pastDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var result = ManualPosting.Create("Backend-utvecklare", "Klarna", null, pastDate);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ExpiresAt.ShouldBe(pastDate);
    }

    // ---------------------------------------------------------------
    // Record-equality (samma fyra fält → lika; ingen Source-axel)
    // ---------------------------------------------------------------

    [Fact]
    public void Equality_WithSameFieldValues_AreEqual()
    {
        var a = ManualPosting.Create(
            "Backend-utvecklare", "Klarna", "https://example.com/jobb", ExpiresAt).Value;
        var b = ManualPosting.Create(
            "Backend-utvecklare", "Klarna", "https://example.com/jobb", ExpiresAt).Value;

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Equality_WithDifferentTitle_AreNotEqual()
    {
        var a = ManualPosting.Create("Backend-utvecklare", "Klarna", null, null).Value;
        var b = ManualPosting.Create("Frontend-utvecklare", "Klarna", null, null).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Equality_WithDifferentExpiresAt_AreNotEqual()
    {
        var a = ManualPosting.Create("Backend-utvecklare", "Klarna", null, ExpiresAt).Value;
        var b = ManualPosting.Create("Backend-utvecklare", "Klarna", null, null).Value;

        a.ShouldNotBe(b);
    }
}
