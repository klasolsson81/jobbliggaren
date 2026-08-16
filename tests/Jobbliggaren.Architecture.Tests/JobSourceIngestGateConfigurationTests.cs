using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Configuration fitness function for the Platsbanken ingestion gate (#196).
///
/// <para>
/// Platsbanken ingestion writes recruiter contact records, which ADR 0050's pre-beta-data gate
/// B-1 covers. B-1 CLOSED 2026-08-16 and this guard stays, because B-1 was never the only gate.
/// The condition for loading is KLAS'S EXPLICIT WRITTEN GO — a decision, not a derivable state
/// (release-checklist.md §2.6 point 3.5). NO DISCHARGED GATE, TICKED BOX OR CLOSED ISSUE IS
/// PERMISSION: four state-shaped conditions each failed open on 2026-08-16. The unit tests
/// beside the two jobs prove the jobs OBEY the flag; they cannot prove the flag is off, because
/// they construct the options themselves. This file pins the two seams that actually decide it
/// on a running box, and both of them fail SILENTLY OPEN if they break:
/// </para>
/// <list type="number">
/// <item>the shipped Production overlay carries <c>JobTech:IngestEnabled=false</c> — delete the
/// key and the code default (<see langword="true"/>) takes over with no error;</item>
/// <item><c>AddJobSources</c> binds <see cref="JobSourceIngestOptions"/> against the JobTech
/// section — delete the binding and <c>IOptions&lt;T&gt;</c> still resolves, to a default
/// instance, with no DI failure and no log line.</item>
/// </list>
///
/// <para>
/// The assertions run the SHIPPED file through the real configuration engine rather than
/// string-matching the JSON — the file carries <c>//</c> comments, so a JSON parser would reject
/// it, and a text match could not tell a bound value from a commented-out one.
/// </para>
///
/// <para>
/// Naming: <c>&lt;ClassUnderTest&gt;_&lt;Scenario&gt;_&lt;Expected&gt;</c>.
/// </para>
/// </summary>
public class JobSourceIngestGateConfigurationTests
{
    private const string ShippedProductionOverlay = "hosts/Worker.appsettings.Production.json";

    /// <summary>The section both <c>JobTechOptions</c> and the Application-owned views bind to.</summary>
    private const string JobTechSection = "JobTech";

    private static IConfigurationRoot BuildFrom(string relativePath) =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, relativePath), optional: false)
            .Build();

    // --- Seam 1: the shipped overlay ---------------------------------------------------------

    [Fact]
    public void WorkerProductionOverlay_ShipsIngestDisabled()
    {
        var options = new JobSourceIngestOptions();
        BuildFrom(ShippedProductionOverlay).GetSection(JobTechSection).Bind(options);

        options.IngestEnabled.ShouldBeFalse(
            "the deployed Worker must not ingest recruiter contact records until Klas gives an " +
            "explicit written GO (release-checklist.md 2.6 point 3.5). That condition is a " +
            "DECISION, not a derivable state: do NOT read a discharged gate, a ticked box or a " +
            "closed issue as permission — four state-shaped conditions each failed open on " +
            "2026-08-16. Removing the key restores the code default, which is true.");
    }

    /// <summary>
    /// The vacuity guard for the test above. If the overlay could not be read at all — wrong
    /// link name, missing Content item, renamed file — <c>Bind</c> would leave the object at its
    /// code default. That default is <see langword="true"/>, so the assertion above would go RED
    /// rather than falsely green, which is the safe direction. This test pins the reason anyway:
    /// it proves the file is present and parsed, so a future default flip cannot turn the
    /// assertion above into a test of nothing.
    /// </summary>
    [Fact]
    public void WorkerProductionOverlay_IsActuallyReadable()
    {
        var config = BuildFrom(ShippedProductionOverlay);

        config.GetSection(JobTechSection).Exists().ShouldBeTrue();
        config[$"{JobTechSection}:IngestEnabled"].ShouldNotBeNull();
    }

    // --- Seam 2: the binding ----------------------------------------------------------------

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void AddJobSources_BindsIngestEnabled_FromTheJobTechSection(string configured, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{JobTechSection}:IngestEnabled"] = configured,
            })
            .Build();

        var provider = new ServiceCollection()
            .AddJobSources(configuration)
            .BuildServiceProvider();

        provider.GetRequiredService<IOptions<JobSourceIngestOptions>>()
            .Value.IngestEnabled.ShouldBe(expected);
    }
}
