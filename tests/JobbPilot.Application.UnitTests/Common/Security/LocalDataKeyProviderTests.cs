using System.Security.Cryptography;
using JobbPilot.Application.Common.Security;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Common.Security;

/// <summary>
/// ADR 0066 (AWS-avveckling) — LocalDataKeyProvider (IDataKeyProvider) som
/// wrappar per-användar-DEK:en med en lokal AES-256-GCM master-nyckel istället
/// för AWS KMS. Verifierar samma envelope-kontrakt som KmsDataKeyProvider:
/// 32-byte DEK, owner-AAD-binding (cross-owner-unwrap hindras), fail-closed
/// auth-tag/format-fel (ADR 0049 Beslut 4, CTO-domen 2026-05-18 — ingen
/// klartext-fallback) och crypto-agility-prefix [0x4C, 0x01].
///
/// Inga AWS-beroenden — master-nyckeln injiceras via IOptions. §5.4:
/// nyckelmaterial (master-nyckel/DEK/wrapped-bytes) får aldrig hamna i
/// exception-message.
/// </summary>
public class LocalDataKeyProviderTests
{
    // Fångar alla loggrader så vi kan asserta att inget nyckelmaterial läcker.
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception) + " " + (exception?.ToString() ?? string.Empty));
    }

    // Giltig 32-byte master-nyckel (AES-256), slumpgenererad per test-instans.
    private readonly byte[] _masterKeyBytes = RandomNumberGenerator.GetBytes(32);
    private readonly string _masterKeyBase64;
    private readonly LocalDataKeyProvider _sut;
    private readonly JobSeekerId _owner = JobSeekerId.New();

    public LocalDataKeyProviderTests()
    {
        _masterKeyBase64 = Convert.ToBase64String(_masterKeyBytes);
        _sut = NewProvider(_masterKeyBase64);
    }

    private static LocalDataKeyProvider NewProvider(string masterKeyBase64) =>
        new(
            Options.Create(new FieldEncryptionOptions
            {
                Provider = "Local",
                LocalMasterKeyBase64 = masterKeyBase64,
            }),
            Substitute.For<ILogger<LocalDataKeyProvider>>());

    [Fact]
    public async Task LocalDataKeyProvider_CreateThenUnwrapSameOwner_ReturnsExactSameDek()
    {
        var created = await _sut.CreateDataKeyAsync(_owner, CancellationToken.None);

        var unwrapped = await _sut.UnwrapDataKeyAsync(
            _owner, created.WrappedDek, CancellationToken.None);

        // Byte-för-byte-identisk round-trip (envelope-kontraktet).
        unwrapped.ShouldBe(created.PlaintextDek);
    }

    [Fact]
    public async Task LocalDataKeyProvider_CreateDataKey_ProducesAes256Dek()
    {
        var created = await _sut.CreateDataKeyAsync(_owner, CancellationToken.None);

        // 32 byte = AES-256.
        created.PlaintextDek.Length.ShouldBe(32);
    }

    [Fact]
    public async Task LocalDataKeyProvider_CreateDataKey_SetsLocalCmkKeyId()
    {
        var created = await _sut.CreateDataKeyAsync(_owner, CancellationToken.None);

        created.CmkKeyId.ShouldBe("local-v1");
    }

    [Fact]
    public async Task LocalDataKeyProvider_CreateDataKey_WrappedDekCarriesV1Prefix()
    {
        var created = await _sut.CreateDataKeyAsync(_owner, CancellationToken.None);

        // [0x4C ('L' = Local), 0x01 (wrap-format-version 1)].
        created.WrappedDek[0].ShouldBe((byte)0x4C);
        created.WrappedDek[1].ShouldBe((byte)0x01);
    }

    [Fact]
    public async Task LocalDataKeyProvider_UnwrapWithDifferentOwner_Throws()
    {
        // Säkerhetsinvariant: AAD binder wrapen till owner A. Owner B får inte
        // unwrappa — även om en user_data_keys-rad kopieras mellan ägare.
        var ownerA = JobSeekerId.New();
        var ownerB = JobSeekerId.New();

        var created = await _sut.CreateDataKeyAsync(ownerA, CancellationToken.None);

        await Should.ThrowAsync<CryptographicException>(
            () => _sut.UnwrapDataKeyAsync(ownerB, created.WrappedDek, CancellationToken.None));
    }

    [Fact]
    public async Task LocalDataKeyProvider_UnwrapTamperedWrappedDek_Throws()
    {
        var created = await _sut.CreateDataKeyAsync(_owner, CancellationToken.None);
        var tampered = (byte[])created.WrappedDek.Clone();

        // Flippa sista byten (ligger i auth-tag(16)) → GCM måste underkänna.
        tampered[^1] ^= 0xFF;

        await Should.ThrowAsync<CryptographicException>(
            () => _sut.UnwrapDataKeyAsync(_owner, tampered, CancellationToken.None));
    }

    [Fact]
    public async Task LocalDataKeyProvider_UnwrapWithUnknownMagicPrefix_Throws()
    {
        var created = await _sut.CreateDataKeyAsync(_owner, CancellationToken.None);
        var wrongMagic = (byte[])created.WrappedDek.Clone();

        // Fel magic/version (0x00,0x00) → crypto-agility-guarden ska faila
        // tydligt, inte som auth-tag-mismatch.
        wrongMagic[0] = 0x00;
        wrongMagic[1] = 0x00;

        await Should.ThrowAsync<CryptographicException>(
            () => _sut.UnwrapDataKeyAsync(_owner, wrongMagic, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]   // tom array
    [InlineData(2)]   // bara prefix, inget nonce/ciphertext/tag
    [InlineData(29)]  // < 2 + 12 (nonce) + 16 (tag) = 30 byte (utan ciphertext)
    public async Task LocalDataKeyProvider_UnwrapTooShortWrappedDek_Throws(int length)
    {
        var tooShort = new byte[length];
        if (length >= 2)
        {
            // Giltigt prefix så vi testar längd-guarden, inte magic-guarden.
            tooShort[0] = 0x4C;
            tooShort[1] = 0x01;
        }

        await Should.ThrowAsync<CryptographicException>(
            () => _sut.UnwrapDataKeyAsync(_owner, tooShort, CancellationToken.None));
    }

    [Fact]
    public async Task LocalDataKeyProvider_CreateTwiceSameOwner_ProducesDistinctWrappedDeksThatBothUnwrap()
    {
        var first = await _sut.CreateDataKeyAsync(_owner, CancellationToken.None);
        var second = await _sut.CreateDataKeyAsync(_owner, CancellationToken.None);

        // Slumpnonce per wrap → wrapped-DEK får aldrig vara deterministisk.
        first.WrappedDek.ShouldNotBe(second.WrappedDek);

        // Båda unwrappar korrekt till sina respektive (olika) DEK:er.
        var unwrappedFirst = await _sut.UnwrapDataKeyAsync(
            _owner, first.WrappedDek, CancellationToken.None);
        var unwrappedSecond = await _sut.UnwrapDataKeyAsync(
            _owner, second.WrappedDek, CancellationToken.None);

        unwrappedFirst.ShouldBe(first.PlaintextDek);
        unwrappedSecond.ShouldBe(second.PlaintextDek);
        unwrappedFirst.ShouldNotBe(unwrappedSecond);
    }

    [Fact]
    public void LocalDataKeyProvider_CtorWithEmptyMasterKey_Throws()
    {
        Should.Throw<CryptographicException>(() => NewProvider(string.Empty));
    }

    [Fact]
    public void LocalDataKeyProvider_CtorWithInvalidBase64_Throws()
    {
        // '!' är inte giltig base64-alfabet.
        Should.Throw<CryptographicException>(() => NewProvider("inte!giltig!base64!"));
    }

    [Fact]
    public void LocalDataKeyProvider_CtorWithWrongLengthMasterKey_Throws()
    {
        // 16 byte = AES-128 — för svag, måste fail-closed redan i ctor.
        var sixteenBytes = Convert.ToBase64String(new byte[16]);

        Should.Throw<CryptographicException>(() => NewProvider(sixteenBytes));
    }

    [Fact]
    public void LocalDataKeyProvider_CtorWithWrongLengthMasterKey_DoesNotLeakKeyInMessage()
    {
        var sixteenBytes = Convert.ToBase64String(new byte[16]);

        var ex = Should.Throw<CryptographicException>(() => NewProvider(sixteenBytes));

        // §5.4 — varken base64-nyckeln eller råa nyckel-bytes får finnas i message.
        ex.Message.ShouldNotContain(sixteenBytes);
    }

    [Fact]
    public async Task LocalDataKeyProvider_UnwrapWithWrongOwner_DoesNotLeakDekOrKeyInMessage()
    {
        var created = await _sut.CreateDataKeyAsync(_owner, CancellationToken.None);
        var wrongOwner = JobSeekerId.New();

        var ex = await Should.ThrowAsync<CryptographicException>(
            () => _sut.UnwrapDataKeyAsync(wrongOwner, created.WrappedDek, CancellationToken.None));

        // §5.4 — varken master-nyckel, DEK eller wrapped-bytes (base64/hex/CSV)
        // får läcka i exception-message.
        ex.Message.ShouldNotContain(_masterKeyBase64);
        ex.Message.ShouldNotContain(Convert.ToBase64String(created.PlaintextDek));
        ex.Message.ShouldNotContain(Convert.ToHexString(created.PlaintextDek));
        ex.Message.ShouldNotContain(Convert.ToBase64String(created.WrappedDek));
    }
}
