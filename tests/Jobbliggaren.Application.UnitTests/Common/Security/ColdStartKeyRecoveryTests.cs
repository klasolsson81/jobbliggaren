using System.Security.Cryptography;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Configuration;
using Jobbliggaren.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Security;

public sealed class ColdStartKeyRecoveryTests : IDisposable
{
    private const string MasterKey = "FieldEncryption:LocalMasterKeyBase64";
    private const string MasterId = "FieldEncryption:LocalMasterKeyId";
    private const string Generation = "synthetic-generation-2";
    private const string Plaintext = "Synthetic recovery fixture";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jbl-recovery-" + Guid.NewGuid());

    public ColdStartKeyRecoveryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ColdStart_RestoredKeyAndIdentity_DecryptsPreviouslyWrittenFieldAsync()
    {
        var escrow = NewKey();
        var owner = JobSeekerId.New();
        var written = await Provider(escrow, Generation).CreateDataKeyAsync(owner, CancellationToken.None);
        var fields = new AesGcmFieldEncryptor();
        var ciphertext = fields.Encrypt(Plaintext, written.PlaintextDek);
        var binaryFields = new BinaryFieldEncryptor();
        var binaryCiphertext = binaryFields.Encrypt("Synthetic file content"u8, written.PlaintextDek);
        CryptographicOperations.ZeroMemory(written.PlaintextDek);

        var configuration = Restore(new Dictionary<string, string?> { [MasterKey] = escrow, [MasterId] = Generation });
        var recovered = Provider(configuration);
        var dek = await recovered.UnwrapDataKeyAsync(owner, written.WrappedDek, CancellationToken.None);
        try
        {
            fields.Decrypt(ciphertext, dek).ShouldBe(Plaintext);
            binaryFields.Decrypt(binaryCiphertext, dek).ShouldBe("Synthetic file content"u8.ToArray());
            var next = await recovered.CreateDataKeyAsync(owner, CancellationToken.None);
            try { next.CmkKeyId.ShouldBe(written.CmkKeyId); }
            finally { CryptographicOperations.ZeroMemory(next.PlaintextDek); }
        }
        finally { CryptographicOperations.ZeroMemory(dek); }
    }

    [Fact]
    public async Task ColdStart_WrongBytesUnderCorrectIdentity_RefusesOldEnvelopeAsync()
    {
        var owner = JobSeekerId.New();
        var written = await Provider(NewKey(), Generation).CreateDataKeyAsync(owner, CancellationToken.None);
        CryptographicOperations.ZeroMemory(written.PlaintextDek);
        var configuration = Restore(new Dictionary<string, string?> { [MasterKey] = NewKey(), [MasterId] = Generation });
        new FieldEncryptionOptionsValidator().Validate(null, Bind(configuration)).Succeeded.ShouldBeTrue();

        await Should.ThrowAsync<CryptographicException>(() =>
            Provider(configuration).UnwrapDataKeyAsync(owner, written.WrappedDek, CancellationToken.None));
    }

    [Theory]
    [InlineData(null, "local-v1")]
    [InlineData("synthetic-wrong-label", "synthetic-wrong-label")]
    public async Task ColdStart_CorrectBytesWithoutMatchingIdentity_DecryptsButStampsDifferentIdentityAsync(
        string? restoredId, string expectedId)
    {
        // The operator can omit/mislabel the identity; unwrap does not consume cmk_key_id.
        var escrow = NewKey();
        var owner = JobSeekerId.New();
        var written = await Provider(escrow, Generation).CreateDataKeyAsync(owner, CancellationToken.None);
        var fields = new AesGcmFieldEncryptor();
        var ciphertext = fields.Encrypt(Plaintext, written.PlaintextDek);
        CryptographicOperations.ZeroMemory(written.PlaintextDek);
        var recovered = Provider(Restore(new Dictionary<string, string?> { [MasterKey] = escrow, [MasterId] = restoredId }));
        var dek = await recovered.UnwrapDataKeyAsync(owner, written.WrappedDek, CancellationToken.None);
        try { fields.Decrypt(ciphertext, dek).ShouldBe(Plaintext); }
        finally { CryptographicOperations.ZeroMemory(dek); }
        var next = await recovered.CreateDataKeyAsync(owner, CancellationToken.None);
        try
        {
            next.CmkKeyId.ShouldBe(expectedId);
            next.CmkKeyId.ShouldNotBe(written.CmkKeyId);
        }
        finally { CryptographicOperations.ZeroMemory(next.PlaintextDek); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ColdStart_AbsentKeyWithoutFallback_RefusesProvider(string? material)
    {
        var configuration = Restore(new Dictionary<string, string?> { [MasterKey] = material, [MasterId] = Generation });
        new FieldEncryptionOptionsValidator().Validate(null, Bind(configuration)).Failed.ShouldBeTrue();
        Should.Throw<CryptographicException>(() => Provider(configuration));
    }

    [Fact]
    public void ColdStart_MissingFileWithFallback_RefusesConfiguration()
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [MasterKey] = NewKey() });
        builder.Add(new EnvFileSecretsConfigurationSource(
            () => new Dictionary<string, string?> { [MasterKey.Replace(":", "__") + "_FILE"] = Path.Combine(_root, "missing") }));
        Should.Throw<InvalidOperationException>(() => builder.Build());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ColdStart_EmptyFileWithStaleFallback_RefusesProvider(string material)
    {
        // An incomplete operator write leaves a blank file, which must mask stale fallback material.
        var configuration = Restore(new Dictionary<string, string?> { [MasterKey] = material, [MasterId] = Generation },
            new Dictionary<string, string?> { [MasterKey] = NewKey() });
        new FieldEncryptionOptionsValidator().Validate(null, Bind(configuration)).Failed.ShouldBeTrue();
        Should.Throw<CryptographicException>(() => Provider(configuration));
    }

    [Theory]
    [InlineData("AuditPseudonymization")]
    [InlineData("CompanyWatchPseudonymization")]
    [InlineData("CvReviewFingerprintPseudonymization")]
    public void ColdStart_PartialPepperInjection_RejectsOnlyMissingCategory(string missingSection)
    {
        var material = new Dictionary<string, string?>
        {
            [MasterKey] = NewKey(),
            [MasterId] = Generation,
            ["AuditPseudonymization:PepperBase64"] = NewKey(),
            ["CompanyWatchPseudonymization:PepperBase64"] = NewKey(),
            ["CvReviewFingerprintPseudonymization:PepperBase64"] = NewKey(),
        };
        material[missingSection + ":PepperBase64"] = " ";
        var configuration = Restore(material);
        new FieldEncryptionOptionsValidator().Validate(null, Bind(configuration)).Succeeded.ShouldBeTrue();
        new AuditPseudonymizationOptionsValidator().Validate(null,
            configuration.GetSection("AuditPseudonymization").Get<AuditPseudonymizationOptions>() ?? new()).Failed
            .ShouldBe(missingSection == "AuditPseudonymization");
        new CompanyWatchPseudonymizationOptionsValidator().Validate(null,
            configuration.GetSection("CompanyWatchPseudonymization").Get<CompanyWatchPseudonymizationOptions>() ?? new()).Failed
            .ShouldBe(missingSection == "CompanyWatchPseudonymization");
        new CvReviewFingerprintPseudonymizationOptionsValidator().Validate(null,
            configuration.GetSection("CvReviewFingerprintPseudonymization").Get<CvReviewFingerprintPseudonymizationOptions>() ?? new()).Failed
            .ShouldBe(missingSection == "CvReviewFingerprintPseudonymization");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ColdStart_RestoredPeppers_RequireOriginalBytesForContinuity(bool sameMaterial)
    {
        var audit = NewKey();
        var watch = NewKey();
        var finding = NewKey();
        var verdict = CvCriterionVerdict.Assessed("A1", RubricCategory.Content, CriterionVerdict.Fail,
            [new StructuralEvidence("kontakt")]);
        var rubric = RubricVersion.Parse("1.1.0");
        var previousAudit = new HmacIdentifierPseudonymizer(Options.Create(new AuditPseudonymizationOptions
        { PepperBase64 = audit })).Pseudonymize("recovery@example.invalid");
        var previousWatch = new HmacProtectedIdentityTokenizer(Options.Create(new CompanyWatchPseudonymizationOptions
        { PepperBase64 = watch })).Tokenize("5560160680");
        var previousFinding = new HmacFindingFingerprinter(Options.Create(new CvReviewFingerprintPseudonymizationOptions
        { PepperBase64 = finding })).Compute(rubric, verdict);
        var configuration = Restore(new Dictionary<string, string?>
        {
            ["AuditPseudonymization:PepperBase64"] = sameMaterial ? audit : NewKey(),
            ["CompanyWatchPseudonymization:PepperBase64"] = sameMaterial ? watch : NewKey(),
            ["CvReviewFingerprintPseudonymization:PepperBase64"] = sameMaterial ? finding : NewKey(),
        });
        var restoredAudit = new HmacIdentifierPseudonymizer(Options.Create(
            configuration.GetSection("AuditPseudonymization").Get<AuditPseudonymizationOptions>()!));
        var restoredWatch = new HmacProtectedIdentityTokenizer(Options.Create(
            configuration.GetSection("CompanyWatchPseudonymization").Get<CompanyWatchPseudonymizationOptions>()!));
        var restoredFinding = new HmacFindingFingerprinter(Options.Create(
            configuration.GetSection("CvReviewFingerprintPseudonymization").Get<CvReviewFingerprintPseudonymizationOptions>()!));
        (restoredAudit.Pseudonymize("recovery@example.invalid") == previousAudit).ShouldBe(sameMaterial);
        (restoredWatch.Tokenize("5560160680") == previousWatch).ShouldBe(sameMaterial);
        (restoredFinding.Compute(rubric, verdict) == previousFinding).ShouldBe(sameMaterial);
    }

    [Theory]
    [InlineData("restored")]
    [InlineData("missing")]
    [InlineData("wrong")]
    [InlineData("wrong-application")]
    public void ColdStart_DataProtectionRing_RequiresOriginalKeyMaterial(string recovery)
    {
        var originalPath = Path.Combine(_root, "original");
        string protectedValue;
        using (var original = DataProtection(originalPath))
            protectedValue = original.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("synthetic-recovery-probe").Protect(Plaintext);

        var restoredPath = Path.Combine(_root, "restored");
        Directory.CreateDirectory(restoredPath);
        if (recovery is "restored" or "wrong-application")
        {
            foreach (var file in Directory.GetFiles(originalPath, "*.xml"))
                File.Copy(file, Path.Combine(restoredPath, Path.GetFileName(file)));
        }
        else if (recovery == "wrong")
        {
            using var unrelated = DataProtection(restoredPath);
            unrelated.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("synthetic-recovery-probe").Protect(Plaintext);
        }

        using var restored = DataProtection(restoredPath, recovery == "wrong-application");
        var protector = restored.GetRequiredService<IDataProtectionProvider>().CreateProtector("synthetic-recovery-probe");
        if (recovery == "restored") protector.Unprotect(protectedValue).ShouldBe(Plaintext);
        else Should.Throw<CryptographicException>(() => protector.Unprotect(protectedValue));
    }

    private IConfigurationRoot Restore(Dictionary<string, string?> material, Dictionary<string, string?>? fallback = null)
    {
        var pointers = new Dictionary<string, string?>();
        foreach (var (key, value) in material)
        {
            if (value is null) continue;
            var name = key.Replace(":", "__");
            var path = Path.Combine(_root, name);
            File.WriteAllText(path, value);
            pointers[name + "_FILE"] = path;
        }
        var builder = new ConfigurationBuilder().AddInMemoryCollection(fallback ?? []);
        builder.Add(new EnvFileSecretsConfigurationSource(() => pointers));
        return builder.Build();
    }

    private static FieldEncryptionOptions Bind(IConfiguration configuration) =>
        configuration.GetSection(FieldEncryptionOptions.SectionName).Get<FieldEncryptionOptions>() ?? new();

    private static LocalDataKeyProvider Provider(IConfiguration configuration) =>
        new(Options.Create(Bind(configuration)), NullLogger<LocalDataKeyProvider>.Instance);

    private static LocalDataKeyProvider Provider(string key, string id) => new(
        Options.Create(new FieldEncryptionOptions { LocalMasterKeyBase64 = key, LocalMasterKeyId = id }),
        NullLogger<LocalDataKeyProvider>.Instance);

    private static ServiceProvider DataProtection(string path, bool wrongApplication = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DependencyInjection.DataProtectionKeyPathConfigKey] = path,
        }).Build();
        var services = new ServiceCollection().AddLogging().AddApiDataProtection(configuration);
        if (wrongApplication) services.AddDataProtection().SetApplicationName("synthetic-unrelated-application");
        return services.BuildServiceProvider();
    }

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
