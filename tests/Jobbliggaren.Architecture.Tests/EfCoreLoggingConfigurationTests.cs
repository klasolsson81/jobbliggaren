using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Configuration fitness function for ADR 0045's measurement plan (#752, perf-audit finding
/// g2). EF Core's per-statement logging is an <i>instrument</i>, not a default.
///
/// <para>
/// Left at Information, the category <c>Microsoft.EntityFrameworkCore.Database.Command</c>
/// emits one <c>Executed DbCommand</c> event per statement. The Platsbanken snapshot opens one
/// child DI scope — and therefore one <c>AppDbContext</c> — per item (ADR 0032 §5 child-scope
/// fix, ~47k items), so a single sync also emits ~47k <c>ContextInitialized</c> events, which
/// live in a <i>different</i> category (<c>Microsoft.EntityFrameworkCore.Infrastructure</c>,
/// Information by default). Silencing only the first category halves a flood that has two
/// sources — which is why both are asserted here.
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

    // --- What must NOT be silenced (guards over-correction) ----------------------------------

    [Theory]
    [MemberData(nameof(Hosts))]
    public void HostConfiguration_DbCommandCategory_StillSurfacesFailedSql(string host)
    {
        var logger = BuildLogger(host, DbCommandCategory);

        // RelationalEventId.CommandExecuted is Information; CommandError is Error. Silencing the
        // success chatter must not blind us to failing SQL.
        logger.IsEnabled(LogLevel.Warning).ShouldBeTrue(
            $"{host}: EF command logging is set to Warning, not None/Error — failed SQL " +
            "(CommandError, Error level) must still reach the log.");
        logger.IsEnabled(LogLevel.Error).ShouldBeTrue($"{host}: EF errors must always be logged.");
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

        dbCommand.IsEnabled(LogLevel.Information).ShouldBeFalse(
            $"{host} (Development): the base EF override must survive the overlay — dev is " +
            "precisely where a full local sync would drown the console and the local Seq.");
        efInfrastructure.IsEnabled(LogLevel.Information).ShouldBeFalse(
            $"{host} (Development): the ContextInitialized override must survive the overlay.");
    }

    // --- Harness ------------------------------------------------------------------------------

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
