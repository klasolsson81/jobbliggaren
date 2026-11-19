using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The open junior IT list's wire (ADR 0186). The web parses each answer with strict schemas, so a member the API adds or
/// drops would answer every <c>/junior</c> view with a 500, and only in production. These hold the web's keys equal to
/// the API's: the row to <see cref="PublicJuniorItJobAdDto"/>, the kommun option to <see cref="PublicJuniorItPlaceDto"/>,
/// the envelope to <see cref="PublicJuniorItJobAdsPage.Available"/>, and the page to <see cref="PagedResult{T}"/>.
/// </summary>
public class PublicJuniorItJobAdsBoundaryTests
{
    private static readonly string[] SchemaPath = ["src", "lib", "dto", "public-junior-it-job-ads.ts"];

    [Fact]
    public void TheWebRowSchemaHasExactlyTheKeysOfTheRow()
    {
        ObjectKeysAfter(WebSource(SchemaPath), "publicJuniorItJobAdSchema = z").ShouldBe(
            CamelCased(typeof(PublicJuniorItJobAdDto).GetProperties().Select(p => p.Name)), ignoreOrder: true);
    }

    [Fact]
    public void TheWebPlaceSchemaHasExactlyTheKeysOfTheOption()
    {
        ObjectKeysAfter(WebSource(SchemaPath), "publicJuniorItPlaceSchema = z").ShouldBe(
            CamelCased(typeof(PublicJuniorItPlaceDto).GetProperties().Select(p => p.Name)), ignoreOrder: true);
    }

    [Fact]
    public void TheWebEnvelopeSchemaHasExactlyTheKeysOfTheAvailableAnswer_AndItsRowsAreTheStrictFiveKeyPagedResult()
    {
        var schema = WebSource(SchemaPath);
        ObjectKeysAfter(schema, "publicJuniorItJobAdsPageSchema = z").ShouldBe(
            CamelCased(typeof(PublicJuniorItJobAdsPage.Available).GetProperties()
                .Where(p => p.Name != "EqualityContract")
                .Select(p => p.Name)),
            ignoreOrder: true);
        schema.ShouldContain("pagedResultWithTotalPages(publicJuniorItJobAdSchema).strict()",
            customMessage: "the page schema is no longer the strict five-key form; update this test with it.");

        ObjectKeysAfter(WebSource("src", "lib", "dto", "_helpers.ts"), "export function pagedResultWithTotalPages").ShouldBe(
            CamelCased(typeof(PagedResult<PublicJuniorItJobAdDto>).GetProperties().Select(p => p.Name)), ignoreOrder: true);
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

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/PublicJuniorItJobAdsBoundaryTests.cs → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
