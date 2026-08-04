namespace Jobbliggaren.Api.Configuration;

/// <summary>
/// Konfig-bindning för reverse-proxy-relaterade flaggor som styr middleware-pipelinen.
/// Symmetri med <see cref="ForwardedHeadersConfig"/> + RateLimitingOptions /
/// HangfireWorkerOptions (sealed class, init-only properties, public const SectionName).
///
/// <para>
/// <c>HttpsEnabled</c> styr om <c>UseHttpsRedirection()</c> och <c>UseHsts()</c>
/// registreras i pipelinen. The invariant it protects: redirecting to 443 when the proxy
/// in front has no reachable TLS listener breaks the health check and rolls the deploy
/// back. ADR 0026 established that gate.
/// </para>
///
/// <para>
/// <b>Under Option B (ADR 0050 Amendment 2026-07-18) this MUST stay false, and that is a
/// decision rather than an unfinished flip.</b> All browser traffic terminates in Caddy
/// and is served by Next; the ASP.NET API is never edge-exposed and is reached only
/// server-side over the internal Docker network — in plain HTTP. Flipping this to true
/// would make <c>UseHttpsRedirection()</c> answer 307 to every internal Next-to-API call
/// and break the app. <c>UseHsts()</c> is inert for a second, independent reason: the
/// API's response headers are consumed by a Next route handler and never reach a
/// browser, so browser-visible HSTS is owed outside ASP.NET, on BOTH response paths —
/// Caddy for the 401 that never reaches Next, and <c>buildSecurityHeaders</c> for the
/// Next path. See CLAUDE.md §11 (ADR 0050 Amendment 2026-08-04 §5; gate M-5a).
/// </para>
///
/// <para>
/// Bound ONCE in <c>Program.cs</c>, at service registration, and consumed twice — by the
/// HSTS validation gate there and by <c>UseHsts</c>/<c>UseHttpsRedirection</c> in the
/// pipeline. Not injected as <c>IOptions&lt;T&gt;</c>, because the values are only read at
/// startup. Binding it twice would be two normalisers for one rule, and the divergence
/// has a security direction (the validation could be skipped while <c>UseHsts()</c> still
/// registers). The preserved Terraform tree injects the OLD <c>Alb__HttpsEnabled</c>
/// name — a record of what actually ran, not live config (ADR 0066 kept the tree; see
/// the NOTE in <c>environments/dev/main.tf</c> and CLAUDE.md §11). Nothing injects this
/// section anywhere today, so the value binds false in every environment.
/// </para>
/// </summary>
public sealed class ReverseProxyOptions
{
    /// <summary>
    /// The retired <c>"Alb"</c> key has NO transitional fallback bind, deliberately
    /// (#196). Measured before removing it: no <c>"Alb"</c> section existed in any
    /// appsettings file, so the option bound false everywhere, and the only injector was
    /// the ECS task-definition ADR 0066 destroyed. A fallback would add a second magic
    /// string for an empty consumer set, and guard a silent-false failure that is
    /// indistinguishable from the state it replaced. <c>ReverseProxyOptionsTests</c>
    /// documents the retirement and pins that <b>this constant</b> does not match the old
    /// key — it does not pin the composition root, so a fallback bind added in
    /// <c>Program.cs</c> would still pass. The absence of a fallback is a documented
    /// intent, not an executable guarantee.
    ///
    /// <para>
    /// Latent collision worth knowing: YARP binds a top-level <c>"ReverseProxy"</c>
    /// section by convention. No YARP package is referenced today (verified against
    /// Directory.Packages.props), so there is no conflict — but a future adopter would
    /// otherwise discover this at runtime.
    /// </para>
    /// </summary>
    public const string SectionName = "ReverseProxy";

    /// <summary>
    /// True when the reverse proxy terminates TLS and has a reachable HTTPS listener.
    /// Triggers <c>app.UseHttpsRedirection()</c> and <c>app.UseHsts()</c> registration in
    /// Api/Program.cs. See the type-level remark: false is correct under Option B.
    /// </summary>
    public bool HttpsEnabled { get; init; }
}
