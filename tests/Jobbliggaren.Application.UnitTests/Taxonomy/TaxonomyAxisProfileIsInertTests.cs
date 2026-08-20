using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Domain.Privacy;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Taxonomy;

/// <summary>
/// The taxonomy-axis personnummer guard (#1419) passes
/// <see cref="PersonnummerGapProfile.SingleLineUserInput"/>, and that choice is currently
/// BEHAVIOURALLY INERT: the conceptId grammar admits no whitespace and no control character, so
/// no gapped form can reach the axis and both profiles answer identically on every value the
/// grammar lets through.
///
/// <para><b>Why pin an inertness.</b> The guard's comment says the profile is inert, and an
/// unpinned claim of that shape is the kind that stops being true without anyone noticing: relax
/// the grammar to admit a space — a plausible future change, since a human types
/// "811218 9876" — and the two profiles diverge on exactly the class the guard exists for. This
/// test turns that from a silent change of reach into a failing build.</para>
///
/// <para>It does NOT assert that the profile choice is arbitrary. It is not: the value is a
/// single line out of a hand-editable URL, so <c>SingleLineUserInput</c> is the correct policy
/// on the day the grammar relaxes. Inert today, right tomorrow.</para>
/// </summary>
public sealed class TaxonomyAxisProfileIsInertTests
{
    // Drives the PRODUCTION gate rather than a copy of its pattern. An earlier revision of this
    // file carried its own compiled literal, which made the whole point of the file undeliverable:
    // relax the production grammar and every row here stays green, because the local copy still
    // rejects the same things. TaxonomyConceptIdGrammarTests already solved this one directory
    // over, by validating instead of copying (ConceptIdPattern is a private const, so validating
    // IS the route).
    private static bool GrammarAdmits(string conceptId) =>
        new ListJobAdsQueryValidator()
            .Validate(new ListJobAdsQuery(OccupationGroup: [conceptId]))
            .IsValid;

    [Theory]
    [InlineData("8112189876")]      // contiguous personnummer — flagged by both
    [InlineData("811218-9876")]     // hyphenated — flagged by both
    [InlineData("198112189876")]    // 12-digit — flagged by both
    [InlineData("811278-9873")]     // samordningsnummer — flagged by both
    [InlineData("8112189875")]      // Luhn-invalid — flagged by neither
    [InlineData("5592804784")]      // real org.nr — flagged by neither
    [InlineData("2512")]            // ordinary taxonomy-shaped id
    [InlineData("SSYK_8112")]       // underscore form
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")] // 32 chars, the cap
    public void BothProfilesAnswerIdentically_ForEveryValueTheGrammarAdmits(string conceptId)
    {
        GrammarAdmits(conceptId).ShouldBeTrue(
            $"the vector must be grammar-admissible, else it measures a value this axis cannot carry: {conceptId}");

        var narrow = PersonnummerScanner.Scan(
            PersonnummerTextNormalizer.Normalize(conceptId, PersonnummerGapProfile.ExtractedDocumentText)).Count;
        var wide = PersonnummerScanner.Scan(
            PersonnummerTextNormalizer.Normalize(conceptId, PersonnummerGapProfile.SingleLineUserInput)).Count;

        wide.ShouldBe(
            narrow,
            "the two profiles differ ONLY in their gap term, and both gap classes are disjoint " +
            "from the conceptId charset, so on any grammar-admissible value they are equivalent. " +
            "A divergence here therefore means a profile or the normalizer changed, NOT that the " +
            $"grammar relaxed — that shows up on the GrammarAdmits assertions. Vector: {conceptId}");
    }

    [Fact]
    public void TheInertnessIsAPropertyOfTheGrammar_NotOfTheProfiles()
    {
        // Non-vacuity, and it is the whole point: the two profiles DO differ — just not on
        // anything this axis can carry. Without this row the theory above would keep passing if
        // both profiles collapsed into one, which is a real regression it must not hide.
        const string gapped = "811218   9876";
        GrammarAdmits(gapped).ShouldBeFalse(
            "the production validator is what keeps this form off the axis; if it starts admitting " +
            "a gapped value, the profile choice below stops being inert and this guard's reach changes");

        PersonnummerScanner.Scan(
            PersonnummerTextNormalizer.Normalize(gapped, PersonnummerGapProfile.ExtractedDocumentText))
            .ShouldBeEmpty();
        PersonnummerScanner.Scan(
            PersonnummerTextNormalizer.Normalize(gapped, PersonnummerGapProfile.SingleLineUserInput))
            .ShouldHaveSingleItem();
    }
}
