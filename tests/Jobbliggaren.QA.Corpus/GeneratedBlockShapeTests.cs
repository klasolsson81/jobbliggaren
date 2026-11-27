using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.QA.Corpus.Generation;
using Shouldly;

namespace Jobbliggaren.QA.Corpus;

/// <summary>
/// The generator builds each experience entry the way the segmenter reads its block (AGENTS.md §5
/// `Tests:`): the actor that produces the state is callable here, so this runs it. The review reads
/// a staging entry through the segmenter's fields, so a block the segmenter reads differently makes
/// the findings corpus measure a CV production never produces. Adversarial stays out: its fields
/// are hostile by design.
/// </summary>
public class GeneratedBlockShapeTests
{
    private static readonly IReadOnlyList<OccupationGroundTruth> GroundTruth =
    [
        new("Mjukvaruutvecklare", "DJh5_yyF_hEM"),
        new("Advokat", "q8wL_kdi_WaW"),
        new("Snickare", "synthetic_grp_1"),
    ];

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
        var segmenter = new HeadingDrivenResumeSegmenter(CvParsingLexiconLoader.Load());
        var cases = new CorpusGenerator().GenerateCvCorpus(GroundTruth).Where(c => c.Stratum == stratum).ToList();
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
}
