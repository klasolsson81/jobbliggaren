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
public sealed class AdminBootstrapOptions
{
    public const string SectionName = "AdminBootstrap";

    public string? InitialAdminEmail { get; init; }
}
