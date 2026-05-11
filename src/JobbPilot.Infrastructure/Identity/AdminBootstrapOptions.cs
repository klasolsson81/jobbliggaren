namespace JobbPilot.Infrastructure.Identity;

/// <summary>
/// Config för admin-roll-bootstrap. Bind:s från sektionen <c>AdminBootstrap</c>
/// i <c>appsettings.json</c> (eller env-vars). När <see cref="InitialAdminEmail"/>
/// är satt skapar <see cref="IdempotentAdminRoleSeeder"/> Admin-rollen om den
/// saknas och tilldelar den till matchande user vid startup.
///
/// Tom email = inget tilldelas (rollen skapas ändå om den saknas så att
/// admin-policies fungerar i alla miljöer).
/// </summary>
/// <remarks>
/// <strong>Prod-konfig-källa (TD-50):</strong> läs ALDRIG via
/// <c>appsettings.json</c> direkt. I prod ska <c>AdminBootstrap__InitialAdminEmail</c>
/// hämtas från AWS Secrets Manager och injiceras som ECS task-def env-var.
/// Se <c>docs/runbooks/admin-bootstrap.md</c> för prod-konfig-flödet.
/// I dev/test bind:s värdet typiskt via <c>appsettings.Development.json</c>
/// (lokalt mail som <c>klas@jobbpilot.test</c>).
/// </remarks>
public sealed class AdminBootstrapOptions
{
    public const string SectionName = "AdminBootstrap";

    public string? InitialAdminEmail { get; init; }
}
