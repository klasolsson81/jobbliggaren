using System.Linq;
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
            RegionEndpoint = ResolveKnownRegion(region),

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

    /// <summary>
    /// Resolves a region name that the SDK actually knows, and throws otherwise.
    /// <para>
    /// <b>`GetBySystemName` alone is fail-OPEN, measured 2026-08-08.</b> It does not throw on an
    /// unknown name: it SYNTHESISES an endpoint, so the typo `eu-nrth-1` returned
    /// <c>SystemName='eu-nrth-1' DisplayName='Unknown'</c> and passed every guard the arm had —
    /// the null check, the <c>[Required]</c> attribute, and the endpoint construction. Nothing
    /// failed until the first real send, in production, after a deploy.
    /// </para>
    /// <para>
    /// <b>And the obvious repair is ALSO fail-open, for a reason worth writing down.</b> Checking
    /// membership of <see cref="RegionEndpoint.EnumerableAllRegions"/> *after* calling
    /// <c>GetBySystemName</c> always succeeds, because that call REGISTERS the synthesised region
    /// into the enumeration as a side effect. Measured: the collection held 47 entries before
    /// <c>GetBySystemName("totally-bogus")</c> and 48 after, with the bogus name among them. The
    /// membership test is therefore only meaningful BEFORE the endpoint is ever resolved, which is
    /// why the order below is load-bearing rather than stylistic.
    /// </para>
    /// <para>
    /// <b>An unknown region is refused, and so is a known one outside the EEA</b>
    /// (security-auditor ruling 2026-08-08 — the call senior-cto-advisor delegated to her).
    /// <c>Email:Ses:Region</c> is the ONLY configuration string that decides the jurisdiction of
    /// every outgoing PII transfer, it is typed by a human at the flip, and <c>us-east-1</c> is the
    /// default in practically every AWS example. Refusing costs nothing: under this repo's own
    /// applied standard there is no legitimate non-EEA value, and if one is ever needed that is an
    /// ADR decision rather than a config edit.
    /// </para>
    /// </summary>
    private static RegionEndpoint ResolveKnownRegion(string region)
    {
        if (!EeaRegions.Contains(region))
        {
            throw new InvalidOperationException(
                $"Email:Ses:Region='{region}' är inte en tillåten region. Tillåtna är AWS "
                + $"kommersiella EES-regioner: {string.Join(", ", EeaRegions.Order())}. "
                + "TVÅ separata skäl, båda mätta 2026-08-08 (ADR 0124): (1) SDK:ns "
                + "RegionEndpoint.GetBySystemName KASTAR INTE på ett okänt namn — den syntetiserar "
                + "en endpoint med DisplayName='Unknown', så en felstavning som 'eu-nrth-1' hade "
                + "passerat DI och fallit först vid första utskicket i drift. (2) Regionen avgör "
                + "jurisdiktionen för varje utgående PII-överföring, så ett värde utanför EES är "
                + "ett dataresidens-beslut och inte en konfigurationsdetalj.");
        }

        return RegionEndpoint.GetBySystemName(region);
    }

    /// <summary>
    /// AWS commercial regions inside the EEA, by <c>SystemName</c>.
    /// <para>
    /// <b>An explicit list, because <c>StartsWith("eu-")</c> is the WRONG FORM</b> — and that is
    /// measured, not cautious. Enumerating the SDK's own `eu-`-prefixed regions on 2026-08-08 gives
    /// nine, and three of them do not belong here: <c>eu-west-2</c> is Europe (London), i.e. the UK,
    /// a third country with an adequacy decision; <c>eu-central-2</c> is Europe (Zurich), i.e.
    /// Switzerland, likewise; and <c>eu-isoe-west-1</c> is in the isolated <c>aws-iso-e</c>
    /// partition, not the commercial one. A prefix guard would have admitted two third countries
    /// while looking exactly as strict.
    /// </para>
    /// <para>
    /// Note the ordering constraint this list also satisfies for free: membership is tested against
    /// a literal set, never against <see cref="RegionEndpoint.EnumerableAllRegions"/> after a
    /// <c>GetBySystemName</c> call, which would be self-fulfilling (measured: the collection grows
    /// from 47 to 48 entries, with the bogus name among them, once that call has run).
    /// </para>
    /// </summary>
    private static readonly HashSet<string> EeaRegions = new(StringComparer.Ordinal)
    {
        "eu-north-1",    // Stockholm, SE
        "eu-central-1",  // Frankfurt, DE
        "eu-west-1",     // Ireland, IE
        "eu-west-3",     // Paris, FR
        "eu-south-1",    // Milan, IT
        "eu-south-2",    // Spain, ES
    };
}
