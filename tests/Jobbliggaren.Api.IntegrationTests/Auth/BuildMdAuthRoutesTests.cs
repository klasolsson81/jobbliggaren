using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// ADR 0142 part 5a (#1743) — BUILD.md §6.2's <b>Auth</b> block lists exactly the routes the host maps under
/// <c>/api/v1/auth</c>, verb and path, in both directions.
/// </summary>
[Collection("Api")]
public partial class BuildMdAuthRoutesTests(ApiFactory factory)
{
    private const string AuthPrefix = "/api/v1/auth/";

    [Fact]
    public void The_Auth_block_lists_exactly_the_mapped_auth_routes()
    {
        var mapped = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (Path: "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'), Endpoint: e))
            .Where(r => r.Path.StartsWith(AuthPrefix, StringComparison.Ordinal))
            .SelectMany(r => (r.Endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(verb => $"{verb} {r.Path}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        mapped.ShouldNotBeEmpty();
        DocumentedAuthRoutes().Order(StringComparer.Ordinal).ShouldBe(mapped);
    }

    // Every bullet of the block, so a line that is not a route reads as one and fails the comparison.
    private static List<string> DocumentedAuthRoutes([CallerFilePath] string thisFile = "")
    {
        var lines = File.ReadAllLines(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", "BUILD.md")));
        var section = Array.FindIndex(lines, l => l.StartsWith("### 6.2 ", StringComparison.Ordinal));
        section.ShouldBeGreaterThanOrEqualTo(0, "BUILD.md has no §6.2");
        var block = Array.FindIndex(lines, section, l => l.Trim() == "**Auth**");
        block.ShouldBeGreaterThan(section, "BUILD.md §6.2 has no **Auth** block");

        var routes = lines
            .Skip(block + 1)
            .TakeWhile(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Select(l => RouteBullet().Match(l) is { Success: true } m ? $"{m.Groups[1].Value} {m.Groups[2].Value}" : l)
            .ToList();

        routes.ShouldNotBeEmpty();
        return routes;
    }

    [GeneratedRegex(@"^- `(GET|POST|PUT|PATCH|DELETE) (/api/v1/auth/[^`\s]+)`")]
    private static partial Regex RouteBullet();
}
