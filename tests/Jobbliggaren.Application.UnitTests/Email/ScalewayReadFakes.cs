using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Admin.Email;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// The real provider-read composition (<see cref="EmailUsageSourceCollectionExtensions.AddEmailUsageSource"/> and the
/// real named client) under Scaleway with a read key of its own, with only the transport replaced and every log line
/// from every category captured. The sending key is configured and bound too, so a test can show it is never sent.
/// </summary>
internal static class ScalewayReadComposition
{
    internal const string Region = "fr-par";
    internal const string ProjectId = "11111111-2222-3333-4444-555555555555";
    internal const string SendKey = "SCW-SECRET-KEY-4711"; // gitleaks:allow
    internal const string ReadKey = "SCW-READ-KEY-4712"; // gitleaks:allow
    internal const string BaseAddress = $"https://api.scaleway.com/transactional-email/v1alpha1/regions/{Region}/";

    internal static ServiceProvider Build(
        ScriptedTransport transport, CapturingLogSink logs, IDateTimeProvider clock, TimeSpan? readTimeout = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Provider"] = "Scaleway",
                ["Email:Scaleway:Region"] = Region,
                ["Email:Scaleway:SecretKey"] = SendKey,
                ["Email:Scaleway:ProjectId"] = ProjectId,
                ["Email:ScalewayRead:SecretKey"] = ReadKey,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddSingleton(clock);
        services.AddOptions<ScalewayEmailOptions>().Bind(configuration.GetSection(ScalewayEmailOptions.SectionName));
        services.AddEmailUsageSource(configuration);
        var client = services.AddHttpClient(ScalewayClientRegistration.UsageHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => transport);
        if (readTimeout is { } timeout)
        {
            client.ConfigureHttpClient(c => c.Timeout = timeout);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>The query string as ordered (name, value) pairs, each unescaped.</summary>
    internal static IReadOnlyList<(string Name, string Value)> QueryOf(Uri? uri) =>
        [.. uri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair =>
        {
            var parts = pair.Split('=', 2);
            return (Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts.Length > 1 ? parts[1] : string.Empty));
        })];
}

internal sealed record CapturedRequest(HttpMethod Method, Uri? Uri, string? AuthToken);

internal sealed class ScriptedTransport : HttpMessageHandler
{
    private Func<HttpResponseMessage>? _respond;
    private Exception? _throw;

    public List<CapturedRequest> Requests { get; } = [];

    public void Respond(
        HttpStatusCode status,
        string body,
        RetryConditionHeaderValue? retryAfter = null,
        bool omitContentLength = false,
        long? contentLength = null)
    {
        _throw = null;
        _respond = () =>
        {
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            if (omitContentLength)
            {
                content.Headers.ContentLength = null;
            }
            else if (contentLength is { } length)
            {
                content.Headers.ContentLength = length;
            }

            var response = new HttpResponseMessage(status) { Content = content };
            response.Headers.RetryAfter = retryAfter;
            return response;
        };
    }

    public void RespondWithUnreadableBody(HttpStatusCode status)
    {
        _throw = null;
        _respond = () => new HttpResponseMessage(status) { Content = new UnreadableContent() };
    }

    public void RespondWith(Func<HttpContent> content)
    {
        _throw = null;
        _respond = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = content() };
    }

    public void Throw(Exception exception) => _throw = exception;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(new CapturedRequest(
            request.Method,
            request.RequestUri,
            request.Headers.TryGetValues("X-Auth-Token", out var values) ? values.Single() : null));
        return _throw is not null ? Task.FromException<HttpResponseMessage>(_throw) : Task.FromResult(_respond!());
    }
}

/// <summary>A body that fails the test if anything reads it.</summary>
internal sealed class UnreadableContent : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        throw new InvalidOperationException("a refusal's body must never be read");

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}

/// <summary>A body whose bytes never arrive.</summary>
internal sealed class StallingStream : ReadOnlyBody
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
}

/// <summary>A body that breaks on its first read.</summary>
internal sealed class FailingStream(Exception failure) : ReadOnlyBody
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<int>(failure);
}

internal abstract class ReadOnlyBody : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public abstract override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class CapturingLogSink : ILoggerProvider
{
    private readonly List<(string Category, LogLevel Level, int EventId, string Message)> _records = [];

    public IReadOnlyList<(string Category, LogLevel Level, int EventId, string Message)> Records
    {
        get
        {
            lock (_records)
                return [.. _records];
        }
    }

    public IEnumerable<string> Lines => Records.Select(r => $"{r.Category} {r.Message}");

    public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);

    public void Dispose() { }

    private void Add(string category, LogLevel level, int eventId, string message)
    {
        lock (_records)
            _records.Add((category, level, eventId, message));
    }

    private sealed class Sink(CapturingLogSink owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            owner.Add(category, logLevel, eventId.Id, formatter(state, exception) + " " + exception);
    }
}
