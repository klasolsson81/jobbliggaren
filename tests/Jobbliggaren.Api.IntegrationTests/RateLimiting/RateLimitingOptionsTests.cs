using Jobbliggaren.Api.RateLimiting;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RateLimiting;

/// <summary>
/// Verifierar defaults + config-binding för <see cref="RateLimitingOptions"/>
/// (TD-21). Defaults är prod-värden — test-miljöer höjer via overlay i ApiFactory.
///
/// Placering i <c>Api.IntegrationTests</c>: testet är rent unit-style (ingen
/// HTTP-yta) men testar Api-projektets konfig-class — där refer-rättigheter finns.
/// Inget separat <c>Api.UnitTests</c>-projekt än.
/// </summary>
public class RateLimitingOptionsTests
{
    [Fact]
    public void Defaults_AccountDeletion_Is1Per60s()
    {
        var sut = new RateLimitingOptions();

        sut.AccountDeletion.PermitLimit.ShouldBe(1);
        sut.AccountDeletion.WindowSeconds.ShouldBe(60);
    }

    [Fact]
    public void Defaults_AuthWrite_Is20Per60s()
    {
        var sut = new RateLimitingOptions();

        sut.AuthWrite.PermitLimit.ShouldBe(20);
        sut.AuthWrite.WindowSeconds.ShouldBe(60);
    }

    [Fact]
    public void Defaults_AuthLoose_Is30Per60s()
    {
        var sut = new RateLimitingOptions();

        sut.AuthLoose.PermitLimit.ShouldBe(30);
        sut.AuthLoose.WindowSeconds.ShouldBe(60);
    }

    [Fact]
    public void Defaults_MeListRead_Is40Per60s()
    {
        var sut = new RateLimitingOptions();

        sut.MeListRead.PermitLimit.ShouldBe(40);
        sut.MeListRead.WindowSeconds.ShouldBe(60);
    }

    [Fact]
    public void Defaults_JobAdStatusBatch_Is60Per60s()
    {
        var sut = new RateLimitingOptions();

        sut.JobAdStatusBatch.PermitLimit.ShouldBe(60);
        sut.JobAdStatusBatch.WindowSeconds.ShouldBe(60);
    }

    [Fact]
    public void Defaults_MeWrite_Is30Per60s()
    {
        var sut = new RateLimitingOptions();

        sut.MeWrite.PermitLimit.ShouldBe(30);
        sut.MeWrite.WindowSeconds.ShouldBe(60);
    }

    [Fact]
    public void SectionName_IsRateLimiting()
    {
        RateLimitingOptions.SectionName.ShouldBe("RateLimiting");
    }

    [Fact]
    public void BindsFromConfiguration_OverlayValues()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:AccountDeletion:PermitLimit"] = "5",
                ["RateLimiting:AccountDeletion:WindowSeconds"] = "120",
                ["RateLimiting:AuthWrite:PermitLimit"] = "100",
                ["RateLimiting:AuthLoose:PermitLimit"] = "1000",
            })
            .Build();

        var bound = config.GetSection(RateLimitingOptions.SectionName)
            .Get<RateLimitingOptions>();

        bound.ShouldNotBeNull();
        bound.AccountDeletion.PermitLimit.ShouldBe(5);
        bound.AccountDeletion.WindowSeconds.ShouldBe(120);
        bound.AuthWrite.PermitLimit.ShouldBe(100);
        bound.AuthLoose.PermitLimit.ShouldBe(1000);
    }

    [Fact]
    public void BindsFromConfiguration_NewMePolicies_OverlayValues()
    {
        // Pre-4 STEG 5 (TD-87 + TD-92) — de tre nya "me"-sektionerna ska binda
        // från config-overlay (MeRateLimitApiFactory sätter dem aggressivt lågt
        // via RateLimiting__*-env-vars).
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:MeListRead:PermitLimit"] = "3",
                ["RateLimiting:MeListRead:WindowSeconds"] = "60",
                ["RateLimiting:JobAdStatusBatch:PermitLimit"] = "3",
                ["RateLimiting:JobAdStatusBatch:WindowSeconds"] = "60",
                ["RateLimiting:MeWrite:PermitLimit"] = "3",
                ["RateLimiting:MeWrite:WindowSeconds"] = "60",
            })
            .Build();

        var bound = config.GetSection(RateLimitingOptions.SectionName)
            .Get<RateLimitingOptions>();

        bound.ShouldNotBeNull();
        bound.MeListRead.PermitLimit.ShouldBe(3);
        bound.MeListRead.WindowSeconds.ShouldBe(60);
        bound.JobAdStatusBatch.PermitLimit.ShouldBe(3);
        bound.JobAdStatusBatch.WindowSeconds.ShouldBe(60);
        bound.MeWrite.PermitLimit.ShouldBe(3);
        bound.MeWrite.WindowSeconds.ShouldBe(60);
    }

    [Fact]
    public void PolicyKeys_AreStable()
    {
        // Stabilitet — policy-nycklar refereras i RequireRateLimiting på
        // endpoints. Drift mellan extension och endpoint = silent rate-limit-bypass.
        RateLimitingExtensions.AccountDeletionPolicy.ShouldBe("account-deletion");
        RateLimitingExtensions.AuthWritePolicy.ShouldBe("auth-write");
        RateLimitingExtensions.AuthLoosePolicy.ShouldBe("auth-loose");
    }

    [Fact]
    public void PolicyKeys_NewMePolicies_AreStable()
    {
        // Pre-4 STEG 5 (TD-87 + TD-92) — nya policy-nyckel-strängar. Drift mellan
        // extension-konstant och endpoint-.RequireRateLimiting(...) = silent
        // rate-limit-bypass. Lås strängarna byte-för-byte.
        RateLimitingExtensions.MeListReadPolicy.ShouldBe("me-list-read");
        RateLimitingExtensions.JobAdStatusBatchPolicy.ShouldBe("job-ad-status-batch");
        RateLimitingExtensions.MeWritePolicy.ShouldBe("me-write");
    }
}
