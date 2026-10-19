using System.Net;
using System.Text.Json;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>
/// Reads Scaleway Transactional Email for the allowance figure (#1981, ADR 0178): <c>project-consumption</c> for
/// <c>monthly_emails_count</c> ("emails sent during the current month in the project") and <c>webhooks_count</c>, and
/// <c>offer-subscriptions</c> for the plan's <c>offer_name</c>. Both need only <c>offer_subscription:read</c>
/// (measured 2026-10-19, ADR 0178).
/// <para>
/// <b>What it never does.</b> It never reads the body of a refusal: an error payload is where a provider echoes what
/// it was sent. It reads a 2xx body only up to <see cref="MaxBodyBytes"/>, and from it only the named fields:
/// <c>project_id</c>, <c>organization_id</c> and every other field are skipped by the reader and never become a value.
/// It logs which read failed, the reason and the status, never the URI (which carries the project id), the body or an
/// exception message. The read client has no loggers at all (<see cref="ScalewayClientRegistration.AddScalewayUsageClient"/>).
/// </para>
/// </summary>
internal sealed partial class ScalewayUsageSource(
    IHttpClientFactory httpClientFactory,
    IOptions<ScalewayEmailOptions> scalewayOptions,
    IDateTimeProvider clock,
    ILogger<ScalewayUsageSource> logger) : IProviderUsageAttemptSource
{
    internal const int MaxBodyBytes = 64 * 1024;

    private const string AuthHeaderName = "X-Auth-Token";
    private const string ConsumptionPath = "project-consumption";
    private const string OfferSubscriptionsPath = "offer-subscriptions";
    private const string MonthlyEmailsCountField = "monthly_emails_count";
    private const string WebhooksCountField = "webhooks_count";
    private const string OfferSubscriptionsField = "offer_subscriptions";
    private const string OfferNameField = "offer_name";
    private const string EssentialOfferName = "essential";
    private const string ScaleOfferName = "scale";

    /// <summary>The status logged when there was no response at all; zero is not a status, which is the point.</summary>
    private const int NoHttpStatus = 0;

    private readonly ScalewayEmailOptions _scaleway = scalewayOptions.Value;

    public ValueTask<ProviderAttempt<EmailProviderUsageRead>> AttemptUsageAsync(CancellationToken cancellationToken) =>
        AttemptAsync<EmailProviderUsageRead>(
            ConsumptionPath,
            body => TryReadCounts(body, out var emails, out var webhooks)
                ? new EmailProviderUsageRead.Observed(emails, webhooks, clock.UtcNow)
                : null,
            reason => new EmailProviderUsageRead.Failed(reason),
            cancellationToken);

    public ValueTask<ProviderAttempt<EmailProviderPlanRead>> AttemptPlanAsync(CancellationToken cancellationToken) =>
        AttemptAsync<EmailProviderPlanRead>(
            OfferSubscriptionsPath,
            body => TryReadPlan(body, out var plan) ? new EmailProviderPlanRead.Observed(plan, clock.UtcNow) : null,
            reason => new EmailProviderPlanRead.Failed(reason),
            cancellationToken);

    private async ValueTask<ProviderAttempt<TRead>> AttemptAsync<TRead>(
        string path,
        Func<byte[], TRead?> parse,
        Func<EmailAllowanceReason, TRead> failed,
        CancellationToken cancellationToken)
        where TRead : class
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"{path}?project_id={Uri.EscapeDataString(_scaleway.ProjectId)}");
            request.Headers.TryAddWithoutValidation(AuthHeaderName, _scaleway.SecretKey);

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

            var body = await ReadBoundedAsync(response.Content, deadline.Token);
            if (body is not null && parse(body) is { } read)
            {
                return new ProviderAttempt<TRead>(read);
            }

            LogReadFailed(path, EmailAllowanceReason.Malformed, (int)response.StatusCode);
            return new ProviderAttempt<TRead>(failed(EmailAllowanceReason.Malformed));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient raises its own timeout as a cancellation the caller did not ask for.
            LogReadFailed(path, EmailAllowanceReason.Timeout, NoHttpStatus);
            return new ProviderAttempt<TRead>(failed(EmailAllowanceReason.Timeout));
        }
        catch (HttpRequestException ex)
        {
            var reason = ReasonFor(ex.HttpRequestError);
            LogReadFailed(path, reason, NoHttpStatus);
            return new ProviderAttempt<TRead>(failed(reason));
        }
        catch (IOException ex)
        {
            var reason = ex is HttpIOException http ? ReasonFor(http.HttpRequestError) : EmailAllowanceReason.ProviderError;
            LogReadFailed(path, reason, NoHttpStatus);
            return new ProviderAttempt<TRead>(failed(reason));
        }
    }

    private static EmailAllowanceReason ReasonFor(HttpRequestError error) => error is HttpRequestError.NameResolutionError
        or HttpRequestError.ConnectionError
        or HttpRequestError.SecureConnectionError
        or HttpRequestError.ProxyTunnelError
        ? EmailAllowanceReason.Unreachable
        : EmailAllowanceReason.ProviderError;

    private static EmailAllowanceReason ReasonFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => EmailAllowanceReason.Unauthorized,
        HttpStatusCode.Forbidden => EmailAllowanceReason.Forbidden,
        HttpStatusCode.TooManyRequests => EmailAllowanceReason.RateLimited,
        _ => EmailAllowanceReason.ProviderError,
    };

    /// <summary>The provider's Retry-After as a delay, read both as seconds and as an HTTP date; null when absent or past.</summary>
    private TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - clock.UtcNow : null);
        return delay > TimeSpan.Zero ? delay : null;
    }

    /// <summary>The body, or null when it is larger than <see cref="MaxBodyBytes"/>, whatever Content-Length claims.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaxBodyBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length
               && (read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)) > 0)
        {
            total += read;
        }

        return total > MaxBodyBytes ? null : buffer[..total];
    }

    /// <summary>
    /// Reads the two counts from the top-level object and skips every other value without materialising it. A count
    /// that is absent, negative or not an integer is unreadable; the webhook count alone may be unreadable.
    /// </summary>
    private static bool TryReadCounts(byte[] body, out long monthlyEmails, out int? webhooks)
    {
        monthlyEmails = 0;
        webhooks = null;
        long? emails = null;

        try
        {
            var reader = new Utf8JsonReader(body);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isEmails = reader.ValueTextEquals(MonthlyEmailsCountField);
                var isWebhooks = !isEmails && reader.ValueTextEquals(WebhooksCountField);
                reader.Read();

                if (isEmails)
                {
                    emails = reader.TokenType == JsonTokenType.Number && reader.TryGetUInt32(out var value)
                        ? value
                        : null;
                }
                else if (isWebhooks)
                {
                    webhooks = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value) && value >= 0
                        ? value
                        : null;
                }

                // A no-op on a number; on an object or array it moves past the whole value, so nothing nested is
                // ever read as a top-level field.
                reader.Skip();
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }

        if (emails is not { } count)
        {
            return false;
        }

        monthlyEmails = count;
        return true;
    }

    /// <summary>
    /// Reads <c>offer_subscriptions[].offer_name</c> and nothing else. The plan is the one subscription's name mapped onto
    /// <see cref="EmailPlan"/>; a name this code does not know, or anything but exactly one subscription, is
    /// <see cref="EmailPlan.Unknown"/>. A body without the array is unreadable.
    /// </summary>
    private static bool TryReadPlan(byte[] body, out EmailPlan plan)
    {
        plan = EmailPlan.Unknown;
        var subscriptions = -1;
        var onlyPlan = EmailPlan.Unknown;

        try
        {
            var reader = new Utf8JsonReader(body);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isSubscriptions = reader.ValueTextEquals(OfferSubscriptionsField);
                reader.Read();
                if (!isSubscriptions)
                {
                    reader.Skip();
                    continue;
                }

                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    return false;
                }

                subscriptions = 0;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    subscriptions++;
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        reader.Skip();
                        continue;
                    }

                    var named = EmailPlan.Unknown;
                    while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var isName = reader.ValueTextEquals(OfferNameField);
                        reader.Read();
                        if (isName && reader.TokenType == JsonTokenType.String)
                        {
                            named = reader.ValueTextEquals(EssentialOfferName) ? EmailPlan.Essential
                                : reader.ValueTextEquals(ScaleOfferName) ? EmailPlan.Scale
                                : EmailPlan.Unknown;
                        }

                        reader.Skip();
                    }

                    onlyPlan = named;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }

        if (subscriptions < 0)
        {
            return false;
        }

        plan = subscriptions == 1 ? onlyPlan : EmailPlan.Unknown;
        return true;
    }

    [LoggerMessage(3020, LogLevel.Warning,
        "[ScalewayUsageSource] {Read} read failed ({Reason}, HTTP {HttpStatus}) — no URI, body or message logged")]
    private partial void LogReadFailed(string read, EmailAllowanceReason reason, int httpStatus);
}
