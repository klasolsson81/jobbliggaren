using System.Net;

namespace Jobbliggaren.Api.Configuration;

/// <summary>
/// Konfig-driven <c>UseForwardedHeaders</c>-uppsättning (TD-21 / STEG 12). Bind:as
/// från <c>appsettings.&lt;env&gt;.json</c>-sektionen <see cref="SectionName"/>. I
/// dev (default tom array) bevaras ASP.NET-default-beteendet (loopback only). I
/// prod sätts <see cref="KnownNetworks"/> till ALB:s VPC-CIDR via Fargate task-
/// definition / IaC så <c>Connection.RemoteIpAddress</c> reflekterar klient-IP.
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
    /// I Jobbliggaren-prod: ALB:s VPC-CIDR. Tom array = ASP.NET-default (loopback).
    /// </summary>
    public string[] KnownNetworks { get; init; } = [];

    /// <summary>
    /// Single-IP-strings för enskilda proxies utanför VPC:n (t.ex. CloudFront-
    /// origin-IP om listan är stabil). Sällan använd i ALB-only-deploy.
    /// </summary>
    public string[] KnownProxies { get; init; } = [];

    /// <summary>
    /// Hur många proxy-hops som accepteras i X-Forwarded-For-kedjan. Default 1 i
    /// dev; 2 i prod (ALB → CloudFront om används). Värden &lt; 1 throwas.
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
                    "(förväntat format: '10.0.0.0/16'). Se docs/runbooks/aws-setup.md §3.3.");
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
                $"ForwardedHeaders:ForwardLimit måste vara 1-10, fick {ForwardLimit}. " +
                "Default 1 (direkt-anrop), 2 vid CloudFront+ALB-kedja.");
        }
        return ForwardLimit;
    }

    /// <summary>
    /// Production-defense per allow-list (security-auditor STEG 12 Sec-Major-1).
    /// Symmetri med Worker <c>safeForAutoSchema</c>-mönstret. Tom <see cref="KnownNetworks"/>
    /// bakom proxy = IP-rate-limiting i en bucket = effektivt no-op = OWASP A07-yta.
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
                $"ForwardedHeaders:KnownNetworks måste sättas till ALB:s VPC-CIDR utanför " +
                $"Development/Test (aktuell miljö: {environmentName}). " +
                "Tom array bakom proxy gör IP-baserad rate-limiting till no-op. " +
                "Se docs/runbooks/aws-setup.md §3.3.");
        }
    }
}
