using System.Net;

namespace Jobbliggaren.Api.Configuration;

/// <summary>
/// Konfig-driven <c>UseForwardedHeaders</c>-uppsättning (TD-21 / STEG 12). Bind:as
/// från <c>appsettings.&lt;env&gt;.json</c>-sektionen <see cref="SectionName"/>. I
/// dev (default tom array) bevaras ASP.NET-default-beteendet (loopback only).
///
/// In production <see cref="KnownNetworks"/> is set to the CIDR of the network the
/// reverse proxy connects FROM, so <c>Connection.RemoteIpAddress</c> reflects the
/// client IP. In the Compose stack that is the Docker bridge subnet — never a public
/// address. The value is owed by #196, which builds that stack.
///
/// Setting it is necessary but NOT sufficient. This class only decides which proxies
/// are trusted to have set <c>X-Forwarded-For</c>; it cannot conjure a header nobody
/// sends. Measured 2026-08-04, under Option B no component sent one, so six policies
/// that partition on the client IP shared a single bucket — two of them only for
/// unauthenticated callers. Closed by #1202: Caddy writes the header toward web, and
/// Next relays it verbatim rather than appending, so exactly one entry reaches this
/// middleware and <see cref="ForwardLimit"/> stays at 1. A backend call made outside a
/// request scope — build, static render, background work — sends none and falls back to
/// the connection address, which for internal traffic is the right answer.
///
/// Parsing är fail-loud per security-auditor STEG 11 Sec-Major-1: tyst no-op:ad
/// rate-limiting i prod är värre än uppstart-throw. Ogiltig CIDR-string eller IP
/// → <see cref="InvalidOperationException"/> innan första request.
///
/// Direct-bound via <c>Configuration.GetSection().Get&lt;T&gt;()</c> i Program.cs
/// — inte injicerat som <c>IOptions&lt;T&gt;</c> eftersom värdena bara läses
/// vid pipeline-uppsättning.
/// </summary>
public sealed class ForwardedHeadersConfig
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// CIDR-strings (t.ex. "10.0.0.0/16") som motsvarar trusted proxy-nätverk.
    /// In production: the reverse proxy's network CIDR. Tom array = ASP.NET-default
    /// (loopback).
    /// </summary>
    public string[] KnownNetworks { get; init; } = [];

    /// <summary>
    /// Single-IP entries for individual proxies outside the trusted network. Rarely
    /// needed with a single reverse proxy in front.
    /// </summary>
    public string[] KnownProxies { get; init; } = [];

    /// <summary>
    /// Hur många proxy-hops som accepteras i X-Forwarded-For-kedjan. 1 for a single
    /// reverse proxy — which is what <c>appsettings.Production.json</c> ships; raise to
    /// 2 only if a CDN is placed in front of it. Värden &lt; 1 throwas.
    /// </summary>
    public int ForwardLimit { get; init; } = 1;

    /// <summary>
    /// Parsar <see cref="KnownNetworks"/> till <see cref="IPNetwork"/>. Fail-loud
    /// vid ogiltig CIDR. Resultatet konsumeras av <c>UseForwardedHeaders(...)</c>
    /// via <c>ForwardedHeadersOptions.KnownIPNetworks</c> i Program.cs.
    /// </summary>
    public IReadOnlyList<IPNetwork> ParseKnownNetworks()
    {
        var result = new List<IPNetwork>(KnownNetworks.Length);
        // for-loop (inte foreach) så KnownNetworks[i]-position kan inkluderas i fel-meddelandet.
        for (var i = 0; i < KnownNetworks.Length; i++)
        {
            var raw = KnownNetworks[i];
            if (!IPNetwork.TryParse(raw, out var network))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownNetworks[{i}] '{raw}' är inte ett giltigt CIDR " +
                    "(förväntat format: '10.0.0.0/16').");
            }
            result.Add(network);
        }
        return result;
    }

    /// <summary>
    /// Parsar <see cref="KnownProxies"/> till <see cref="IPAddress"/>. Fail-loud
    /// vid ogiltig IP-string.
    /// </summary>
    public IReadOnlyList<IPAddress> ParseKnownProxies()
    {
        var result = new List<IPAddress>(KnownProxies.Length);
        // for-loop (inte foreach) så KnownProxies[i]-position kan inkluderas i fel-meddelandet.
        for (var i = 0; i < KnownProxies.Length; i++)
        {
            var raw = KnownProxies[i];
            if (!IPAddress.TryParse(raw, out var ip))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownProxies[{i}] '{raw}' är inte en giltig IP-adress.");
            }
            result.Add(ip);
        }
        return result;
    }

    /// <summary>
    /// Validerar <see cref="ForwardLimit"/>. Range 1-10 (>10 indikerar konfig-misstag).
    /// </summary>
    public int ValidateForwardLimit()
    {
        if (ForwardLimit is < 1 or > 10)
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders:ForwardLimit must be 1-10, got {ForwardLimit}. " +
                "1 for a single reverse proxy, 2 when a CDN sits in front of it.");
        }
        return ForwardLimit;
    }

    /// <summary>
    /// Production-defense per allow-list (security-auditor STEG 12 Sec-Major-1).
    /// Symmetri med Worker <c>safeForAutoSchema</c>-mönstret. Tom <see cref="KnownNetworks"/>
    /// bakom proxy = alla klienter i EN bucket = sex policies som partitionerar på klient-IP
    /// (två av dem bara för oautentiserade anropare) blir en GLOBAL limiter — inte en
    /// frånvarande kontroll, utan en delad. OWASP A07-yta.
    /// Bara <c>Development</c> och <c>Test</c> får tom array; allt annat tvingas till
    /// explicit overlay via fail-loud uppstart-throw.
    /// </summary>
    public void EnsureSafeForEnvironment(string environmentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        var safeForEmpty =
            string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);

        if (!safeForEmpty && KnownNetworks.Length == 0)
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders:KnownNetworks must be set outside Development/Test " +
                $"(current environment: {environmentName}). An empty array behind a proxy " +
                "collapses every client into ONE bucket, which turns six policies that " +
                "partition on the client IP (two only for unauthenticated callers) into a " +
                "global limiter — one caller can exhaust the login " +
                "budget and deny authentication to everyone. " +
                "Set it to the CIDR of the network the reverse proxy connects FROM — in the " +
                "Compose stack that is the Docker bridge subnet, never the public address. " +
                "NECESSARY BUT NOT SUFFICIENT: setting this silences THIS check, it does not " +
                "make per-IP limiting work. That also requires an X-Forwarded-For to actually " +
                "arrive: Caddy writes one toward web, and Next relays it verbatim on every " +
                "request-scoped backend call (#1202). Calls made outside a request scope send " +
                "none, and fall back to the connection address by design. " +
                "See docs/decisions/0050-deployment-migration-aws-exit-hetzner.md, " +
                "Amendment 2026-08-04, gate M-5b point 3.");
        }
    }
}
