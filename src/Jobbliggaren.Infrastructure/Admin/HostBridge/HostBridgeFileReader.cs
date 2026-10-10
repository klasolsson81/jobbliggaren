using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32.SafeHandles;

namespace Jobbliggaren.Infrastructure.Admin.HostBridge;

/// <summary>
/// Reads one host-bridge file and checks its envelope (#1982, ADR 0157). Syntax only: it holds no clock,
/// judges no time against another and knows nothing about what a source's <c>data</c> means. Every rule is a
/// refusal: the host's file is trusted only after it has been validated.
/// </summary>
internal sealed partial class HostBridgeFileReader(IOptions<HostBridgeOptions> options, ILogger<HostBridgeFileReader> logger)
{
    internal const int SchemaVersion = 1;
    internal const int MaxFileBytes = 16 * 1024;
    private const int MaxJsonDepth = 8;
    private const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>
    /// Reads <paramref name="fileName"/>, a compile-time constant of the source. The directory is never
    /// listed: the set of sources is closed, and each has one fixed name.
    /// </summary>
    public async ValueTask<HostBridgeFileRead> ReadAsync(string fileName, string expectedSource, CancellationToken cancellationToken)
    {
        var directory = options.Value.Directory;
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
        {
            return Failed(HostBridgeFailure.NotConfigured, fileName);
        }

        var path = Path.Combine(directory, fileName);
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null)
            {
                return Failed(HostBridgeFailure.NotARegularFile, fileName);
            }

            if (!info.Exists)
            {
                return Failed(System.IO.Directory.Exists(path) ? HostBridgeFailure.NotARegularFile : HostBridgeFailure.NotSampledYet, fileName);
            }

            using var handle = File.OpenHandle(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous);
            var bytes = await ReadBoundedAsync(handle, cancellationToken);
            return bytes is null ? Failed(HostBridgeFailure.TooLarge, fileName) : Parse(bytes.Value, expectedSource, fileName);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Failed(HostBridgeFailure.NotSampledYet, fileName);
        }
        catch (NotSupportedException)
        {
            return Failed(HostBridgeFailure.NotARegularFile, fileName);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            LogReadFailed(logger, fileName, exception.GetType().Name);
            return Failed(HostBridgeFailure.Unreadable, fileName);
        }
    }

    // Reads at most the cap plus one byte from the handle, so a file that grew after anything looked at its
    // size is still refused. Null means over the cap.
    private static async ValueTask<ReadOnlyMemory<byte>?> ReadBoundedAsync(SafeFileHandle handle, CancellationToken cancellationToken)
    {
        if (RandomAccess.GetLength(handle) > MaxFileBytes)
        {
            return null;
        }

        var buffer = new byte[MaxFileBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await RandomAccess.ReadAsync(handle, buffer.AsMemory(total), total, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total > MaxFileBytes ? null : buffer.AsMemory(0, total);
    }

    private HostBridgeFileRead Parse(ReadOnlyMemory<byte> bytes, string expectedSource, string fileName)
    {
        // JsonDocument decodes names and strings lazily, so invalid UTF-8 would first surface as an
        // InvalidOperationException from deep inside a property access. Refused here, whole, instead.
        if (!Utf8.IsValid(bytes.Span))
        {
            return Failed(HostBridgeFailure.InvalidFormat, fileName);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                MaxDepth = MaxJsonDepth,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
        }
        catch (JsonException)
        {
            // Not even the exception message is logged: System.Text.Json quotes the offending character.
            return Failed(HostBridgeFailure.InvalidFormat, fileName);
        }

        var envelope = TryReadEnvelope(document, expectedSource);
        if (envelope is null)
        {
            document.Dispose();
            return Failed(HostBridgeFailure.InvalidFormat, fileName);
        }

        return new HostBridgeFileRead(null, envelope);
    }

    private static HostBridgeEnvelope? TryReadEnvelope(JsonDocument document, string expectedSource)
    {
        if (!TryProperties(document.RootElement, out var props))
        {
            return null;
        }

        var hasData = props.TryGetValue("data", out var data);
        var hasError = props.TryGetValue("error", out var error);
        if (hasData == hasError || props.Count != 4)
        {
            return null;
        }

        if (!props.TryGetValue("schema", out var schema)
            || schema.ValueKind != JsonValueKind.Number
            || schema.GetRawText() != SchemaVersion.ToString(CultureInfo.InvariantCulture))
        {
            return null;
        }

        if (!props.TryGetValue("source", out var source)
            || source.ValueKind != JsonValueKind.String
            || !string.Equals(source.GetString(), expectedSource, StringComparison.Ordinal))
        {
            return null;
        }

        if (!props.TryGetValue("sampledAt", out var sampled) || !TryReadInstant(sampled, out var sampledAt))
        {
            return null;
        }

        if (hasError)
        {
            return error.ValueKind == JsonValueKind.String
                ? new HostBridgeEnvelope(document, sampledAt, error.GetString(), default)
                : null;
        }

        return new HostBridgeEnvelope(document, sampledAt, null, data);
    }

    /// <summary>The object's properties by name, or false: not an object, or a name repeated.</summary>
    internal static bool TryProperties(JsonElement element, [NotNullWhen(true)] out Dictionary<string, JsonElement>? properties)
    {
        properties = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var found = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!found.TryAdd(property.Name, property.Value))
            {
                return false;
            }
        }

        properties = found;
        return true;
    }

    /// <summary>Exactly <c>yyyy-MM-ddTHH:mm:ssZ</c>: no decimals, no offset other than Z, no whitespace.</summary>
    internal static bool TryReadInstant(JsonElement element, out DateTimeOffset instant)
    {
        instant = default;
        return element.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParseExact(
                element.GetString(),
                InstantFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out instant);
    }

    private HostBridgeFileRead Failed(HostBridgeFailure failure, string fileName)
    {
        // An absent file is the expected state until the sampler is enabled, and an unreadable one has
        // already logged its exception type; neither is logged again here.
        if (failure is not (HostBridgeFailure.NotConfigured or HostBridgeFailure.NotSampledYet or HostBridgeFailure.Unreadable))
        {
            LogRefused(logger, fileName, failure.ToString());
        }

        return new HostBridgeFileRead(failure, null);
    }

    [LoggerMessage(5101, LogLevel.Warning, "Host bridge file {File} could not be read ({ErrorType})")]
    private static partial void LogReadFailed(ILogger logger, string file, string errorType);

    [LoggerMessage(5102, LogLevel.Warning, "Host bridge file {File} was refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string file, string reason);
}

internal enum HostBridgeFailure
{
    NotConfigured,
    NotSampledYet,
    Unreadable,
    NotARegularFile,
    TooLarge,
    InvalidFormat,
}

/// <summary>Either a failure, or a validated envelope the caller must dispose (it owns the parsed document).</summary>
internal readonly record struct HostBridgeFileRead(HostBridgeFailure? Failure, HostBridgeEnvelope? Envelope);

internal sealed class HostBridgeEnvelope(JsonDocument document, DateTimeOffset sampledAt, string? errorToken, JsonElement data) : IDisposable
{
    public DateTimeOffset SampledAt { get; } = sampledAt;

    /// <summary>Set when the sampler reported that it could not establish the facts; <see cref="Data"/> is then empty.</summary>
    public string? ErrorToken { get; } = errorToken;

    public JsonElement Data { get; } = data;

    public void Dispose() => document.Dispose();
}
