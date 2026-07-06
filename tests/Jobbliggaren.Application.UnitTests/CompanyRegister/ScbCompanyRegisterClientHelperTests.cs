using System.Net;
using System.Text;
using Jobbliggaren.Infrastructure.CompanyRegister.Scb;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #708 (ADR 0091) — unit tests for the SCB client's two pure failure-observability statics:
/// <c>DescribeQuery</c> (serializes a rejected partition's filters for the WARN 5702/5704 log) and
/// <c>ReadReasonAsync</c> (a bounded, single-lined, never-throwing slice of a non-success response body
/// carrying SCB's validator reason). Both are pure — no HTTP stack, no cert, no DB: the response object
/// is built directly with <see cref="StringContent"/>. These pin the diagnostics the 2026-07-05
/// population run lacked (40 unattributed 400s), so a truncated run is diagnosable from the log alone.
/// The descriptor NEVER carries an org.nr by construction (the population channel has no org.nr-valued
/// filter category — CLAUDE.md §5).
/// </summary>
public class ScbCompanyRegisterClientHelperTests
{
    [Fact]
    public void DescribeQuery_SerializesCategoriesNivaAndCodes_WhenMultiFilterQuery()
    {
        // Each filter renders as "Kategori(niva N)=[kod,kod,…]" (niva only when set), joined by "; ",
        // in filter order (no sorting) — so a rejected partition is identifiable from the WARN alone:
        // which rung depth, which shape, which taxonomy codes.
        var query = new ScbQuery([
            new ScbCategoryFilter("SätesKommun", ["0180"]),
            new ScbCategoryFilter("Juridisk form", ["49", "51"]),
            new ScbCategoryFilter("Bransch", ["70100"], BranschNiva: 3),
        ]);

        var descriptor = ScbCompanyRegisterClient.DescribeQuery(query);

        descriptor.ShouldBe("SätesKommun=[0180]; Juridisk form=[49,51]; Bransch(niva 3)=[70100]");
    }

    [Fact]
    public void DescribeQuery_CapsAtMaxWithEllipsis_WhenDescriptorExceeds512()
    {
        // A pathological over-long descriptor (many codes) is capped at 512 chars + a single ellipsis, so
        // the log field can never blow up. 130 five-digit codes ≈ 780 chars before the cap.
        var codes = Enumerable.Range(0, 130)
            .Select(i => (70000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        var query = new ScbQuery([new ScbCategoryFilter("Bransch", codes, BranschNiva: 3)]);

        var descriptor = ScbCompanyRegisterClient.DescribeQuery(query);

        descriptor.Length.ShouldBe(513);                // 512 kept chars + the ellipsis
        descriptor.ShouldEndWith("…");
        descriptor.ShouldStartWith("Bransch(niva 3)=[");
        descriptor[..512].ShouldNotContain("…");        // the kept head is verbatim, ellipsis only appended
    }

    [Fact]
    public async Task ReadReasonAsync_StripsControlCharsToSpaces_SingleLine_WhenBodyHasNewlinesAndTabs()
    {
        // SCB's validator reason often spans lines; each control char (CR, LF, tab) becomes exactly one
        // space (no run-collapsing by design — CR+LF → two spaces) so the reason is a single log line.
        using var response = Response(HttpStatusCode.BadRequest, "rad1\r\nrad2\trad3");

        var reason = await ScbCompanyRegisterClient.ReadReasonAsync(
            response, TestContext.Current.CancellationToken);

        reason.ShouldBe("rad1  rad2 rad3");
        reason.ShouldNotContain("\r");
        reason.ShouldNotContain("\n");
        reason.ShouldNotContain("\t");
    }

    [Fact]
    public async Task ReadReasonAsync_CapsAtMaxWithEllipsis_WhenBodyExceeds500()
    {
        // A runaway body (SCB error page, HTML dump) is bounded to 500 chars + a single ellipsis.
        var body = new string('a', 600);
        using var response = Response(HttpStatusCode.BadRequest, body);

        var reason = await ScbCompanyRegisterClient.ReadReasonAsync(
            response, TestContext.Current.CancellationToken);

        reason.Length.ShouldBe(501);            // 500 kept chars + the ellipsis
        reason.ShouldEndWith("…");
        reason[..500].ShouldBe(new string('a', 500));
    }

    [Fact]
    public async Task ReadReasonAsync_ReturnsEmptyBodyMarker_WhenBodyIsEmpty()
    {
        // An empty rejection body (SCB returns the status with no text) must still yield a stable,
        // non-empty reason token so the log field is never blank.
        using var response = Response(HttpStatusCode.BadRequest, string.Empty);

        var reason = await ScbCompanyRegisterClient.ReadReasonAsync(
            response, TestContext.Current.CancellationToken);

        reason.ShouldBe("(tom svarskropp)");
    }

    [Fact]
    public async Task ReadReasonAsync_ReturnsEmptyBodyMarker_WhenBodyIsOnlyControlChars()
    {
        // A body of ONLY control chars sanitizes+trims to nothing — the stable token must still come
        // back so the log field is never blank (code-reviewer #708 hardening).
        using var response = Response(HttpStatusCode.BadRequest, "\0\0\r\n\t");

        var reason = await ScbCompanyRegisterClient.ReadReasonAsync(
            response, TestContext.Current.CancellationToken);

        reason.ShouldBe("(tom svarskropp)");
    }

    [Fact]
    public async Task ReadReasonAsync_StripsUnicodeLineSeparators_WhenBodyHasZlZpChars()
    {
        // U+2028/U+2029 (Zl/Zp) are NOT char.IsControl but some log viewers render them as line
        // breaks — they must neutralize to spaces like the control chars (security-auditor +
        // code-reviewer #708 hardening).
        using var response = Response(HttpStatusCode.BadRequest, "rad1\u2028rad2\u2029rad3");

        var reason = await ScbCompanyRegisterClient.ReadReasonAsync(
            response, TestContext.Current.CancellationToken);

        reason.ShouldBe("rad1 rad2 rad3");
        reason.ShouldNotContain("\u2028");
        reason.ShouldNotContain("\u2029");
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
}
