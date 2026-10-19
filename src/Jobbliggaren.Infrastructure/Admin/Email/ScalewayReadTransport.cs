using System.Net;
using System.Text;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>
/// The one GET every Scaleway Transactional Email read goes through (#1981, ADR 0178), authenticated with the read
/// application's key (<see cref="ScalewayReadOptions"/>). It holds no reference to the sending key.
/// <para>
/// <b>What it never does.</b> It never reads the body of a refusal: an error payload is where a provider echoes what
/// it was sent. It logs which read failed, the reason and the status, never the URI (which carries the project id),
/// the body or an exception message. The read client has no loggers at all
/// (<see cref="ScalewayClientRegistration.AddScalewayUsageClient"/>).
/// </para>
/// </summary>
internal sealed partial class ScalewayReadTransport(
    IHttpClientFactory httpClientFactory,
    IOptions<ScalewayReadOptions> readOptions,
    IDateTimeProvider clock,
    ILogger<ScalewayReadTransport> logger)
{
    private const string AuthHeaderName = "X-Auth-Token";

    /// <summary>The status logged when there was no response at all; zero is not a status, which is the point.</summary>
    private const int NoHttpStatus = 0;

    private readonly ScalewayReadOptions _read = readOptions.Value;

    internal async ValueTask<ProviderAttempt<TRead>> GetAsync<TRead>(
        string path,
        IReadOnlyList<KeyValuePair<string, string>> query,
        int maxBodyBytes,
        Func<byte[], TRead?> parse,
        Func<EmailProviderReadReason, TRead> failed,
        CancellationToken cancellationToken)
        where TRead : class
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, RequestUri(path, query));
            request.Headers.TryAddWithoutValidation(AuthHeaderName, _read.SecretKey);

            var client = httpClientFactory.CreateClient(ScalewayClientRegistration.UsageHttpClientName);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(client.Timeout);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);

            if (!response.IsSuccessStatusCode)
            {
                var reason = ReasonFor(response.StatusCode);
                LogReadFailed(path, reason, (int)response.StatusCode);
                return new ProviderAttempt<TRead>(failed(reason), RetryAfterOf(response));
            }

            var body = await ReadBoundedAsync(response.Content, maxBodyBytes, deadline.Token);
            if (body is not null && parse(body) is { } read)
            {
                return new ProviderAttempt<TRead>(read);
            }

            LogReadFailed(path, EmailProviderReadReason.Malformed, (int)response.StatusCode);
            return new ProviderAttempt<TRead>(failed(EmailProviderReadReason.Malformed));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient raises its own timeout as a cancellation the caller did not ask for.
            LogReadFailed(path, EmailProviderReadReason.Timeout, NoHttpStatus);
            return new ProviderAttempt<TRead>(failed(EmailProviderReadReason.Timeout));
        }
        catch (HttpRequestException ex)
        {
            var reason = ReasonFor(ex.HttpRequestError);
            LogReadFailed(path, reason, NoHttpStatus);
            return new ProviderAttempt<TRead>(failed(reason));
        }
        catch (IOException ex)
        {
            var reason = ex is HttpIOException http ? ReasonFor(http.HttpRequestError) : EmailProviderReadReason.ProviderError;
            LogReadFailed(path, reason, NoHttpStatus);
            return new ProviderAttempt<TRead>(failed(reason));
        }
    }

    private string RequestUri(string path, IReadOnlyList<KeyValuePair<string, string>> query)
    {
        var uri = new StringBuilder(path)
            .Append("?project_id=")
            .Append(Uri.EscapeDataString(_read.ProjectId));
        foreach (var (name, value) in query)
        {
            uri.Append('&').Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
        }

        return uri.ToString();
    }

    private static EmailProviderReadReason ReasonFor(HttpRequestError error) => error is HttpRequestError.NameResolutionError
        or HttpRequestError.ConnectionError
        or HttpRequestError.SecureConnectionError
        or HttpRequestError.ProxyTunnelError
        ? EmailProviderReadReason.Unreachable
        : EmailProviderReadReason.ProviderError;

    private static EmailProviderReadReason ReasonFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => EmailProviderReadReason.Unauthorized,
        HttpStatusCode.Forbidden => EmailProviderReadReason.Forbidden,
        HttpStatusCode.TooManyRequests => EmailProviderReadReason.RateLimited,
        _ => EmailProviderReadReason.ProviderError,
    };

    /// <summary>The provider's Retry-After as a delay, read both as seconds and as an HTTP date; null when absent or past.</summary>
    private TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - clock.UtcNow : null);
        return delay > TimeSpan.Zero ? delay : null;
    }

    /// <summary>The body, or null when it is larger than <paramref name="maxBodyBytes"/>, whatever Content-Length claims.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, int maxBodyBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maxBodyBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[maxBodyBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length
               && (read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)) > 0)
        {
            total += read;
        }

        return total > maxBodyBytes ? null : buffer[..total];
    }

    [LoggerMessage(3020, LogLevel.Warning,
        "[ScalewayReadTransport] {Read} read failed ({Reason}, HTTP {HttpStatus}) — no URI, body or message logged")]
    private partial void LogReadFailed(string read, EmailProviderReadReason reason, int httpStatus);
}
