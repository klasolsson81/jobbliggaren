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
/// browser, so browser-visible HSTS is owed by the edge, not by ASP.NET (ADR 0050
/// Amendment 2026-08-04, section 5; gate M-5a).
/// </para>
///
/// <para>
/// Read directly twice in <c>Program.cs</c> — once at service registration (to validate
/// the HSTS options) and once when composing the pipeline — rather than injected as
/// <c>IOptions&lt;T&gt;</c>, because the values are only read at startup. The preserved
/// Terraform tree still injects this as an env-var (ADR 0066 kept the tree, CLAUDE.md
/// §11); no live injector exists on the Netcup box, so the value binds false there.
/// </para>
/// </summary>
public sealed class ReverseProxyOptions
{
    public const string SectionName = "Alb";

    /// <summary>
    /// True when the reverse proxy terminates TLS and has a reachable HTTPS listener.
    /// Triggers <c>app.UseHttpsRedirection()</c> and <c>app.UseHsts()</c> registration in
    /// Api/Program.cs. See the type-level remark: false is correct under Option B.
    /// </summary>
    public bool HttpsEnabled { get; init; }
}
