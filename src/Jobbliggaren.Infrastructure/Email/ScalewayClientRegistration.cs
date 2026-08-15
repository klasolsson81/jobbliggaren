using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// Registers and configures the HTTP client the Scaleway Transactional Email arm sends over
/// (#183), and owns the region allow-list.
/// <para>
/// <b>Why this file exists at all.</b> Two things need a home outside
/// <c>DependencyInjection.cs</c>, which is a §6.5 hotspot many sessions edit: the client's
/// construction and the region guard. Its predecessor (<c>SesClientRegistration</c>) existed for a
/// third reason that has now lapsed — keeping the composition root textually Amazon-free — because
/// there is no SDK left to confine. The file survives on the first two reasons alone, and the
/// architecture test it was shaped around is correspondingly stricter now, not looser
/// (<c>NoAmazonReferenceTests</c>).
/// </para>
/// </summary>
internal static class ScalewayClientRegistration
{
    /// <summary>
    /// The named <see cref="HttpClient"/> <see cref="ScalewayEmailSender"/> resolves per send.
    /// <para>
    /// <b>A NAMED client, not a typed one, and the choice is load-bearing in two directions.</b>
    /// <c>AddHttpClient&lt;TClient, TImplementation&gt;</c> registers the client TRANSIENT behind a
    /// factory lambda, which would leave <c>ServiceDescriptor.ImplementationType</c> null — and the
    /// gate suites assert on exactly that property, so a typed client fails every one of them in a
    /// way that reads like a DI bug rather than a lifetime change. The sender is therefore a plain
    /// <c>AddSingleton&lt;IEmailSender, ScalewayEmailSender&gt;</c> that takes
    /// <see cref="IHttpClientFactory"/> and calls <see cref="IHttpClientFactory.CreateClient(string)"/>
    /// per send. That is also the correct shape independently of the tests: a singleton that
    /// captured one <see cref="HttpClient"/> for the process lifetime would freeze
    /// <c>HttpMessageHandler</c> rotation, which is the precise reason the Resend arm's sender was
    /// Transient. Creating the client per send costs a handler-pool lookup, not a socket.
    /// </para>
    /// </summary>
    internal const string HttpClientName = "scaleway-transactional-email";

    private const string ApiHost = "https://api.scaleway.com";

    /// <summary>
    /// Bounds one send attempt. There is no retry anywhere on this path, so this is the only bound
    /// that exists: without it the <see cref="HttpClient"/> default of 100 s applies, and
    /// <c>DigestDispatchJob</c> would hold that long PER RECIPIENT against an unreachable provider.
    /// A transactional send that has not been accepted within this window is not going to be
    /// rescued by waiting longer; the reaper owns the recovery, not the transport.
    /// </summary>
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registers the named client with the region built into its base address, and refuses a region
    /// the arm does not allow BEFORE anything can boot.
    /// <para>
    /// <b>No resilience handler, and never add one.</b> <c>AddStandardResilienceHandler</c> attaches
    /// a retry strategy, and a retry of a POST whose outcome is unknown — a timeout AFTER the
    /// service accepted the message — is a duplicate delivery, not a recovery. Scaleway's send
    /// endpoint carries no idempotency parameter, exactly as SES v2 did not, so the reasoning behind
    /// the retired <c>MaxErrorRetry = 0</c> transfers to this arm unchanged: dedupe across calls is
    /// owned by the claim-then-send spine and by <c>ICooldownGate</c> (ADR 0103), one layer up, and
    /// the posture this codebase already ratified is <c>StrandedMatchReaperJob</c>'s "MarkFailed,
    /// never re-send". ACCEPTED COST: a 429 or a transient 5xx fails outright and costs one reaped
    /// notification. If that ever needs fixing, the fix is a bounded application-level retry through
    /// the claim-then-send spine, NEVER a transport-level retry here.
    /// </para>
    /// </summary>
    internal static IServiceCollection AddScalewayEmailClient(
        this IServiceCollection services,
        string region)
    {
        EnsureAllowedRegion(region);

        services.AddHttpClient(HttpClientName, client =>
        {
            // The TRAILING SLASH is not cosmetic: relative-URI resolution replaces the last segment
            // of a base address that lacks one, so without it "emails" would resolve against
            // …/regions/ and drop the region entirely. Pinned by a request-URI assertion in
            // ScalewayEmailSenderTests rather than left to review.
            client.BaseAddress = new Uri($"{ApiHost}/transactional-email/v1alpha1/regions/{region}/");
            client.Timeout = SendTimeout;
        });

        return services;
    }

    /// <summary>
    /// Refuses a region outside the allow-list, at REGISTRATION.
    /// <para>
    /// <b>The fail-open this closes is real, and it arrives by a different road than the SES arm's
    /// did.</b> There is no SDK here to synthesise an endpoint from a typo — but string
    /// interpolation synthesises one just as happily. <c>fr-pars</c> builds a perfectly well-formed
    /// URL that satisfies <see cref="Uri"/>, the <c>[Required]</c> attribute and every null check
    /// the arm has, and fails first as a 404 on the first real send, in production, after a deploy.
    /// That is the same defect class ADR 0124 repaired for <c>RegionEndpoint.GetBySystemName</c>,
    /// reached without an SDK.
    /// </para>
    /// <para>
    /// <b>An allow-list, because a prefix guard is the wrong FORM</b> — the same form ruling as the
    /// retired <c>EeaRegions</c>, though the reason it bites differs and saying otherwise would
    /// overstate it. Scaleway's regions today are <c>fr-par</c>, <c>nl-ams</c> and <c>pl-waw</c>,
    /// all three inside the EEA, so this list is not currently separating EEA from non-EEA the way
    /// the AWS one was: it separates SUPPORTED from unsupported, since Transactional Email runs in
    /// <c>fr-par</c> only (measured against Scaleway's API reference 2026-08-15). The form still
    /// has to be an allow-list, because it is the only form that survives Scaleway adding a region:
    /// a prefix or a looks-region-shaped guard would admit a new value silently, and "is Transactional
    /// Email available there" and "is that jurisdiction acceptable" are two questions, neither of
    /// which a prefix can answer. Adding <c>nl-ams</c> or <c>pl-waw</c> here is a one-line change on
    /// the day TEM reaches them; anything outside the EEA is a data-residency decision and belongs
    /// in an ADR, not in this literal.
    /// </para>
    /// </summary>
    private static void EnsureAllowedRegion(string region)
    {
        if (!AllowedRegions.Contains(region))
        {
            throw new InvalidOperationException(
                $"Email:Scaleway:Region='{region}' är inte en tillåten region. Tillåtna är: "
                + $"{string.Join(", ", AllowedRegions.Order())}. TVÅ separata skäl. (1) Regionen "
                + "interpoleras rakt in i endpoint-URL:en, så ett felstavat namn bygger en "
                + "välformad URL som passerar varje null-kontroll och [Required] i armen och faller "
                + "först som 404 vid första skarpa utskicket i drift. (2) Regionen avgör "
                + "jurisdiktionen för varje utgående PII-överföring, så ett värde utanför EES är "
                + "ett dataresidens-beslut och inte en konfigurationsdetalj.");
        }
    }

    /// <summary>
    /// Scaleway regions this arm may send from. <c>fr-par</c> is the only region Transactional
    /// Email runs in as of 2026-08-15; <c>nl-ams</c> and <c>pl-waw</c> are EEA and would be
    /// legitimate additions the day the service reaches them.
    /// </summary>
    private static readonly HashSet<string> AllowedRegions = new(StringComparer.Ordinal)
    {
        "fr-par",  // Paris, FR
    };
}
