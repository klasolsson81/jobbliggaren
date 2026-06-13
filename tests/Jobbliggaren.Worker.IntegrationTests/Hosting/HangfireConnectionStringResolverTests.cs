using Jobbliggaren.Worker.Hosting;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Hosting;

/// <summary>
/// Unit-test för <see cref="HangfireConnectionStringResolver"/>. TD-17 punkt 4 —
/// fallback-kedja HangfireStorage → Postgres så prod kan splitta access-yta utan
/// att dev/test behöver konfig-overhead.
///
/// Placering i Worker.IntegrationTests-projektet är pragmatisk (samma rationale
/// som <see cref="HangfireWorkerOptionsTests"/> — separat Worker.UnitTests
/// existerar inte ännu).
/// </summary>
public class HangfireConnectionStringResolverTests
{
    private const string HangfireConnString = "Host=hangfire-host;Database=jobbliggaren;Username=jobbliggaren_worker;Password=h";
    private const string PostgresConnString = "Host=app-host;Database=jobbliggaren;Username=jobbliggaren_app;Password=p";

    [Fact]
    public void Resolve_PrefersHangfireStorage_OverPostgres()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:HangfireStorage"] = HangfireConnString,
            ["ConnectionStrings:Postgres"] = PostgresConnString
        });

        var resolved = HangfireConnectionStringResolver.Resolve(config);

        resolved.ShouldBe(HangfireConnString, "Prod-prefix routar Worker till jobbliggaren_worker-rollen");
    }

    [Fact]
    public void Resolve_FallsBackToPostgres_WhenHangfireStorageMissing()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = PostgresConnString
        });

        var resolved = HangfireConnectionStringResolver.Resolve(config);

        resolved.ShouldBe(PostgresConnString, "Dev/test använder en sanning utan split-overhead");
    }

    [Fact]
    public void Resolve_ThrowsInvalidOperation_WhenBothMissing()
    {
        var config = BuildConfig(new Dictionary<string, string?>());

        var ex = Should.Throw<InvalidOperationException>(() =>
            HangfireConnectionStringResolver.Resolve(config));

        ex.Message.ShouldContain("HangfireStorage");
        ex.Message.ShouldContain("Postgres");
    }

    [Fact]
    public void Resolve_ThrowsArgumentNullException_WhenConfigurationNull()
    {
        Should.Throw<ArgumentNullException>(() =>
            HangfireConnectionStringResolver.Resolve(null!));
    }

    [Fact]
    public void Keys_AreStableConstants()
    {
        // Klient-kontrakt — runbook-dokumentation refererar konstant-namnen.
        HangfireConnectionStringResolver.PrimaryKey.ShouldBe("HangfireStorage");
        HangfireConnectionStringResolver.FallbackKey.ShouldBe("Postgres");
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
