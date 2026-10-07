using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.UnitTests.Auth;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.IdentityBootstrap;

/// <summary>
/// TD-61 audit-evidence: verifierar att <see cref="IdempotentAdminRoleSeeder"/>
/// emit:ar LogAdminAssigned (EventId=2) till ILogger när bootstrap faktiskt
/// tilldelar Admin-rollen, och INTE när rollen redan är tilldelad (idempotens-
/// no-op-vägen tar EventId=3 LogAdminAlreadyAssigned istället).
///
/// <para>
/// CTO-beslut 2026-05-11 (senior-cto-advisor Alt A): seederns observability
/// går via ILogger → Seq — INTE via <c>audit_log</c>-
/// tabellen. ADR 0022 etablerar att DB-audit kräver <c>IAuditableCommand</c>-
/// marker på Mediator-command. Bootstrap är <see cref="IHostedService"/>
/// utanför Mediator-pipelinen och har därför ingen <c>AuditLogEntry</c>-rad.
/// Detta test bevisar att evidence-spåret som faktiskt finns (strukturerad
/// log-rad) emit:as när det ska och inte när det inte ska.
/// </para>
///
/// <para>
/// In-memory Identity-store via EF Core <c>UseInMemoryDatabase</c>. Identity
/// CRUD-operationer (FindByEmail, IsInRole, AddToRole) fungerar utan riktig
/// Postgres — separat <c>IdempotentAdminRoleSeederProdBubbleTests</c> i
/// Api.IntegrationTests täcker prod-pipeline-beteendet med Testcontainers.
/// </para>
/// </summary>
public class IdempotentAdminRoleSeederAuditEvidenceTests
{
    private const string AdminEmail = "admin@jobbliggaren.test";

    [Fact]
    public async Task StartAsync_MatchingUserExists_EmitsLogAdminAssignedEventId2()
    {
        var ct = TestContext.Current.CancellationToken;
        var capturingLogger = new CapturingLogger();
        await using var sp = BuildServiceProvider();
        await CreateUserAsync(sp, AdminEmail, ct);

        var seeder = BuildSeeder(sp, AdminEmail, capturingLogger);
        await seeder.StartAsync(ct);

        var assigned = capturingLogger.Entries
            .Where(e => e.EventId.Id == 2 && e.LogLevel == LogLevel.Information)
            .ToList();
        assigned.Count.ShouldBe(
            1,
            "LogAdminAssigned (EventId=2) ska emit:as exakt en gång när seedern faktiskt tilldelar Admin-rollen.");
        var access = sp.GetRequiredService<AccountAccessTestKit.RecordingAccountAccessCoordinator>();
        var begun = access.BegunScopes.ShouldHaveSingleItem();
        begun.Lifecycle.ShouldBeTrue();
        begun.UserIds.ShouldBe([(await UserIdAsync(sp, AdminEmail))]);
        access.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task StartAsync_UserAlreadyAdmin_DoesNotEmitEventId2AndEmitsEventId3()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildServiceProvider();
        await CreateUserAsync(sp, AdminEmail, ct);
        await PromoteToAdminAsync(sp, AdminEmail, ct);

        var capturingLogger = new CapturingLogger();
        var seeder = BuildSeeder(sp, AdminEmail, capturingLogger);
        await seeder.StartAsync(ct);

        capturingLogger.Entries.ShouldNotContain(
            e => e.EventId.Id == 2,
            "Idempotens: LogAdminAssigned (EventId=2) får inte emit:as när rollen redan är tilldelad.");
        capturingLogger.Entries.ShouldContain(
            e => e.EventId.Id == 3 && e.LogLevel == LogLevel.Debug,
            "No-op-vägen ska emit:a LogAdminAlreadyAssigned (EventId=3) som bevis för att seedern kört men hoppat över role-add.");
    }

    [Fact]
    public async Task StartAsync_NoMatchingUser_DoesNotEmitEventId2AndWarnsUserNotFound()
    {
        // Ingen user pre-seedad — seedern ska logga EventId=4 (LogAdminUserNotFound)
        // och absolut inte EventId=2 (ingen tilldelning skedde).
        var ct = TestContext.Current.CancellationToken;
        var capturingLogger = new CapturingLogger();
        await using var sp = BuildServiceProvider();

        var seeder = BuildSeeder(sp, AdminEmail, capturingLogger);
        await seeder.StartAsync(ct);

        capturingLogger.Entries.ShouldNotContain(e => e.EventId.Id == 2);
        capturingLogger.Entries.ShouldContain(
            e => e.EventId.Id == 4 && e.LogLevel == LogLevel.Warning);
    }

    [Fact]
    public async Task StartAsync_AnotherAccountHoldsTheRole_GrantsNothingAndWarnsByUserIdOnly()
    {
        // The configured address now names a second account: its admin moved address through the self-service
        // change-email flow, and the next account to prove that inbox took it (#2001, security-auditor's probe).
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildServiceProvider();
        await CreateUserAsync(sp, "holder@jobbliggaren.test", ct);
        await PromoteToAdminAsync(sp, "holder@jobbliggaren.test", ct);
        await CreateUserAsync(sp, AdminEmail, ct);
        var capturingLogger = new CapturingLogger();

        await BuildSeeder(sp, AdminEmail, capturingLogger).StartAsync(ct);

        (await IsAdminAsync(sp, AdminEmail)).ShouldBeFalse();
        capturingLogger.Entries.ShouldNotContain(e => e.EventId.Id == 2);
        var warning = capturingLogger.Entries.Single(e => e.EventId.Id == 6);
        warning.LogLevel.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain((await UserIdAsync(sp, AdminEmail)).ToString());
        warning.Message.ShouldNotContain(AdminEmail);
    }

    [Fact]
    public async Task StartAsync_CandidatePendingDeletion_GrantsNothingAndReportsUnavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildServiceProvider();
        await CreateUserAsync(sp, AdminEmail, ct);
        var userId = await UserIdAsync(sp, AdminEmail);
        var app = sp.GetRequiredService<AppDbContext>();
        var profile = await app.JobSeekers.SingleAsync(seeker => seeker.UserId == userId, ct);
        profile.SoftDelete(FakeDateTimeProvider.Default);
        profile.DeletedAt.ShouldBe(FakeDateTimeProvider.Default.UtcNow);
        await app.SaveChangesAsync(ct);
        var logger = new CapturingLogger();

        await BuildSeeder(sp, AdminEmail, logger).StartAsync(ct);

        (await IsAdminAsync(sp, AdminEmail)).ShouldBeFalse();
        logger.Entries.ShouldNotContain(entry => entry.EventId.Id == 2);
        logger.Entries.ShouldContain(entry => entry.EventId.Id == 7);
        (await app.JobSeekers.IgnoreQueryFilters().SingleAsync(seeker => seeker.UserId == userId, ct))
            .DeletedAt.ShouldBe(FakeDateTimeProvider.Default.UtcNow);
    }

    [Fact]
    public async Task StartAsync_UnreachableOperatorErasedProfile_GrantsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildServiceProvider();
        await CreateUserAsync(sp, AdminEmail, ct);
        var userId = await UserIdAsync(sp, AdminEmail);
        var app = sp.GetRequiredService<AppDbContext>();
        // Declared unreachable through today's atomic account lifecycle: operator damage deletes
        // the profile while leaving Identity. Assert only that bootstrap fails closed on that read.
        app.JobSeekers.Remove(await app.JobSeekers.SingleAsync(seeker => seeker.UserId == userId, ct));
        await app.SaveChangesAsync(ct);
        var logger = new CapturingLogger();

        await BuildSeeder(sp, AdminEmail, logger).StartAsync(ct);

        (await IsAdminAsync(sp, AdminEmail)).ShouldBeFalse();
        logger.Entries.ShouldNotContain(entry => entry.EventId.Id == 2);
        logger.Entries.ShouldContain(entry => entry.EventId.Id == 7);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var dbName = $"identity-tests-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppIdentityDbContext>(opts =>
            opts.UseInMemoryDatabase(dbName));
        services.AddSingleton<AppDbContext>(_ => TestAppDbContextFactory.Create());
        services.AddSingleton<AccountAccessTestKit.RecordingAccountAccessCoordinator>(_ => AccountAccessTestKit.Coordinator());
        services.AddSingleton<IAccountAccessCoordinator>(provider =>
            provider.GetRequiredService<AccountAccessTestKit.RecordingAccountAccessCoordinator>());
        services.AddScoped<IAccountAccessReader>(provider =>
        {
            var identity = provider.GetRequiredService<AppIdentityDbContext>();
            var app = provider.GetRequiredService<AppDbContext>();
            return AccountAccessTestKit.Reader(userId =>
            {
                provider.GetRequiredService<IAccountAccessCoordinator>().Holds(userId).ShouldBeTrue(
                    "Bootstrap admission must be read while its candidate account is held.");
                var user = identity.Users.AsNoTracking().SingleOrDefault(user => user.Id == userId);
                if (user?.Email is not { } email)
                    return null;
                var profile = app.JobSeekers.IgnoreQueryFilters().AsNoTracking()
                    .Where(seeker => seeker.UserId == userId).Select(seeker => new { seeker.DeletedAt }).FirstOrDefault();
                var isAdmin = identity.UserRoles.Join(identity.Roles,
                    assignment => assignment.RoleId, role => role.Id, (assignment, role) => new { assignment.UserId, role.Name })
                    .Any(assignment => assignment.UserId == userId && assignment.Name == Roles.Admin);
                return new AccountAccessSnapshot(userId, email, user.IsSuspended, user.AccessRevision,
                    user.CredentialCutoff, profile is not null, profile?.DeletedAt, isAdmin);
            });
        });

        // AddIdentityCore-pattern matchar Worker-DI (HTTP-fri Identity-stack).
        // Räcker för CreateAsync/FindByEmailAsync/IsInRoleAsync/AddToRoleAsync.
        services.AddIdentityCore<ApplicationUser>(opts =>
            {
                opts.Password.RequiredLength = 10;
                opts.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppIdentityDbContext>();

        var sp = services.BuildServiceProvider();
        using (var scope = sp.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>()
                .Database.EnsureCreated();
        }
        return sp;
    }

    private static async Task CreateUserAsync(IServiceProvider sp, string email, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
        };
        var result = await userManager.CreateAsync(user);
        result.Succeeded.ShouldBeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));
        var app = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = JobSeeker.Register(user.Id, TermsAcceptance.AcceptCurrent(FakeDateTimeProvider.Default),
            FakeDateTimeProvider.Default).Value;
        app.JobSeekers.Add(profile);
        await app.SaveChangesAsync(ct);
    }

    private static async Task PromoteToAdminAsync(IServiceProvider sp, string email, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = sp.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roleManager.RoleExistsAsync(Roles.Admin))
            (await roleManager.CreateAsync(new IdentityRole<Guid>(Roles.Admin))).Succeeded.ShouldBeTrue();

        var user = await userManager.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"User {email} saknas — pre-seeda först.");
        (await userManager.AddToRoleAsync(user, Roles.Admin)).Succeeded.ShouldBeTrue();
    }

    private static async Task<bool> IsAdminAsync(IServiceProvider sp, string email)
    {
        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email) ?? throw new InvalidOperationException($"User {email} saknas.");
        return await userManager.IsInRoleAsync(user, Roles.Admin);
    }

    private static async Task<Guid> UserIdAsync(IServiceProvider sp, string email)
    {
        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await userManager.FindByEmailAsync(email) ?? throw new InvalidOperationException($"User {email} saknas.")).Id;
    }

    private static IdempotentAdminRoleSeeder BuildSeeder(
        IServiceProvider sp,
        string initialAdminEmail,
        ILogger<IdempotentAdminRoleSeeder> logger)
    {
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new AdminBootstrapOptions { InitialAdminEmail = initialAdminEmail });
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Test");
        return new IdempotentAdminRoleSeeder(scopeFactory, options, env, logger);
    }

    /// <summary>
    /// Minimal ILogger-fångare för EventId/LogLevel-assertions. Undviker
    /// extern paketreferens (Microsoft.Extensions.Logging.Testing) — vi behöver
    /// bara strukturerad-event-id-bevis, inte fullständig log-message-template-
    /// rendering. Lagrar entries thread-safely.
    /// </summary>
    private sealed class CapturingLogger : ILogger<IdempotentAdminRoleSeeder>
    {
        private readonly List<LogEntry> _entries = new();
        private readonly object _gate = new();

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_gate) return _entries.ToList();
            }
        }

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_gate)
            {
                _entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception)));
            }
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, EventId EventId, string Message);
}
