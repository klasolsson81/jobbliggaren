namespace Jobbliggaren.Api.Configuration;

/// <summary>
/// Konfig-bindning för ALB-relaterade flaggor som styr middleware-pipelinen.
/// Symmetri med <see cref="ForwardedHeadersConfig"/> + RateLimitingOptions /
/// HangfireWorkerOptions (sealed class, init-only properties, public const SectionName).
///
/// <para>
/// <c>HttpsEnabled</c> styr om <c>UseHttpsRedirection()</c> registreras i pipelinen.
/// Bakom HTTP-only-ALB (per ADR 0026) skulle redirect → port 443 (stängd) → ALB-
/// health-check fail → ECS deployment_circuit_breaker rollback. Default false fram
/// till ADR 0026-trigger uppfylls (domän + ACM-cert, eller superseder-ADR).
/// </para>
///
/// <para>
/// Värdet injiceras av Terraform som env-var <c>Alb__HttpsEnabled</c> (ECS task-def)
/// och flippas synkront med ALB-listenerns konfiguration via samma
/// <c>var.alb_https_enabled</c> i <c>environments/dev/variables.tf</c>.
/// </para>
/// </summary>
public sealed class AlbOptions
{
    public const string SectionName = "Alb";

    /// <summary>
    /// True om ALB-listenern lyssnar på HTTPS port 443. Triggar
    /// <c>app.UseHttpsRedirection()</c>-registrering i Api/Program.cs.
    /// </summary>
    public bool HttpsEnabled { get; init; }
}
