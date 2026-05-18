using JobbPilot.Infrastructure.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Common.Security;

/// <summary>
/// TD-13 — FieldEncryptionOptionsValidator (ADR 0049 Mekanik-not 2 Approach D
/// + security-auditor C3 Medium 1 EU-residens-guard). Miljö-villkorad
/// fail-closed: hård fail Production/Staging, warning Development/Test.
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
}
