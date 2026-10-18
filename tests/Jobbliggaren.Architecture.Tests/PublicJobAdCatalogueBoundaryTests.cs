using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The public catalogue's wire (ADR 0176). The web parses each page with strict schemas, so a member the API adds or
/// drops would answer every catalogue page with a 500, and only in production: the smoke runs against a stub that does
/// not know the API. These hold the web's keys equal to the API's: the row to <see cref="PublicJobAdSummaryDto"/>, and
/// the page to the five members <see cref="PagedResult{T}"/> serialises.
/// </summary>
public class PublicJobAdCatalogueBoundaryTests
{
    [Fact]
    public void TheWebRowSchemaHasExactlyTheKeysOfTheRow()
    {
        var schema = WebSource("src", "lib", "dto", "public-job-ad-catalogue.ts");

        ObjectKeysAfter(schema, "publicJobAdSummarySchema = z").ShouldBe(
            CamelCased(typeof(PublicJobAdSummaryDto).GetProperties().Select(p => p.Name)), ignoreOrder: true);
    }

    [Fact]
    public void TheWebPageSchemaIsTheStrictFiveKeyPagedResult()
    {
        var schema = WebSource("src", "lib", "dto", "public-job-ad-catalogue.ts");
        schema.ShouldContain("pagedResultWithTotalPages(publicJobAdSummarySchema).strict()",
            customMessage: "the page schema is no longer the strict five-key form; update this test with it.");

        var helpers = WebSource("src", "lib", "dto", "_helpers.ts");
        ObjectKeysAfter(helpers, "export function pagedResultWithTotalPages").ShouldBe(
            CamelCased(typeof(PagedResult<PublicJobAdSummaryDto>).GetProperties().Select(p => p.Name)), ignoreOrder: true);
    }

    private static IEnumerable<string> ObjectKeysAfter(string source, string anchor)
    {
        var start = source.IndexOf(anchor, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"`{anchor}` is gone from the web source; update this parse with it.");
        var open = source.IndexOf("({", start, StringComparison.Ordinal);
        var close = source.IndexOf("})", open, StringComparison.Ordinal);
        return Regex.Matches(source[(open + 2)..close], @"(?:^|[\s,])(\w+):\s").Select(m => m.Groups[1].Value);
    }

    private static IEnumerable<string> CamelCased(IEnumerable<string> names) =>
        names.Select(n => char.ToLowerInvariant(n[0]) + n[1..]);

    private static string WebSource(params string[] path) =>
        File.ReadAllText(Path.Combine([RepoRoot(), "web", "jobbliggaren-web", .. path]));

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/PublicJobAdCatalogueBoundaryTests.cs → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
