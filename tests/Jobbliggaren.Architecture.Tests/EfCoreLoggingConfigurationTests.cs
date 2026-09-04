using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Configuration fitness function for ADR 0045's measurement plan (#752, perf-audit finding
/// g2; extended by #1633). EF Core's per-statement logging is an <i>instrument</i>, not a default.
///
/// <para>
/// Left at Information, the category <c>Microsoft.EntityFrameworkCore.Database.Command</c>
/// emits one <c>Executed DbCommand</c> event per statement. The Platsbanken snapshot opens one
/// child DI scope — and therefore one <c>AppDbContext</c> — per item (ADR 0032 §5 child-scope
/// fix, ~47k items), so a single sync also emits ~47k <c>ContextInitialized</c> events, which
/// live in a <i>different</i> category (<c>Microsoft.EntityFrameworkCore.Infrastructure</c>,
/// Information by default). <c>Microsoft.EntityFrameworkCore.Update</c> is the third
/// (#1633): it carries <c>SaveChangesFailed</c>, which the ingest job's absorbed duplicate-key
/// path triggers on every collision. Silencing one of the three leaves the other two — which is
/// why all three are asserted here.
/// </para>
///
/// <para>
/// <b>Two halves, and the property needs both.</b> A category rule can only silence an event at
/// the level EF <i>emits</i> it. <c>CommandError</c> and <c>SaveChangesFailed</c> are Error by
/// default, so a Warning rule does not reach them; <c>AddPersistence</c>'s
/// <c>ConfigureWarnings</c> moves both to Information (#1633) and the rules below then take the
/// volume. <see cref="AddPersistence_ExpectedDatabaseOutcomes_AreEmittedAtInformation"/> pins the
/// emitting half so neither can be removed while the other keeps this file green.
/// </para>
///
/// <para>
/// The assertions run the SHIPPED appsettings files through the real MEL filter engine (the
/// same <c>AddConfiguration(GetSection("Logging"))</c> binding both hosts use), rather than
/// string-matching the JSON. A text assertion would keep passing if the config-merge semantics
/// changed underneath it, and it could not tell "silenced" from "silenced too much".
/// </para>
///
/// <para>
/// Naming: <c>&lt;ClassUnderTest&gt;_&lt;Scenario&gt;_&lt;Expected&gt;</c>.
/// </para>
/// </summary>
public class EfCoreLoggingConfigurationTests
{
    /// <summary>Per-statement SQL. The flood #752 removes; the instrument the runbook re-enables.</summary>
    private const string DbCommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    /// <summary>Carries <c>ContextInitialized</c> — one event per DbContext, i.e. per snapshot item.</summary>
    private const string EfInfrastructureCategory = "Microsoft.EntityFrameworkCore.Infrastructure";

    /// <summary>Carries <c>SaveChangesFailed</c> — one event per absorbed duplicate key (#1633).</summary>
    private const string EfUpdateCategory = "Microsoft.EntityFrameworkCore.Update";

    /// <summary>"Applying migration ..." — deliberately still Information (see the parent-category test).</summary>
    private const string EfMigrationsCategory = "Microsoft.EntityFrameworkCore.Migrations";

    /// <summary>An ordinary product category — the probe's own vacuity guard.</summary>
    private const string ProductCategory = "Jobbliggaren.Application.JobAds.Jobs.SyncPlatsbanken";

    public static TheoryData<string> Hosts => ["Api", "Worker"];

    // --- The probe can say "enabled". Without this, every assertion below is vacuous. ---------
    //
    // MEL resolves IsEnabled as (filter rules) AND (the provider's own logger). A LoggerFactory
    // with ZERO providers answers false for EVERYTHING — so a "not enabled at Information"
    // assertion would pass against a harness that can never say yes, and could never go red.
    // That is the #843 test-fiction class. AlwaysEnabledProvider pins the provider side to true
    // so the answer is decided purely by the filter rules under test, and this test proves it.

    [Theory]
    [MemberData(nameof(Hosts))]
    public void HostConfiguration_OrdinaryProductCategory_IsEnabledAtInformation(string host)
    {
        var logger = BuildLogger(host, ProductCategory);

        logger.IsEnabled(LogLevel.Information).ShouldBeTrue(
            $"{host}: the probe must be able to answer 'enabled' — otherwise every " +
            "'not enabled' assertion in this class is vacuous and can never go red. " +
            "Logging:LogLevel:Default is Information; a product category must inherit it.");
    }

    // --- The two floods --------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Hosts))]
    public void HostConfiguration_DbCommandCategory_IsNotEnabledAtInformation(string host)
    {
        var logger = BuildLogger(host, DbCommandCategory);

        logger.IsEnabled(LogLevel.Information).ShouldBeFalse(
            $"{host}: EF per-statement logging must be off by default (#752 / finding g2) — " +
            "one Platsbanken sync would otherwise emit 100k+ 'Executed DbCommand' events. " +
            "Turn it on for a measurement session via appsettings.Local.json " +
            "(docs/runbooks/performance-measurement.md §D).");
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void HostConfiguration_EfInfrastructureCategory_IsNotEnabledAtInformation(string host)
    {
        var logger = BuildLogger(host, EfInfrastructureCategory);

        logger.IsEnabled(LogLevel.Information).ShouldBeFalse(
            $"{host}: ContextInitialized lives in this category and fires once per DbContext. " +
            "The snapshot job opens one child scope per item (~47k), so leaving this at " +
            "Information keeps ~47k events per sync even after Database.Command is silenced.");
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void HostConfiguration_EfUpdateCategory_IsNotEnabledAtInformation(string host)
    {
        var logger = BuildLogger(host, EfUpdateCategory);

        logger.IsEnabled(LogLevel.Information).ShouldBeFalse(
            $"{host}: SaveChangesFailed lives in this category and fires on every absorbed " +
            "duplicate key (#1633). AddPersistence emits it at Information; without this rule " +
            "the whole DbUpdateException — stack trace included, twice — still ships.");
    }

    // --- What must NOT be silenced (guards over-correction) ----------------------------------

    [Theory]
    [MemberData(nameof(Hosts))]
    public void HostConfiguration_EfUpdateCategory_StillSurfacesWarnings(string host)
    {
        var logger = BuildLogger(host, EfUpdateCategory);

        // Warning is the floor, not None. This category also carries EF's own update warnings —
        // among them the optimistic-concurrency and cascade-delete diagnostics — which #1633's
        // volume argument does not reach: they are not emitted per absorbed collision.
        logger.IsEnabled(LogLevel.Warning).ShouldBeTrue(
            $"{host}: this category must stay at Warning, not None — #1633 silences the two " +
            "events it re-levelled in AddPersistence, not the category's own warnings.");
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void HostConfiguration_EfInfrastructureCategory_StillSurfacesTheSensitiveDataTripwire(string host)
    {
        var logger = BuildLogger(host, EfInfrastructureCategory);

        // EF's own SensitiveDataLoggingEnabledWarning is emitted at Warning in THIS category.
        // The runbook's PII guard-rail (§D) leans on that tripwire: if anyone ever turns
        // EnableSensitiveDataLogging on, EF says so in the log. Setting this category to None
        // would silence the tripwire while leaving every "not enabled at Information" assertion
        // above green — the guard-rail would then be resting on a warning that can no longer
        // fire. Warning is the floor here, not an accident.
        logger.IsEnabled(LogLevel.Warning).ShouldBeTrue(
            $"{host}: this category must stay at Warning, not None — it carries EF's " +
            "SensitiveDataLoggingEnabledWarning, the tripwire that would tell us someone " +
            "enabled EnableSensitiveDataLogging (docs/runbooks/performance-measurement.md §D).");
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void HostConfiguration_EfMigrationsCategory_RemainsEnabledAtInformation(string host)
    {
        var logger = BuildLogger(host, EfMigrationsCategory);

        // This is why the override names two precise categories instead of the parent
        // "Microsoft.EntityFrameworkCore". The parent would silence this too, and "Applying
        // migration ..." is a line we want on every deploy.
        logger.IsEnabled(LogLevel.Information).ShouldBeTrue(
            $"{host}: migration logging must survive. If this went red, someone replaced the " +
            "two precise EF category overrides with the parent category " +
            "'Microsoft.EntityFrameworkCore', which silences migrations as collateral.");
    }

    // --- Merge semantics: the base override must survive the Development overlay ---------------

    [Theory]
    [MemberData(nameof(Hosts))]
    public void HostConfiguration_DevelopmentOverlay_DoesNotResurrectTheFlood(string host)
    {
        // Both Development overlays restate Logging:LogLevel (Default + one Microsoft.* key).
        // Configuration merges key-by-key, not section-by-section, so the base EF overrides
        // survive — but that is a claim about MEL's merge semantics, so it gets pinned rather
        // than assumed. Development is where a developer actually runs a sync.
        var dbCommand = BuildLogger(host, DbCommandCategory, withDevelopmentOverlay: true);
        var efInfrastructure = BuildLogger(host, EfInfrastructureCategory, withDevelopmentOverlay: true);
        var efUpdate = BuildLogger(host, EfUpdateCategory, withDevelopmentOverlay: true);

        dbCommand.IsEnabled(LogLevel.Information).ShouldBeFalse(
            $"{host} (Development): the base EF override must survive the overlay — dev is " +
            "precisely where a full local sync would drown the console and the local Seq.");
        efInfrastructure.IsEnabled(LogLevel.Information).ShouldBeFalse(
            $"{host} (Development): the ContextInitialized override must survive the overlay.");
        efUpdate.IsEnabled(LogLevel.Information).ShouldBeFalse(
            $"{host} (Development): the SaveChangesFailed override must survive the overlay.");
    }

    // --- The emitting half: what level AddPersistence hands MEL ------------------------------
    //
    // The category rules above can only silence an event at the level EF EMITS it. Both events
    // below are Error by default, which no Warning rule reaches. This drives the real
    // AddPersistence composition and reads the resulting DbContextOptions.

    [Fact]
    public void AddPersistence_ExpectedDatabaseOutcomes_AreEmittedAtInformation()
    {
        var warnings = BuildWarningsConfiguration();

        warnings.GetLevel(RelationalEventId.CommandError).ShouldBe(
            LogLevel.Information,
            "#1633: a UNIQUE violation the code absorbs by design is an EXPECTED outcome, and " +
            "EF's Error default misreports it. Removing this override puts the failing INSERT " +
            "back on the Error channel once per collision.");

        warnings.GetLevel(CoreEventId.SaveChangesFailed).ShouldBe(
            LogLevel.Information,
            "#1633: same event, second EF report — the whole DbUpdateException with its stack " +
            "trace, printed twice by the console formatter. This is the larger half by volume.");
    }

    [Fact]
    public void AddPersistence_AnEventItDoesNotRelevel_KeepsEfsOwnDefault()
    {
        // The probe's discriminator. Without this, the assertions above could not tell "the two
        // overrides are configured" from "GetLevel answers Information for everything".
        BuildWarningsConfiguration().GetLevel(CoreEventId.SaveChangesStarting).ShouldBeNull(
            "AddPersistence must re-level exactly the two events it names — an event it does " +
            "not configure has no override, and this is what makes the two assertions above " +
            "capable of going red.");
    }

    // --- Harness ------------------------------------------------------------------------------

    /// <summary>
    /// Runs the SHIPPED <c>AddPersistence</c> and returns the warnings configuration its
    /// <c>DbContextOptions</c> carries. No database is touched: <c>UseNpgsql</c> only records the
    /// connection string, and the two interceptors the options lambda resolves are parameterless
    /// singletons registered by <c>AddPersistence</c> itself.
    /// </summary>
    private static WarningsConfiguration BuildWarningsConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Never connected to. AddPersistence throws without the key, so it must be present.
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=unused",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddPersistence(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<DbContextOptions<AppDbContext>>();

        return options.FindExtension<CoreOptionsExtension>()
            .ShouldNotBeNull("every DbContextOptions carries a CoreOptionsExtension")
            .WarningsConfiguration;
    }

    private static ILogger BuildLogger(string host, string category, bool withDevelopmentOverlay = false)
    {
        var hostsDir = Path.Combine(AppContext.BaseDirectory, "hosts");

        var builder = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(hostsDir, $"{host}.appsettings.json"), optional: false);

        if (withDevelopmentOverlay)
        {
            builder.AddJsonFile(
                Path.Combine(hostsDir, $"{host}.appsettings.Development.json"), optional: false);
        }

        var configuration = builder.Build();

        // The exact binding both composition roots get from Host.CreateApplicationBuilder /
        // WebApplication.CreateBuilder: the "Logging" section drives the filter rules.
        var factory = LoggerFactory.Create(logging =>
        {
            logging.AddConfiguration(configuration.GetSection("Logging"));
            logging.AddProvider(new AlwaysEnabledProvider());
        });

        return factory.CreateLogger(category);
    }

    /// <summary>
    /// A provider whose logger is enabled for every level, so <see cref="ILogger.IsEnabled"/> on
    /// the composite logger reflects the configured filter rules and nothing else. The real
    /// console/Seq providers are level-permissive in the same way; what decides the outcome in
    /// production is the filter configuration, which is what these tests are about.
    /// </summary>
    private sealed class AlwaysEnabledProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new AlwaysEnabledLogger();

        public void Dispose()
        {
        }

        private sealed class AlwaysEnabledLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
            }
        }
    }
}
