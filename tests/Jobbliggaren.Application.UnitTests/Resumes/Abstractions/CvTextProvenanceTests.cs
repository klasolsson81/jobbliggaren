using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Application.UnitTests.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Abstractions;

/// <summary>#2353: the extractor's per-line geometry, indexed as RawText's lines.</summary>
public class CvTextProvenanceTests
{
    [Fact]
    public void For_RefusesALineCountThatDiffersFromTheText() =>
        Should.Throw<ArgumentException>(() =>
            CvTextProvenance.For("Rad ett\nRad två", [CvLineProvenance.Unmeasured]));

    [Fact]
    public void For_IndexesOneEntryPerLine()
    {
        var joined = new CvLineProvenance(FirstSegmentLength: 84, Left: 90.0, SpaceWidth: 3.05);

        var provenance = CvTextProvenance.For("Rad ett\nRad två", [CvLineProvenance.Unmeasured, joined]);

        provenance.LineCount.ShouldBe(2);
        provenance[0].Measured.ShouldBeFalse();
        provenance[1].ShouldBe(joined);
        provenance[1].Joined.ShouldBeTrue();
    }

    [Fact]
    public void None_IsUnmeasuredOnEveryLine()
    {
        CvTextProvenance.None.LineCount.ShouldBe(0);
        CvTextProvenance.None[0].ShouldBe(CvLineProvenance.Unmeasured);
        CvTextProvenance.None[7].Joined.ShouldBeFalse();
    }

    [Fact]
    public void AMeasuredLineThatHoldsOnePhysicalLine_IsMeasuredButNotJoined()
    {
        var line = new CvLineProvenance(FirstSegmentLength: null, Left: 72.0, SpaceWidth: 3.05);

        line.Measured.ShouldBeTrue();
        line.Joined.ShouldBeFalse();
    }

    [Fact]
    public void Segment_IgnoresAProvenanceThatDoesNotIndexTheTextsLines()
    {
        // Declared unreachable: the extractor builds the provenance from the same text (For checks the
        // count), and the import hands both on unchanged. If that invariant breaks, the segmenter reads
        // the text as if nothing was measured.
        var sut = CvParsingLexiconFixture.Segmenter();
        const string text = "Anna Andersson\nanna@example.com\n\nProfil\nTestare med tio års erfarenhet.";
        var foreign = CvTextProvenance.For("En rad", [new CvLineProvenance(12, 90.0, 3.05)]);

        var withForeign = sut.Segment(text, foreign);
        var withNone = sut.Segment(text);

        withForeign.Content.Profile.ShouldBe(withNone.Content.Profile);
        withForeign.Content.Contact.ShouldBe(withNone.Content.Contact);
    }
}
