using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.Admin.ServiceObservations;
using Jobbliggaren.Application.Admin.ServiceObservations.Queries.GetServiceObservation;
using Jobbliggaren.Worker.Auditing;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The services sampler's boundaries (#1982, ADR 0159). The layer tests catch the direction of a dependency; this
/// pins three more things: Application's <c>ServiceObservations</c> knows nothing of Hangfire, a driver or the
/// readiness-check framework (the adapters for those live in the Api, where the types already do); the sampler and its
/// Api adapters are composed in the Api and nowhere else (the Worker has no admin surface and must not watch itself,
/// and <c>AddInfrastructure</c>/<c>AddPersistence</c> are loaded by it); and the Worker sets none of the Hangfire
/// server timings the 120 s alive window is derived from. The adapters are internal, so they are named by string.
/// </summary>
public class ServiceObservationBoundaryTests
{
    private const string Sampler = "Jobbliggaren.Application.Admin.ServiceObservations.ServiceObservationSampler";
    private const string WorkerSource = "Jobbliggaren.Api.BackgroundJobs.HangfireWorkerHeartbeatSource";
    private const string StoreProbe = "Jobbliggaren.Api.HealthChecks.ReadinessVolatileStoreProbe";
    private static readonly string[] Composed = [Sampler, WorkerSource, StoreProbe];

    [Fact]
    public void ServiceObservations_should_not_depend_on_hangfire_a_driver_or_the_health_check_framework()
    {
        var result = Types.InAssembly(typeof(ServiceObservationSampler).Assembly)
            .That().ResideInNamespaceStartingWith(typeof(ServiceObservationSampler).Namespace!)
            .ShouldNot().HaveDependencyOnAny(
                "Hangfire", "Npgsql", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions.Diagnostics.HealthChecks")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Application's service observation must stay free of its sources' frameworks: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Worker_should_not_reference_the_services_sampler_or_its_api_adapters()
    {
        var result = Types.InAssembly(typeof(WorkerSystemUser).Assembly)
            .ShouldNot().HaveDependencyOnAny(Composed)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"The services sampler is Api-only, the Worker references it: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Infrastructure_should_not_compose_the_services_sampler_or_its_api_adapters()
    {
        var result = Types.InAssembly(typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly)
            .ShouldNot().HaveDependencyOnAny(Composed)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"AddInfrastructure and AddPersistence are loaded by the Worker too: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_should_not_compose_the_services_sampler()
    {
        var result = Types.InAssembly(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .That().DoNotResideInNamespaceStartingWith(typeof(ServiceObservationSampler).Namespace!)
            .ShouldNot().HaveDependencyOnAny(Sampler)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"AddApplication is loaded by the Worker too: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    // The 120 s alive window is two of Hangfire's default heartbeat plus a sampling interval. A Worker that set its own
    // timings would make the window a guess, so the registration is read for any of them.
    [Theory]
    [InlineData("HeartbeatInterval")]
    [InlineData("ServerTimeout")]
    [InlineData("ServerCheckInterval")]
    public void Worker_should_set_none_of_the_hangfire_server_timings_the_alive_window_assumes(string option)
    {
        var program = File.ReadAllLines(Path.Combine(RepoRoot(), "src", "Jobbliggaren.Worker", "Program.cs"))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .ToList();

        program.ShouldContain(line => line.Contains("AddHangfireServer", StringComparison.Ordinal), "the registration this pin reads is gone");
        program.ShouldNotContain(
            line => Regex.IsMatch(line, $@"\b{option}\b"),
            $"the Worker sets {option}; the alive window of ServiceObservationLimits is derived from Hangfire's defaults");
    }

    /// <summary>
    /// The browser-facing schema spells the API's tokens and keys by hand, and a misspelt member makes the source fail its
    /// strict parse (the card reads "failed" for good) with every other suite green. The C# enums and the DTO are the source.
    /// </summary>
    [Fact]
    public void TheWebSchemaSpellsTheSameTokensAsTheEnums_OfTheServicesResponse()
    {
        var schema = File.ReadAllText(Path.Combine(RepoRoot(), "web", "jobbliggaren-web", "src", "lib", "dto", "admin-services.ts"));

        TokensOf(schema, "workerStateSchema").ShouldBe(Enum.GetNames<WorkerProcessState>().Order(StringComparer.Ordinal));
        TokensOf(schema, "volatileStoreStateSchema").ShouldBe(Enum.GetNames<VolatileStoreState>().Order(StringComparer.Ordinal));
        TokensOf(schema, "jobImportStateSchema").ShouldBe(Enum.GetNames<StreamImportState>().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheWebSchemaHasExactlyTheKeysOfTheDto_OnTheBodyAndOnEveryRow()
    {
        var schema = File.ReadAllText(Path.Combine(RepoRoot(), "web", "jobbliggaren-web", "src", "lib", "dto", "admin-services.ts"));

        KeysOf(schema, "servicesObservationSchema = z.strictObject").ShouldBe(WireNames(typeof(ServiceObservationDto)));
        KeysOf(schema, "backgroundJobsSchema = z").ShouldBe(WireNames(typeof(WorkerServiceDto)));
        KeysOf(schema, "volatileStoreSchema = z").ShouldBe(WireNames(typeof(VolatileStoreServiceDto)));
        KeysOf(schema, "jobImportSchema = z").ShouldBe(WireNames(typeof(JobImportServiceDto)));
    }

    private static IEnumerable<string> TokensOf(string schema, string name)
    {
        var declaration = Regex.Match(schema, $@"export const {name} = z\.enum\(\[(.*?)\]\)", RegexOptions.Singleline);
        declaration.Success.ShouldBeTrue($"{name} is no longer `z.enum([...])`; update this parse with it.");
        return Regex.Matches(declaration.Groups[1].Value, @"""(\w+)""").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal);
    }

    private static IEnumerable<string> KeysOf(string schema, string anchor)
    {
        var start = schema.IndexOf(anchor, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"`{anchor}` is gone from the web schema; update this parse with it.");
        var open = schema.IndexOf("({", start, StringComparison.Ordinal);
        var close = schema.IndexOf("})", open, StringComparison.Ordinal);
        return Regex.Matches(schema[(open + 2)..close], @"(?:^|[\s,])(\w+):\s").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal);
    }

    // The wire name is the property name in camel case, minus what the DTO keeps off the wire ([JsonIgnore]).
    private static IEnumerable<string> WireNames(Type dto) => dto.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
        .Where(p => p.GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: false).Length == 0)
        .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
        .Order(StringComparer.Ordinal);

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/ServiceObservationBoundaryTests.cs → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
