using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #2198 (ADR 0180) — configuration fitness for "the occupation search term is never logged".
/// The one framework line that would print a request's query string, <c>?q=&lt;term&gt;</c>, is the
/// hosting diagnostics' "Request starting …" message at Information. Only the
/// <c>Microsoft.AspNetCore</c> rule keeps it out, so this runs every shipped Api overlay through
/// the real MEL filter engine (the <see cref="EfCoreLoggingConfigurationTests"/> form) and
/// asserts the category is off at Information. The integration canary test runs one
/// environment's configuration; this is what covers Production.
/// </summary>
public class ApiRequestLoggingConfigurationTests
{
    private const string HostingDiagnosticsCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";

    /// <summary>An ordinary product category, the probe's own vacuity guard.</summary>
    private const string ProductCategory = "Jobbliggaren.Application.JobAds.Queries.SearchOccupations";

    /// <summary>The base file and every environment overlay linked into <c>hosts/</c>.</summary>
    public static TheoryData<string> Overlays
    {
        get
        {
            var data = new TheoryData<string> { "" };
            foreach (var overlay in LinkedOverlayNames())
                data.Add(overlay);
            return data;
        }
    }

    [Fact]
    public void EveryShippedApiOverlay_IsLinkedIntoTheProbe()
    {
        // A new appsettings.<Env>.json would otherwise ship without the two facts below ever reading it.
        var shipped = Directory.GetFiles(Path.Combine(RepositoryRoot(), "src", "Jobbliggaren.Api"), "appsettings.*.json")
            .Select(f => Path.GetFileName(f)["appsettings.".Length..^".json".Length])
            .Where(env => !env.Equals("Local", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();

        shipped.ShouldNotBeEmpty();
        LinkedOverlayNames().ShouldBe(shipped);
    }

    [Theory]
    [MemberData(nameof(Overlays))]
    public void ApiConfiguration_HasNoProviderSpecificLogLevelRule(string overlay)
    {
        // The probe below reads the provider-neutral rules. A rule under Logging:<provider>:LogLevel (Seq,
        // Console) would apply to that provider alone and the probe would never see it, so none may exist.
        var logging = BuildConfiguration(overlay).GetSection("Logging");
        logging.Exists().ShouldBeTrue();

        logging.GetChildren()
            .Where(child => child.Key != "LogLevel" && child.GetSection("LogLevel").Exists())
            .Select(child => child.Key)
            .ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Overlays))]
    public void ApiConfiguration_OrdinaryProductCategory_IsEnabledAtInformation(string overlay)
    {
        BuildLogger(overlay, ProductCategory).IsEnabled(LogLevel.Information).ShouldBeTrue(
            $"Api{(overlay.Length == 0 ? string.Empty : "." + overlay)}: the probe must be able to answer " +
            "'enabled', or the assertion below is vacuous and can never go red.");
    }

    [Theory]
    [MemberData(nameof(Overlays))]
    public void ApiConfiguration_HostingDiagnostics_IsNotEnabledAtInformation(string overlay)
    {
        BuildLogger(overlay, HostingDiagnosticsCategory).IsEnabled(LogLevel.Information).ShouldBeFalse(
            $"Api{(overlay.Length == 0 ? string.Empty : "." + overlay)}: at Information the hosting diagnostics " +
            "write every request's URL with its query string, so a picker's search term would reach " +
            "the container log, Seq and the log archive.");
    }

    private static string HostsDirectory => Path.Combine(AppContext.BaseDirectory, "hosts");

    private static List<string> LinkedOverlayNames() =>
        Directory.GetFiles(HostsDirectory, "Api.appsettings.*.json")
            .Select(f => Path.GetFileName(f)["Api.appsettings.".Length..^".json".Length])
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Jobbliggaren.sln")))
            directory = directory.Parent;
        directory.ShouldNotBeNull("the test runs from inside the repository");
        return directory!.FullName;
    }

    private static IConfigurationRoot BuildConfiguration(string overlay)
    {
        var builder = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(HostsDirectory, "Api.appsettings.json"), optional: false);
        if (overlay.Length > 0)
            builder.AddJsonFile(Path.Combine(HostsDirectory, $"Api.appsettings.{overlay}.json"), optional: false);
        return builder.Build();
    }

    private static ILogger BuildLogger(string overlay, string category)
    {
        var configuration = BuildConfiguration(overlay);

        var factory = LoggerFactory.Create(logging =>
        {
            logging.AddConfiguration(configuration.GetSection("Logging"));
            logging.AddProvider(new AlwaysEnabledProvider());
        });
        return factory.CreateLogger(category);
    }

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
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
            }
        }
    }
}
