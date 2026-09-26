using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #1681 part 2 (ADR 0139) — the three result types <see cref="ICompanyWatchBrowseQuery"/>'s ad half
/// answers with, and the constructor guards that make their impossible combinations
/// UNREPRESENTABLE rather than merely undesirable.
///
/// <para>
/// <b>Each guard is a claim its own docblock makes in prose, and prose cannot fail.</b> Deleting any
/// one of the three <c>throw</c> expressions leaves every handler, resolver and endpoint test in this
/// repo green — the production code never constructs a contradictory value, which is exactly why the
/// guard has no other oracle. This is the same form <c>CompanyBrowseCriteriaTests</c> exists in, and
/// for the same measured reason: the fix for "a guarantee nobody tests" is itself a guarantee nobody
/// tests until someone tries the lock.
/// </para>
///
/// <para>
/// <b>Calling a guard with the input it exists to reject is not a §5 premise problem</b> (CLAUDE.md
/// §5 <c>Tests:</c>): the actor is named and callable — it IS the constructor — and what is asserted
/// is that actor's own predicate, never a fact about what production emits. The negative controls
/// matter as much as the throws: an UNCONDITIONAL guard would satisfy a throw-only assertion, so
/// every case below pins the admitted shape beside the rejected one.
/// </para>
/// </summary>
public class MaterialisedAdResultTests
{
    // ----- MaterialisedAdCount -------------------------------------------------------------------

    [Fact]
    public void AdCount_Counted_CarriesTheNumber_AndZeroIsARealAnswer()
    {
        // The negative control for the two rejections below, and a claim in its own right: zero
        // active ads is an ANSWER under Materialised, not an absence.
        var zero = MaterialisedAdCount.Counted(0, saturated: false);

        zero.State.ShouldBe(CriterionMaterialisationState.Materialised);
        zero.Count.ShouldBe(0);
        zero.Saturated.ShouldBeFalse();

        MaterialisedAdCount.Counted(167, saturated: true).Saturated.ShouldBeTrue();
    }

    [Fact]
    public void AdCount_Rejects_ANumberBesideARefusal()
    {
        // A count next to a refusal is a figure the port has no coverage for — the "floor wearing a
        // magnitude's clothes" the whole ad half is written against. Both refusal states, separately:
        // a guard keyed on only one of them would let the other through.
        Should.Throw<ArgumentException>(() =>
            new MaterialisedAdCount(CriterionMaterialisationState.TooBroad, 5, Saturated: false));

        Should.Throw<ArgumentException>(() =>
            new MaterialisedAdCount(
                CriterionMaterialisationState.NotMaterialised, 0, Saturated: false));
    }

    [Fact]
    public void AdCount_Rejects_MaterialisedWithoutANumber()
    {
        // The other direction: a measurement that was taken and then thrown away. Without this arm a
        // read that lost its scalar would surface as an absent number, i.e. as ignorance — which the
        // surface renders as "vi har inte räknat än" about a criterion that WAS counted.
        Should.Throw<ArgumentException>(() =>
            new MaterialisedAdCount(
                CriterionMaterialisationState.Materialised, null, Saturated: false));
    }

    [Fact]
    public void AdCount_TooBroadAndNotMaterialised_AreTwoDifferentAnswers_NeitherOfThemAZero()
    {
        // The distinction ADR 0139 alternative 4 was rejected for collapsing. They render different
        // copy and offer different actions: one is narrowed by the user, the other resolves itself on
        // the next run.
        MaterialisedAdCount.TooBroad.State.ShouldBe(CriterionMaterialisationState.TooBroad);
        MaterialisedAdCount.NotMaterialised.State
            .ShouldBe(CriterionMaterialisationState.NotMaterialised);

        MaterialisedAdCount.TooBroad.ShouldNotBe(MaterialisedAdCount.NotMaterialised);

        MaterialisedAdCount.TooBroad.Count.ShouldBeNull();
        MaterialisedAdCount.NotMaterialised.Count.ShouldBeNull();
        MaterialisedAdCount.TooBroad.Saturated.ShouldBeFalse();
        MaterialisedAdCount.NotMaterialised.Saturated.ShouldBeFalse();
    }

    // ----- MaterialisedAdIds ---------------------------------------------------------------------

    [Fact]
    public void AdIds_Resolved_CarriesTheSet_AndAnEmptySetIsARealAnswer()
    {
        var ad = new JobAdId(Guid.NewGuid());

        var one = MaterialisedAdIds.Resolved([ad]);
        one.State.ShouldBe(CriterionMaterialisationState.Materialised);
        one.Refused.ShouldBeFalse();
        one.Ids.ShouldBe([ad]);

        // Empty is a LIST, not a null: "no ads match this criterion" is an answer, and the refusal is
        // a different member of this type entirely.
        var empty = MaterialisedAdIds.Resolved([]);
        empty.Ids.ShouldNotBeNull();
        empty.Ids.ShouldBeEmpty();
        empty.Refused.ShouldBeFalse();
    }

    [Fact]
    public void AdIds_Rejects_ASetBesideARefusal()
    {
        // A prefix of a refused set is the exact defect ListActiveAdIdsAsync's LIMIT + 1 shape exists
        // to make unreachable at the database. This guard is the same property at the type level: no
        // value can carry both a refusal and rows to count.
        var ad = new JobAdId(Guid.NewGuid());

        Should.Throw<ArgumentException>(() =>
            new MaterialisedAdIds(CriterionMaterialisationState.Materialised, [ad], Refused: true));

        Should.Throw<ArgumentException>(() =>
            new MaterialisedAdIds(CriterionMaterialisationState.TooBroad, [ad], Refused: false));

        Should.Throw<ArgumentException>(() =>
            new MaterialisedAdIds(
                CriterionMaterialisationState.NotMaterialised, [], Refused: false));
    }

    [Fact]
    public void AdIds_Rejects_AnAnswerableQuestionWithNoSet()
    {
        Should.Throw<ArgumentException>(() =>
            new MaterialisedAdIds(
                CriterionMaterialisationState.Materialised, null, Refused: false));
    }

    [Fact]
    public void AdIds_TooManyAds_IsARefusalOfAMaterialisedCriterion_NotAMaterialisationFailure()
    {
        // The distinction the port's docblock makes and the read side must not infer away: TooManyAds
        // says THIS QUESTION's bound was exceeded by a criterion that WAS materialised, while
        // TooBroad says the criterion's company set never was. Three distinct values, no two equal.
        MaterialisedAdIds.TooManyAds.State.ShouldBe(CriterionMaterialisationState.Materialised);
        MaterialisedAdIds.TooManyAds.Refused.ShouldBeTrue();
        MaterialisedAdIds.TooManyAds.Ids.ShouldBeNull();

        MaterialisedAdIds.TooBroad.State.ShouldBe(CriterionMaterialisationState.TooBroad);
        MaterialisedAdIds.TooBroad.Refused.ShouldBeFalse();

        MaterialisedAdIds.NotMaterialised.State
            .ShouldBe(CriterionMaterialisationState.NotMaterialised);
        MaterialisedAdIds.NotMaterialised.Refused.ShouldBeFalse();

        MaterialisedAdIds.TooManyAds.ShouldNotBe(MaterialisedAdIds.TooBroad);
        MaterialisedAdIds.TooBroad.ShouldNotBe(MaterialisedAdIds.NotMaterialised);
        MaterialisedAdIds.TooManyAds.ShouldNotBe(MaterialisedAdIds.NotMaterialised);
    }

    // ----- MaterialisedAdPage --------------------------------------------------------------------

    [Fact]
    public void AdPage_Resolved_CarriesThePage_AndAnEmptyPageIsARealAnswer()
    {
        var page = MaterialisedAdPage.Resolved(new PagedResult<JobAdId>([], 0, 1, 20));

        page.State.ShouldBe(CriterionMaterialisationState.Materialised);
        page.Page.ShouldNotBeNull();
        page.Page.TotalCount.ShouldBe(0);
    }

    [Fact]
    public void AdPage_Rejects_APageBesideARefusal_AndARefusalCarryingAPage()
    {
        // The false zero ONE LEVEL UP from the count: an empty ad list rendered for a breadth refusal or
        // "not materialised" reads as "nothing found". Keeping the page absent in those two states is
        // what forces the surface to branch before it reaches its empty state.
        var page = new PagedResult<JobAdId>([], 0, 1, 20);

        Should.Throw<ArgumentException>(() =>
            new MaterialisedAdPage(CriterionMaterialisationState.TooBroad, page));

        Should.Throw<ArgumentException>(() =>
            new MaterialisedAdPage(CriterionMaterialisationState.NotMaterialised, page));

        Should.Throw<ArgumentException>(() =>
            new MaterialisedAdPage(CriterionMaterialisationState.Materialised, null));
    }

    [Fact]
    public void AdPage_TooBroadAndNotMaterialised_AreTwoDifferentAnswers()
    {
        MaterialisedAdPage.TooBroad.Page.ShouldBeNull();
        MaterialisedAdPage.NotMaterialised.Page.ShouldBeNull();
        MaterialisedAdPage.TooBroad.ShouldNotBe(MaterialisedAdPage.NotMaterialised);
    }
}
