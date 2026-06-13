using Jobbliggaren.Application.Common.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Jobbliggaren.Infrastructure.Identity;

/// <summary>
/// Bootstrap-jobb som körs vid app-startup. Skapar <c>Admin</c>-rollen om den
/// saknas (krävs för att <c>RequireRole("Admin")</c>-policyn ska kunna
/// utvärderas över huvud taget), och tilldelar rollen till user med email
/// <see cref="AdminBootstrapOptions.InitialAdminEmail"/> om matchande user finns.
///
/// Idempotent: säker att köra vid varje startup. Använder
/// <see cref="RoleManager{TRole}"/> och <see cref="UserManager{TUser}"/> så
/// Identity:s egna check-och-insert-flöden hanterar race-conditions.
///
/// Observability (TD-61 korrigering 2026-05-11 efter CTO Alt A): bootstrap-
/// aktivitet observeras via strukturerad logg — Admin-role-add emit:ar
/// EventId=2 ("Admin-rollen tilldelad till user {UserId} vid bootstrap.")
/// via <see cref="LogAdminAssigned"/> till ILogger, som routas till Seq
/// (dev) eller CloudWatch Logs (staging/prod) via Serilog-konfigurationen
/// i <c>Jobbliggaren.Api</c>. Seedern populerar INTE <c>audit_log</c>-tabellen
/// — admin-vyn (<c>GetAuditLogEntriesQueryHandler</c>) läser AuditLogEntries
/// skrivna av <c>AuditBehavior</c> via Mediator-commands markerade med
/// <c>IAuditableCommand</c> (ADR 0022). Bootstrap är en
/// <see cref="IHostedService"/> utanför Mediator-pipelinen, så dess
/// role-assignment hör inte hemma i samma tabell utan dedikerad
/// audit-skrivnings-port. Sådan port är kandidat för Fas 6 admin-
/// impersonation-ADR — inte aktuellt i Fas 1.
///
/// Audit-evidence verifieras av <c>IdempotentAdminRoleSeederAuditEvidenceTests</c>.
///
/// Senior-cto-advisor-beslut 2026-05-11: B1 över B2 — IaC-konsistens med
/// STEG 13/14 (Terraform + Migrate-task). Twelve-Factor §III/V.
/// </summary>
internal sealed partial class IdempotentAdminRoleSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<AdminBootstrapOptions> options,
    IHostEnvironment hostEnvironment,
    ILogger<IdempotentAdminRoleSeeder> logger)
    : IHostedService
{
    private readonly AdminBootstrapOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        try
        {
            await EnsureAdminRoleExistsAsync(roleManager, cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.InitialAdminEmail))
                await EnsureUserIsAdminAsync(userManager, _options.InitialAdminEmail, cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01" && IsSchemaInitGracePeriod(hostEnvironment))
        {
            // 42P01 = undefined_table. Identity-tabellerna finns inte ännu.
            // I prod-pipeline kör Jobbliggaren.Migrate (en separat ECS-task) DDL
            // FÖRE Api-tasken startar — så detta ska aldrig inträffa där.
            // I integration-test-fixturer triggas host-start innan migrations
            // körs (Migrate-anrop sker via Services-property som SJÄLV triggar
            // host-start → catch-22). Log warning och hoppa över seeding;
            // tester som kräver Admin-roll skapar den explicit per-test.
            //
            // N-2 hardening (arch-audit 2026-05-11): catch:en är gated på
            // Development/Test-environment. I prod bubblar 42P01 → host start
            // failer → ECS deployment_circuit_breaker triggar rollback. Detta
            // är fail-loud (CLAUDE.md §3.4 + §5.1) — Migrate-task-failure
            // ska larma, inte sluka tyst.
            LogSchemaMissing(logger);
        }
    }

    /// <summary>
    /// True om vi tolereras starta utan Identity-schema (Development eller Test).
    /// Production/Staging: undefined-table-fel måste bubbla per CLAUDE.md §3.4.
    /// Internal static för direkt unit-test mot gate-logiken utan att resolva
    /// hela seeder-DI-grafen.
    /// </summary>
    internal static bool IsSchemaInitGracePeriod(IHostEnvironment env) =>
        env.IsDevelopment() || env.IsEnvironment("Test");

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureAdminRoleExistsAsync(
        RoleManager<IdentityRole<Guid>> roleManager,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (await roleManager.RoleExistsAsync(Roles.Admin))
            return;

        var result = await roleManager.CreateAsync(new IdentityRole<Guid>(Roles.Admin));
        if (result.Succeeded)
        {
            LogAdminRoleCreated(logger);
            return;
        }

        // Race-condition: en annan instans skapade rollen mellan vår check och insert.
        // RoleExistsAsync igen för att skilja race från äkta fel.
        if (await roleManager.RoleExistsAsync(Roles.Admin))
            return;

        var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
        throw new InvalidOperationException(
            $"Kunde inte skapa Admin-rollen vid bootstrap: {errors}");
    }

    private async Task EnsureUserIsAdminAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            LogAdminUserNotFound(logger);
            return;
        }

        if (await userManager.IsInRoleAsync(user, Roles.Admin))
        {
            LogAdminAlreadyAssigned(logger, user.Id);
            return;
        }

        var result = await userManager.AddToRoleAsync(user, Roles.Admin);
        if (result.Succeeded)
        {
            LogAdminAssigned(logger, user.Id);
            return;
        }

        var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
        throw new InvalidOperationException(
            $"Kunde inte tilldela Admin-rollen till user {user.Id}: {errors}");
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Admin-rollen skapad vid bootstrap.")]
    private static partial void LogAdminRoleCreated(ILogger logger);

    // PII-disciplin: vi loggar UserId (Guid, pseudonym i GDPR-mening), inte email.
    // Email finns i config + Identity-tabellen, men ackumulerad logg-storage skulle
    // korrelera email mot bootstrap-händelser över tid (senior-cto-advisor 2026-05-11 M2).
    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Admin-rollen tilldelad till user {UserId} vid bootstrap.")]
    private static partial void LogAdminAssigned(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug,
        Message = "Admin-rollen redan tilldelad till user {UserId} — ingen ändring.")]
    private static partial void LogAdminAlreadyAssigned(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "AdminBootstrap.InitialAdminEmail satt men ingen matchande user hittades. Skippar tilldelning.")]
    private static partial void LogAdminUserNotFound(ILogger logger);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning,
        Message = "Admin-bootstrap skippad: Identity-tabellerna finns inte ännu. Kör migrations innan app-start i prod (Jobbliggaren.Migrate-task).")]
    private static partial void LogSchemaMissing(ILogger logger);
}
