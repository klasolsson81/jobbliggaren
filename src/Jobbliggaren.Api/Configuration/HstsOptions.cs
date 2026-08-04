namespace Jobbliggaren.Api.Configuration;

/// <summary>
/// Konfig-bindning för HTTP Strict Transport Security (HSTS).
/// Symmetri med <see cref="ReverseProxyOptions"/> + <see cref="ForwardedHeadersConfig"/>
/// (sealed class, init-only properties, public const SectionName).
///
/// <para>
/// HSTS instruerar browsers att alltid använda HTTPS för domänen i <c>MaxAgeDays</c>
/// dagar framåt. Aktiveras tillsammans med <c>ReverseProxyOptions.HttpsEnabled</c> —
/// bägge flippas synkront med reverse-proxyns TLS-listener (ADR 0026-trigger 1).
/// Note that under Option B neither is expected to flip: browser-visible HSTS is owed
/// outside ASP.NET, on BOTH response paths — Caddy for the 401, and
/// <c>buildSecurityHeaders</c> for the Next path, which is not the edge — since the
/// API's responses never reach a browser. See
/// <see cref="ReverseProxyOptions"/>.
/// </para>
///
/// <para>
/// Header sätts bara på HTTPS-svar (ASP.NET-default). I Development-miljön
/// registreras <c>UseHsts()</c> ALDRIG — annars permanently lockar browsern
/// localhost till HTTPS i <c>MaxAgeDays</c>-fönstret, även efter att TLS-cert
/// roterats eller dev-miljön bytts.
/// </para>
///
/// <para>
/// <c>Preload</c>-flaggan default false. Aktivering kräver submission till
/// <see href="https://hstspreload.org"/> post-prod-launch och är effektivt
/// oåterkalleligt (browsern hardcodar; unsubmit tar 18+ månader). Lyfts till
/// prod-launch-checklistan, inte Fas 0.
/// </para>
/// </summary>
public sealed class HstsOptions
{
    public const string SectionName = "Hsts";

    /// <summary>
    /// HSTS max-age i dagar. Default 365 (HSTS-spec-rekommendation, även
    /// hstspreload.org-krav för preload-submission). 0 = HSTS effektivt
    /// disabled (browsern bortser från header).
    /// </summary>
    public int MaxAgeDays { get; init; } = 365;

    /// <summary>
    /// Inkludera <c>includeSubDomains</c>-direktiv. Default true. Skyddar
    /// alla subdomäner (api.*, www.*, etc) — kräver att ALLA subdomäner
    /// betjänas via HTTPS. Om någon subdomän behöver HTTP-only: sätt false.
    /// </summary>
    public bool IncludeSubDomains { get; init; } = true;

    /// <summary>
    /// Inkludera <c>preload</c>-direktiv. Default false. Aktivering enbart
    /// efter prod-launch + hstspreload.org-submission. Hardcodar i browsern
    /// — oåterkalleligt på kort sikt.
    /// </summary>
    public bool Preload { get; init; }

    /// <summary>
    /// Production-defense (paritet med
    /// <c>ForwardedHeadersConfig.EnsureSafeForEnvironment</c>): fail-loud vid
    /// uppstart om HSTS-config skulle ge tyst säkerhetsregression i Production.
    /// Anropas i Program.cs gate:at på <c>ReverseProxyOptions.HttpsEnabled</c> så HTTP-
    /// only Fas 0 (ADR 0026) inte triggar throw.
    /// </summary>
    /// <param name="environmentName">ASP.NET Core-environment-namn (Development/Test/Production/Staging/...).</param>
    /// <exception cref="ArgumentException">Tom <paramref name="environmentName"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// MaxAgeDays &lt; 365 utanför Development/Test, eller Preload=true utan
    /// hstspreload.org-krav uppfyllda (MaxAgeDays&gt;=365 + IncludeSubDomains=true).
    /// </exception>
    public void EnsureSafeForEnvironment(string environmentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        var isDevOrTest =
            string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);

        if (isDevOrTest)
            return;

        if (MaxAgeDays < 365)
        {
            throw new InvalidOperationException(
                $"Hsts:MaxAgeDays måste vara >= 365 utanför Development/Test (aktuell miljö: " +
                $"{environmentName}, fick {MaxAgeDays}). HSTS-spec + hstspreload.org-krav. " +
                "Se docs/decisions/0027-* när ADR 0026 supersedas.");
        }

        if (Preload && (MaxAgeDays < 365 || !IncludeSubDomains))
        {
            throw new InvalidOperationException(
                "Hsts:Preload=true kräver MaxAgeDays>=365 OCH IncludeSubDomains=true " +
                "(hstspreload.org-submission-krav). Aktivera Preload bara efter dokumenterad " +
                "submission per docs/runbooks/.");
        }
    }
}
