using System.Security.Cryptography;
using Jobbliggaren.Infrastructure.Security;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Security;

/// <summary>
/// FieldEncryptionOptionsValidator (ADR 0049 / ADR 0066). Efter AWS-exiten
/// (#802) är Local den enda DEK-providern; validatorn kör provider- och
/// miljö-agnostiskt (den tidigare miljö-villkorade Kms-grenen med CmkKeyId +
/// EU-region-guard är borttagen). Master-nyckel-guarden hård-failar i ALLA
/// miljöer — en saknad/degraderad lokal nyckel får aldrig tyst släppas igenom
/// (det SER krypterat ut = värre än ingen kryptering). Provider-axeln
/// fail-stoppas separat i DI (AddPersistence kastar på ett explicit
/// icke-Local-värde).
/// </summary>
public class FieldEncryptionOptionsValidatorTests
{
    private static FieldEncryptionOptionsValidator Validator() => new();

    // Giltig 32-byte (AES-256) lokal master-nyckel i base64.
    private static string ValidMasterKeyBase64() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Valid32ByteMasterKey_Succeeds()
    {
        var result = Validator().Validate(null,
            new FieldEncryptionOptions
            {
                Provider = "Local",
                LocalMasterKeyBase64 = ValidMasterKeyBase64(),
            });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void EmptyMasterKey_FailsInAllEnvironments()
    {
        // Miljö-agnostiskt sedan Kms-grenen togs bort (#802): en tom master-nyckel
        // hård-failar överallt — ingen tyst degradering.
        var result = Validator().Validate(null,
            new FieldEncryptionOptions { Provider = "Local", LocalMasterKeyBase64 = "" });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("LocalMasterKeyBase64");
    }

    [Fact]
    public void BlankMasterKeyId_Fails()
    {
        // #198 (M-3): the key identity is not a secret, but it is the re-wrap operation's
        // idempotency marker. A blank one stamps empty cmk_key_id values, and the next
        // rotation would select on a value it can neither match nor distinguish from
        // "never stamped".
        var result = Validator().Validate(null,
            new FieldEncryptionOptions
            {
                Provider = "Local",
                LocalMasterKeyBase64 = ValidMasterKeyBase64(),
                LocalMasterKeyId = "   ",
            });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("LocalMasterKeyId");
    }

    [Fact]
    public void OmittedMasterKeyId_DefaultsAndSucceeds()
    {
        // Dev and every pre-rotation row are unchanged by the new option.
        var result = Validator().Validate(null,
            new FieldEncryptionOptions
            {
                Provider = "Local",
                LocalMasterKeyBase64 = ValidMasterKeyBase64(),
            });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void InvalidBase64MasterKey_Fails()
    {
        var result = Validator().Validate(null,
            new FieldEncryptionOptions
            {
                Provider = "Local",
                LocalMasterKeyBase64 = "inte!giltig!base64!",
            });

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WrongLengthMasterKey_Fails()
    {
        // 16 byte = AES-128 — för svag, måste fail-closed.
        var result = Validator().Validate(null,
            new FieldEncryptionOptions
            {
                Provider = "Local",
                LocalMasterKeyBase64 = Convert.ToBase64String(new byte[16]),
            });

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void DefaultProvider_ValidatesMasterKey()
    {
        // Provider default är "Local" — en utelämnad Provider validerar master-nyckeln.
        var result = Validator().Validate(null,
            new FieldEncryptionOptions { LocalMasterKeyBase64 = ValidMasterKeyBase64() });

        result.Succeeded.ShouldBeTrue();
    }
}
