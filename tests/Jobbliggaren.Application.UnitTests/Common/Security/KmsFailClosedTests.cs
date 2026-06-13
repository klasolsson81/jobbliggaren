using Amazon.KeyManagementService;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Security;

/// <summary>
/// TD-13 FAS 3.5 batch C1 — svit D (architect §5).
/// Verifierar fail-closed-kontraktet för KmsDataKeyProvider (IDataKeyProvider):
/// vid KMS-fel kastar provider:n och returnerar ALDRIG en default/tom/klartext-DEK
/// (ADR 0049 Beslut 4 + CTO-domen 2026-05-18 — ingen klartext-fallback).
/// §5.4: nyckelmaterial får aldrig hamna i logg.
///
/// IAmazonKeyManagementService är ALLTID NSubstitute-fake — ingen riktig AWS.
/// TDD-ordning (CLAUDE.md §2.4/§7): RÖDA tills C1-impl finns.
/// </summary>
public class KmsFailClosedTests
{
    // Fångar alla loggrader så scenario 9 kan asserta att inget
    // nyckelmaterial (plaintext-DEK / wrapped-bytes) läcker.
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception) + " " + (exception?.ToString() ?? string.Empty));
    }

    private readonly IAmazonKeyManagementService _kms =
        Substitute.For<IAmazonKeyManagementService>();
    private readonly RecordingLogger<KmsDataKeyProvider> _logger = new();
    private readonly KmsDataKeyProvider _sut;
    private readonly JobSeekerId _owner = JobSeekerId.New();

    public KmsFailClosedTests()
    {
        // C1-impl-kontrakt: KmsDataKeyProvider(IAmazonKeyManagementService, ILogger<KmsDataKeyProvider>).
        // CMK-ARN antas bindas via IOptions i produktionskoden (ej testat här —
        // Secrets-Manager-precedensen, ADR 0049 Kontext).
        _sut = new KmsDataKeyProvider(_kms, _logger);
    }

    [Fact]
    public async Task UnwrapDataKey_KmsDecryptThrows_PropagatesNoPlaintextFallback()
    {
        // KMS otillgängligt vid unwrap (t.ex. AccessDenied / nätverksfel).
        _kms.DecryptAsync(Arg.Any<Amazon.KeyManagementService.Model.DecryptRequest>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonKeyManagementServiceException("KMS Decrypt nere"));

        var wrappedDek = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        byte[]? leaked = null;
        Exception? caught = null;
        try
        {
            leaked = await _sut.UnwrapDataKeyAsync(_owner, wrappedDek, CancellationToken.None);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // Fail-closed: måste kasta, ALDRIG returnera en DEK (default/tom/klartext).
        caught.ShouldNotBeNull();
        leaked.ShouldBeNull();
    }

    [Fact]
    public async Task CreateDataKey_KmsUnavailable_ThrowsBeforeReturningKey()
    {
        _kms.GenerateDataKeyAsync(
                Arg.Any<Amazon.KeyManagementService.Model.GenerateDataKeyRequest>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonKeyManagementServiceException("KMS GenerateDataKey nere"));

        GeneratedDataKey result = default;
        Exception? caught = null;
        try
        {
            result = await _sut.CreateDataKeyAsync(_owner, CancellationToken.None);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // Ingen halvfärdig GeneratedDataKey får returneras vid KMS-fel.
        caught.ShouldNotBeNull();
        result.PlaintextDek.ShouldBeNull();
        result.WrappedDek.ShouldBeNull();
        result.CmkKeyId.ShouldBeNull();
    }

    [Fact]
    public async Task UnwrapDataKey_NeverLogsPlaintextDekOrWrappedBytes()
    {
        // Distinkta byte-mönster så vi kan söka exakt efter dem i loggen.
        var wrappedDek = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x10, 0x20, 0x30, 0x40 };

        _kms.DecryptAsync(Arg.Any<Amazon.KeyManagementService.Model.DecryptRequest>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonKeyManagementServiceException("KMS Decrypt nere"));

        try
        {
            await _sut.UnwrapDataKeyAsync(_owner, wrappedDek, CancellationToken.None);
        }
        catch
        {
            // Förväntat — fail-closed. Vi verifierar loggen, inte exception:n här.
        }

        // §5.4 — varken wrapped-bytes (base64 eller hex) eller någon
        // plaintext-DEK-representation får finnas i någon loggrad.
        var wrappedBase64 = Convert.ToBase64String(wrappedDek);
        var wrappedHex = Convert.ToHexString(wrappedDek);

        foreach (var line in _logger.Messages)
        {
            line.ShouldNotContain(wrappedBase64);
            line.ShouldNotContain(wrappedHex);
            line.ShouldNotContain(wrappedHex.ToLowerInvariant());
            // Råa byte-sekvensen som CSV (en naiv .ToString()-läcka).
            line.ShouldNotContain(string.Join(",", wrappedDek));
        }
    }
}
