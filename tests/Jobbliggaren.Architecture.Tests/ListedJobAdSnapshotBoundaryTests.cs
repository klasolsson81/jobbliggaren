using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.JobAds.Listing;
using Jobbliggaren.Application.JobAds.Queries.GetListedJobAdIds;
using Jobbliggaren.Worker.Auditing;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The listed-ad snapshot's boundaries (ADR 0174). The layer tests catch the direction of a dependency; these pin two
/// more things. The sampler is composed by the Api and nowhere else: <c>AddPersistence</c> and <c>AddApplication</c>
/// are loaded by the Worker too, where it would read the whole listed set every five minutes for no reader. The rule
/// names the type and not the namespace, because the Worker's generated Mediator code legitimately references the
/// query handler. And the web, which owns the sitemap's files, agrees with the API on the part count and the wire.
/// A drift there would surface only in production, as every part answering 503.
/// </summary>
public class ListedJobAdSnapshotBoundaryTests
{
    private static readonly string Sampler = typeof(ListedJobAdSnapshotSampler).FullName!;

    [Fact]
    public void Worker_should_not_reference_the_listed_ad_sampler()
    {
        var result = Types.InAssembly(typeof(WorkerSystemUser).Assembly)
            .ShouldNot().HaveDependencyOn(Sampler)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"The listed-ad sampler is Api-only, the Worker references it: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Infrastructure_should_not_compose_the_listed_ad_sampler()
    {
        var result = Types.InAssembly(typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly)
            .ShouldNot().HaveDependencyOn(Sampler)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"AddInfrastructure and AddPersistence are loaded by the Worker too: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_should_not_compose_the_listed_ad_sampler_outside_its_own_namespace()
    {
        var result = Types.InAssembly(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .That().DoNotResideInNamespaceStartingWith(typeof(ListedJobAdSnapshotSampler).Namespace!)
            .ShouldNot().HaveDependencyOn(Sampler)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"AddApplication is loaded by the Worker too: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void TheWebsPartCount_IsTheApis_AndDividesTheHexDigitsEvenly()
    {
        var module = File.ReadAllText(Path.Combine(RepoRoot(), "web", "jobbliggaren-web", "src", "lib", "seo", "job-sitemap.ts"));
        var declaration = Regex.Match(module, @"export const JOB_SITEMAP_PART_COUNT = (\d+);");
        declaration.Success.ShouldBeTrue("JOB_SITEMAP_PART_COUNT is no longer a literal in job-sitemap.ts; update this parse with it.");

        var webPartCount = int.Parse(declaration.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        webPartCount.ShouldBe(ListedJobAdSnapshotLimits.PartCount,
            "the API refuses any other part count with a 400, which the web turns into a 503 on every part.");
        ListedJobAdPartition.IsSupported(webPartCount).ShouldBeTrue();
    }

    [Fact]
    public void TheWebSchemaHasExactlyTheKeysOfTheWire()
    {
        var schema = File.ReadAllText(Path.Combine(RepoRoot(), "web", "jobbliggaren-web", "src", "lib", "dto", "public-job-ad-ids.ts"));
        var start = schema.IndexOf("publicJobAdIdsSchema = z", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, "`publicJobAdIdsSchema = z` is gone from the web schema; update this parse with it.");
        var open = schema.IndexOf("({", start, StringComparison.Ordinal);
        var close = schema.IndexOf("})", open, StringComparison.Ordinal);
        var keys = Regex.Matches(schema[(open + 2)..close], @"(?:^|[\s,])(\w+):\s").Select(m => m.Groups[1].Value);

        keys.ShouldBe(typeof(ListedJobAdIdsDto).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]),
            ignoreOrder: true);
    }

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/ListedJobAdSnapshotBoundaryTests.cs → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
