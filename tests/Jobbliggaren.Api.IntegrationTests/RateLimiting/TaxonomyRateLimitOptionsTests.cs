using Jobbliggaren.Api.RateLimiting;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RateLimiting;

/// <summary>
/// ADR 0043 MAP-3 — taxonomi-endpointen har en EGEN rate-limit-policy
/// (least common mechanism): drift mellan policy-nyckel i
/// RateLimitingExtensions och .RequireRateLimiting på endpointen =
/// silent rate-limit-bypass. Speglar RateLimitingOptionsTests.
/// </summary>
public class TaxonomyRateLimitOptionsTests
{
    [Fact]
    public void TaxonomyReadPolicyKey_IsStable()
    {
        RateLimitingExtensions.TaxonomyReadPolicy.ShouldBe("taxonomy-read");
    }

    [Fact]
    public void Defaults_TaxonomyRead_Is20Per60s()
    {
        var sut = new RateLimitingOptions();

        sut.TaxonomyRead.PermitLimit.ShouldBe(20);
        sut.TaxonomyRead.WindowSeconds.ShouldBe(60);
    }
}
