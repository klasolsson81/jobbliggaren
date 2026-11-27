using System.Text.Json;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.QA.Corpus.Generation;
using Shouldly;

namespace Jobbliggaren.QA.Corpus;

/// <summary>
/// The generator builds each experience entry the way the segmenter reads its block (AGENTS.md §5
/// `Tests:`): the actor that produces the state is callable here, so this runs it. The review reads
/// a staging entry through the segmenter's fields, so a block the segmenter reads differently makes
/// the findings corpus measure a CV production never produces. It runs on the corpus's own ground
/// truth, read from the same embedded taxonomy and frozen map the findings report reads through its
/// database. Adversarial stays out: its fields are hostile by design.
/// </summary>
public class GeneratedBlockShapeTests
{
    private const string FrozenMapResource =
        "Jobbliggaren.Infrastructure.Persistence.Migrations.Resources." +
        "occupation-name-to-ssyk-level-4.v30.json";

    public static TheoryData<CorpusStratum> StrataWithADescription() =>
    [
        CorpusStratum.CleanExactTitle,
        CorpusStratum.InflectedTitle,
        CorpusStratum.NonStandardOrEnglishTitle,
        CorpusStratum.NewlyArrived,
        CorpusStratum.FakePersonnummer,
    ];

    [Theory]
    [MemberData(nameof(StrataWithADescription))]
    public void EveryGeneratedEntry_IsTheEntryTheSegmenterReadsFromItsBlock(CorpusStratum stratum)
    {
        var segmenter = new HeadingDrivenResumeSegmenter(CvParsingLexiconLoader.Load(), CvPlaceLexiconLoader.Load());
        var cases = new CorpusGenerator().GenerateCvCorpus(CorpusGroundTruth()).Where(c => c.Stratum == stratum).ToList();
        cases.ShouldNotBeEmpty();

        foreach (var generated in cases)
        {
            var entry = generated.Cv.Content.Experience.ShouldHaveSingleItem();
            var heading = generated.Cv.DetectedLanguage == ResumeLanguage.En ? "Experience" : "Erfarenhet";

            var read = segmenter.Segment($"{heading}\n{entry.RawText}").Content.Experience.ShouldHaveSingleItem();

            read.Title.ShouldBe(entry.Title, $"{stratum} #{generated.Index}");
            read.Organization.ShouldBe(entry.Organization, $"{stratum} #{generated.Index}");
            read.Period.ShouldBe(entry.Period, $"{stratum} #{generated.Index}");
        }
    }

    // The pairs CorpusFindingsReportTests builds from the seeded database, read from the embedded
    // files that seed it.
    private static List<OccupationGroundTruth> CorpusGroundTruth()
    {
        var snapshot = TaxonomySnapshotLoader.Load();
        var occLabelById = snapshot.OccupationFields.SelectMany(f => f.Occupations)
            .GroupBy(o => o.ConceptId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Label, StringComparer.Ordinal);
        var groupIds = snapshot.OccupationFields.SelectMany(f => f.OccupationGroups ?? [])
            .Select(g => g.ConceptId).ToHashSet(StringComparer.Ordinal);

        using var stream = typeof(TaxonomyReadModel).Assembly.GetManifestResourceStream(FrozenMapResource)!;
        using var doc = JsonDocument.Parse(stream);
        var pairs = new List<OccupationGroundTruth>();
        foreach (var entry in doc.RootElement.GetProperty("mappings").EnumerateObject())
        {
            var groupId = entry.Value.GetString()!;
            if (occLabelById.TryGetValue(entry.Name, out var label) && groupIds.Contains(groupId))
            {
                pairs.Add(new OccupationGroundTruth(label, groupId));
            }
        }

        pairs.Count.ShouldBeGreaterThan(1000, "premise: the corpus's ground truth, not a sample");
        return pairs;
    }
}
