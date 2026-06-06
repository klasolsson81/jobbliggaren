using System.Security.Cryptography;
using JobbPilot.Infrastructure.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Common.Security;

/// <summary>
/// TD-13 — FieldEncryptionOptionsValidator (ADR 0049 Mekanik-not 2 Approach D
/// + security-auditor C3 Medium 1 EU-residens-guard). Miljö-villkorad
/// fail-closed: hård fail Production/Staging, warning Development/Test.
///
/// ADR 0066 (AWS-avveckling): Provider-axeln väljer guard-gren. Local-grenen
/// validerar master-nyckeln och hård-failar i ALLA miljöer (till skillnad från
/// Kms-grenens CmkKeyId som warnar i Development/Test) — en degraderad lokal
/// nyckel får aldrig tyst släppas igenom. AwsRegion/CmkKeyId-guarderna är no-op
/// i Local-grenen.
/// </summary>
public class FieldEncryptionOptionsValidatorTests
{
    private sealed class FakeEnv(string envName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = envName;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static FieldEncryptionOptionsValidator Validator(string env) =>
        new(new FakeEnv(env), NullLogger<FieldEncryptionOptionsValidator>.Instance);

    // Giltig 32-byte (AES-256) lokal master-nyckel i base64.
    private static string ValidMasterKeyBase64() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void EmptyCmkKeyId_InProductionOrStaging_Fails(string env)
    {
        var result = Validator(env).Validate(null,
            new FieldEncryptionOptions { CmkKeyId = "", AwsRegion = "eu-north-1" });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("CmkKeyId");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void EmptyCmkKeyId_InDevelopmentOrTest_SucceedsWithWarning(string env)
    {
        var result = Validator(env).Validate(null,
            new FieldEncryptionOptions { CmkKeyId = "", AwsRegion = "eu-north-1" });

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void NonEuRegion_InProductionOrStaging_Fails(string env)
    {
        var result = Validator(env).Validate(null,
            new FieldEncryptionOptions
            {
                CmkKeyId = "arn:aws:kms:us-east-1:0:key/x",
                AwsRegion = "us-east-1",
            });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("EU");
    }

    [Fact]
    public void EuRegion_WithCmkKeyId_Succeeds()
    {
        var result = Validator("Production").Validate(null,
            new FieldEncryptionOptions
            {
                CmkKeyId = "arn:aws:kms:eu-north-1:0:key/x",
                AwsRegion = "eu-north-1",
            });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void NonEuRegion_InDevelopment_DoesNotHardFail()
    {
        // Dev/Test: icke-EU hård-failar ej (paritet CmkKeyId-warn); fake-KMS
        // används där ändå. Default eu-north-1 gör detta sällsynt.
        var result = Validator("Development").Validate(null,
            new FieldEncryptionOptions { CmkKeyId = "x", AwsRegion = "us-east-1" });

        result.Succeeded.ShouldBeTrue();
    }

    // --- ADR 0066: Local-grenen ---

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    [InlineData("Test")]
    public void LocalProvider_WithValid32ByteMasterKey_Succeeds(string env)
    {
        // Giltig master-nyckel → Success i ALLA miljöer (inkl. Production).
        var result = Validator(env).Validate(null,
            new FieldEncryptionOptions
            {
                Provider = "Local",
                LocalMasterKeyBase64 = ValidMasterKeyBase64(),
            });

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    [InlineData("Test")]
    public void LocalProvider_WithEmptyMasterKey_FailsInAllEnvironments(string env)
    {
        // Till skillnad från CmkKeyId hård-failar tom master-nyckel även i
        // Development/Test — ingen tyst degradering.
        var result = Validator(env).Validate(null,
            new FieldEncryptionOptions { Provider = "Local", LocalMasterKeyBase64 = "" });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("LocalMasterKeyBase64");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void LocalProvider_WithInvalidBase64_Fails(string env)
    {
        var result = Validator(env).Validate(null,
            new FieldEncryptionOptions
            {
                Provider = "Local",
                LocalMasterKeyBase64 = "inte!giltig!base64!",
            });

        result.Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void LocalProvider_WithWrongLengthMasterKey_Fails(string env)
    {
        // 16 byte = AES-128 — för svag, måste fail-closed.
        var result = Validator(env).Validate(null,
            new FieldEncryptionOptions
            {
                Provider = "Local",
                LocalMasterKeyBase64 = Convert.ToBase64String(new byte[16]),
            });

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void LocalProvider_IgnoresNonEuAwsRegionAndEmptyCmkKeyId()
    {
        // Local-grenen bryr sig inte om AwsRegion/CmkKeyId — guarderna är no-op.
        // us-east-1 + tom CmkKeyId i Production ska ändå ge Success.
        var result = Validator("Production").Validate(null,
            new FieldEncryptionOptions
            {
                Provider = "Local",
                LocalMasterKeyBase64 = ValidMasterKeyBase64(),
                AwsRegion = "us-east-1",
                CmkKeyId = "",
            });

        result.Succeeded.ShouldBeTrue();
    }

    // --- ADR 0066 regression: Kms-grenen oförändrad vid explicit Provider="Kms" ---

    [Fact]
    public void KmsProvider_Explicit_StillEnforcesCmkKeyIdInProduction()
    {
        var result = Validator("Production").Validate(null,
            new FieldEncryptionOptions
            {
                Provider = "Kms",
                CmkKeyId = "",
                AwsRegion = "eu-north-1",
            });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("CmkKeyId");
    }
}
