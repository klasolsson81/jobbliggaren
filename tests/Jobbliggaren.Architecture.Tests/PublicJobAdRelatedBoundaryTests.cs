using System.Runtime.CompilerServices;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;
using Jobbliggaren.Application.JobAds.Queries.GetRelatedPublicJobAds;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The related ads' wire (ADR 0182). The web parses the answer with a strict envelope over the catalogue's strict row,
/// so a member the API adds or drops would leave the section out of every job page, and only in production: the smoke
/// runs against a stub that does not know the API. This holds the web's envelope to <see cref="RelatedPublicJobAdsDto"/>;
/// the row's keys are held to <see cref="PublicJobAdSummaryDto"/> by <see cref="PublicJobAdCatalogueBoundaryTests"/>.
/// </summary>
public class PublicJobAdRelatedBoundaryTests
{
    [Fact]
    public void TheWebEnvelopeIsTheStrictOneKeyListOfTheCataloguesRowSchema()
    {
        var schema = WebSource("src", "lib", "dto", "public-job-ad-related.ts");

        schema.ShouldContain("z.object({ items: z.array(publicJobAdSummarySchema) }).strict()",
            customMessage: "the envelope is no longer the strict one-key list of the catalogue's row; update this test with it.");
        schema.ShouldContain("import { publicJobAdSummarySchema } from \"./public-job-ad-catalogue\"",
            customMessage: "the row must be the catalogue's own schema, never a copy (ADR 0176 D5).");
        typeof(RelatedPublicJobAdsDto).GetProperties().Select(p => p.Name).ShouldBe(["Items"]);
        typeof(RelatedPublicJobAdsDto).GetProperty("Items")!.PropertyType
            .ShouldBe(typeof(IReadOnlyList<PublicJobAdSummaryDto>));
    }

    private static string WebSource(params string[] path) =>
        File.ReadAllText(Path.Combine([RepoRoot(), "web", "jobbliggaren-web", .. path]));

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/PublicJobAdRelatedBoundaryTests.cs → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
