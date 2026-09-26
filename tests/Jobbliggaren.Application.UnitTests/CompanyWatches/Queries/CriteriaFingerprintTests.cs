using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #1681 part 2 (ADR 0139) — <see cref="CriteriaFingerprint"/>, the staleness guard's discriminator.
///
/// <para>
/// <b>Every assertion here is about the FUNCTION, never about what a stored criterion looks like.</b>
/// <c>Of</c> is a pure transform and <c>FromTrusted</c> is the production factory that builds a spec
/// from already-validated storage <i>verbatim</i> — its own docblock says so, and says why. So the
/// inputs below are ones a production entry point admits, and what is measured is how <c>Of</c> maps
/// them (CLAUDE.md §5 <c>Tests:</c> — "where that actor is callable in the test, the test asserts the
/// actor's own predicate or transform admits the state"). Nothing here claims that a criterion in the
/// database carries un-normalised codes.
/// </para>
///
/// <para>
/// <b>Why the canonicalisation needs its own pin.</b> <c>CompanyWatchCriteriaSpec.Create</c> sorts and
/// de-duplicates (pinned by <c>CompanyWatchCriteriaSpecTests.Create_NormalizesSniCodes_-
/// TrimDistinctSortedOrdinal</c>), so a fingerprint that trusted its input would look correct in every
/// end-to-end test. It fails in the direction that is hardest to see and worst to ship: a
/// reordered-but-identical predicate would produce a different digest, the read would report "not
/// materialised", and a working watch would silently lose its numbers. The guard's own docblock calls
/// that failing CLOSED in the annoying direction — so the reordering and duplication cases are pinned
/// here rather than inferred from the write path's normalisation.
/// </para>
/// </summary>
public class CriteriaFingerprintTests
{
    private static readonly string[] SniIt = ["62010", "62020"];
    private static readonly string[] KommunStockholmGoteborg = ["0180", "1480"];

    [Fact]
    public void Of_IsTheSameDigest_WhenTheAxesArriveInADifferentOrder()
    {
        // The canonical spec the create path writes...
        var canonical = CompanyWatchCriteriaSpec.Create(SniIt, KommunStockholmGoteborg).Value;

        // ...and the SAME predicate as FromTrusted would hand it over from a row whose arrays are in
        // some other order. FromTrusted copies its input verbatim (by design), so this is the input
        // the digest must be immune to.
        var reordered = CompanyWatchCriteriaSpec.FromTrusted(["62020", "62010"], ["1480", "0180"]);

        CriteriaFingerprint.Of(reordered).ShouldBe(CriteriaFingerprint.Of(canonical));
    }

    [Fact]
    public void Of_IsTheSameDigest_WhenAnAxisRepeatsACode()
    {
        var canonical = CompanyWatchCriteriaSpec.Create(SniIt, KommunStockholmGoteborg).Value;
        var duplicated = CompanyWatchCriteriaSpec.FromTrusted(
            ["62010", "62020", "62010"], ["0180", "1480", "1480"]);

        CriteriaFingerprint.Of(duplicated).ShouldBe(CriteriaFingerprint.Of(canonical));
    }

    [Fact]
    public void Of_IsADifferentDigest_WhenTheSniAxisChanges()
    {
        // The half the guard exists FOR — and the negative control for the two tests above, which on
        // their own would also pass under a constant digest.
        var before = CompanyWatchCriteriaSpec.Create(["62010"], ["0180"]).Value;
        var after = CompanyWatchCriteriaSpec.Create(["41200"], ["0180"]).Value;

        CriteriaFingerprint.Of(after).ShouldNotBe(CriteriaFingerprint.Of(before));
    }

    [Fact]
    public void Of_IsADifferentDigest_WhenTheMunicipalityAxisChanges()
    {
        // The other axis, separately: a digest that only hashed the SNI half would pass the test
        // above and let a user who moved a watch from Stockholm to Göteborg keep the old numbers.
        var before = CompanyWatchCriteriaSpec.Create(["62010"], ["0180"]).Value;
        var after = CompanyWatchCriteriaSpec.Create(["62010"], ["1480"]).Value;

        CriteriaFingerprint.Of(after).ShouldNotBe(CriteriaFingerprint.Of(before));
    }

    [Fact]
    public void Of_IsADifferentDigest_WhenAnAxisIsNarrowed()
    {
        // Narrowing is the ordinary edit (the materialisation suite's
        // Materialise_AfterTheCriterionIsNarrowed_DeletesTheOldSet_NeverSupplementsIt runs the same
        // transition through the aggregate). A digest that ignored cardinality would carry the wide
        // predicate's member set over onto the narrow one.
        var wide = CompanyWatchCriteriaSpec.Create(SniIt, KommunStockholmGoteborg).Value;
        var narrow = CompanyWatchCriteriaSpec.Create(SniIt, ["0180"]).Value;

        CriteriaFingerprint.Of(narrow).ShouldNotBe(CriteriaFingerprint.Of(wide));
    }

    [Fact]
    public void Of_CannotBeCollided_ByMovingACodeBetweenTheTwoAxes()
    {
        // The separator's job, stated as the property it buys. Concatenating the two axes without a
        // delimiter that cannot occur in a code — or without the per-axis counts — would make these
        // two predicates hash the same input, and one criterion would then read the other's members.
        //
        // Both operands come from FromTrusted, whose contract is to copy without validating; the
        // claim is about Of's INPUT ENCODING, not about a criterion the register could produce.
        var sniSide = CompanyWatchCriteriaSpec.FromTrusted(["62010", "0180"], ["1480"]);
        var kommunSide = CompanyWatchCriteriaSpec.FromTrusted(["62010"], ["0180", "1480"]);

        CriteriaFingerprint.Of(sniSide).ShouldNotBe(CriteriaFingerprint.Of(kommunSide));
    }

    [Fact]
    public void Of_CannotBeCollided_ByRegroupingCodesWithinAnAxis()
    {
        // The delimiter's other half. ["620", "10"] and ["62010"] are one string once joined, so a
        // digest over a bare concatenation would map them together — and the axis lengths alone would
        // not separate them either, since both axes would then carry two elements against one.
        var split = CompanyWatchCriteriaSpec.FromTrusted(["620", "10"], ["0180"]);
        var whole = CompanyWatchCriteriaSpec.FromTrusted(["62010"], ["0180"]);

        CriteriaFingerprint.Of(split).ShouldNotBe(CriteriaFingerprint.Of(whole));
    }

    [Fact]
    public void Of_IsDeterministic_AcrossCalls()
    {
        // Both sides of the guard compute this value independently — the writer stamps it, the reader
        // compares against it — so a digest that varied per call (a hash seed, a GUID, a timestamp)
        // would blank every number on every read while every other test stayed green.
        var spec = CompanyWatchCriteriaSpec.Create(SniIt, KommunStockholmGoteborg).Value;

        CriteriaFingerprint.Of(spec).ShouldBe(CriteriaFingerprint.Of(spec));
    }

    [Fact]
    public void Of_ProducesTheStoredColumnsExactWidth_InLowerCaseHex()
    {
        // The column is varchar(64) and CriteriaFingerprint.Length single-sources it. A digest wider
        // than the column would throw on write; one that emitted upper-case hex would compare unequal
        // to itself across a round trip, because the read compares with StringComparison.Ordinal.
        var value = CriteriaFingerprint.Of(
            CompanyWatchCriteriaSpec.Create(SniIt, KommunStockholmGoteborg).Value).Value;

        value.Length.ShouldBe(CriteriaFingerprint.Length);
        value.ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Of_NeverProducesTheMigrationsBackfillValue()
    {
        // THE property the migration's backfill rests on, asserted where it can fail.
        // AddCriterionMaterialisationFingerprint backfills pre-existing rows with '' and argues that
        // this is safe because "an empty string is a value CriteriaFingerprint.Of can never produce".
        // That sentence is about THIS function, and nothing else measured it: a row written before the
        // column existed must degrade to "not materialised", never match a live predicate.
        var spec = CompanyWatchCriteriaSpec.Create(["62010"], ["0180"]).Value;

        CriteriaFingerprint.Of(spec).Value.ShouldNotBe(string.Empty);
        CriteriaFingerprint.Of(spec).ShouldNotBe(CriteriaFingerprint.FromTrusted(string.Empty));
    }

    [Fact]
    public void FromTrusted_RoundTripsAComputedDigest()
    {
        // The read side rebuilds the stored text through FromTrusted and compares by value; a
        // fingerprint that did not compare equal after a round trip would make every read stale.
        var computed = CriteriaFingerprint.Of(
            CompanyWatchCriteriaSpec.Create(SniIt, KommunStockholmGoteborg).Value);

        CriteriaFingerprint.FromTrusted(computed.Value).ShouldBe(computed);
    }

    [Fact]
    public void Of_Throws_WhenTheSpecIsNull()
    {
        Should.Throw<ArgumentNullException>(() => CriteriaFingerprint.Of(null!));
    }
}
