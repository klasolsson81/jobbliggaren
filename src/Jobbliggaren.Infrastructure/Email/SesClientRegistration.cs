using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// Constructs and registers the Amazon SES v2 client (ADR 0124, #1237).
/// <para>
/// <b>Why this file exists at all.</b> <c>NoAmazonReferenceTests</c> allow-lists Amazon imports
/// under <c>src/Jobbliggaren.Infrastructure/Email/</c> and nowhere else. Putting the client
/// construction here rather than inline in <c>DependencyInjection.cs</c> keeps that composition
/// root — a §6.5 hotspot many sessions edit — textually Amazon-free. The code is shaped to satisfy
/// the guard; the guard was not widened to admit the code.
/// </para>
/// </summary>
internal static class SesClientRegistration
{
    /// <summary>
    /// Registers <see cref="IAmazonSimpleEmailServiceV2"/> as a SINGLETON.
    /// <para>
    /// <b>Singleton, and the Resend arm's reasoning does NOT transfer</b> (senior-cto-advisor
    /// 2026-08-08). Resend's sender was <c>Transient</c> because <c>IResend</c> was an
    /// <c>IHttpClientFactory</c> TYPED CLIENT, so a singleton capturing it would freeze
    /// <c>HttpMessageHandler</c> rotation. The AWS SDK client is not a typed client: it is
    /// thread-safe and owns its own pooled handler, and AWS documents one long-lived client per
    /// service/region/credential pair. A transient would build a whole SDK client per email. Note
    /// also that <c>ConsoleEmailSender</c> and <c>NullEmailSender</c> are already singletons — the
    /// transient was the local deviation, not the norm.
    /// </para>
    /// </summary>
    internal static IServiceCollection AddSesClient(
        this IServiceCollection services,
        string region,
        string accessKeyId,
        string secretAccessKey)
    {
        var config = new AmazonSimpleEmailServiceV2Config
        {
            // Explicit ALWAYS. The parameterless config walks the SDK's default region chain, which
            // on a box without IMDS can also stall while it tries.
            RegionEndpoint = RegionEndpoint.GetBySystemName(region),

            // MaxErrorRetry = 0 (senior-cto-advisor bind 2, 2026-08-08). SES v2 SendEmail has NO
            // idempotency parameter — no ClientToken, no dedup (measured against the API reference
            // 2026-08-08) — so an SDK retry of a request whose outcome is unknown (a timeout AFTER
            // the service accepted it) is a duplicate delivery, not a recovery. Standard retry mode
            // defaults to 2 attempts for non-DynamoDB services, i.e. two possible re-POSTs of an
            // accepted send. Zero makes the transport agree with a posture this codebase already
            // ratified three times over: StrandedMatchReaperJob "MarkFailed, never re-send".
            // ACCEPTED COST: a 429/transient 5xx now fails outright and costs one reaped
            // notification. Negligible at MVP volume; RE-MEASURE at volume, and if it ever needs
            // fixing the fix is a bounded application-level retry through the claim-then-send spine,
            // NEVER raising this number.
            MaxErrorRetry = 0,
        };

        var credentials = new BasicAWSCredentials(accessKeyId, secretAccessKey);

        services.AddSingleton<IAmazonSimpleEmailServiceV2>(
            _ => new AmazonSimpleEmailServiceV2Client(credentials, config));

        return services;
    }
}
