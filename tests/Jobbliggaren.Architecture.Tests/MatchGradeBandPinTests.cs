using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Infrastructure.JobAds;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// <see cref="MatchGradeBands"/> replaced five hand-written transcriptions of one threshold with a
/// derivation. That trade is only safe with these pins, and this file is the reason it is safe —
/// not decoration on top of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the derivation needs a pin at all.</b> <see cref="MatchGradeBands.GoodOrBetter"/> is
/// <c>Filterable.Where(g =&gt; g &gt;= MatchGrade.Good)</c>, which turns the enum's DECLARATION ORDER
/// into a semantic claim: that the rungs are ordinal. They are today, deliberately — ADR 0084 §F2
/// placed <see cref="MatchGrade.Related"/> BETWEEN Basic and Good. The derivation removes
/// transcription risk and INCREASES the blast radius of a reorder — every consumer moves at once;
/// these tests are the other half of that bargain.
/// </para>
/// <para>
/// <b>What these pins do NOT do.</b> They do not verify that <see cref="MatchGradeBands.Filterable"/>
/// is exactly the set <c>GradeRankExpression</c> can emit a positive rank for — that needs Postgres
/// to observe, and is held behaviourally by <c>MatchSortGradeFilterOracleTests</c> and
/// <c>MatchCountOracleTests</c> against Testcontainers. A cheap static approximation of it was
/// considered and rejected: a weak pin on a strong claim reads as coverage and is not.
/// </para>
/// </remarks>
public sealed class MatchGradeBandPinTests
{
    /// <summary>
    /// The identity that crosses the layer boundary, and until this pin it was held by a prose
    /// comment alone ("MÅSTE spegla … håll dem i lockstep"). Driven over EVERY declared
    /// <see cref="MatchGrade"/>, not over <see cref="MatchGradeBands.Filterable"/> — the one-sided
    /// form would pass if <c>GradeToRank</c> also accepted <see cref="MatchGrade.Top"/>.
    /// </summary>
    [Fact]
    public void GradeToRank_accepts_exactly_the_filterable_band()
    {
        foreach (var grade in Enum.GetValues<MatchGrade>())
        {
            var isFilterable = MatchGradeBands.Filterable.Contains(grade);

            if (isFilterable)
            {
                var rank = PerUserJobAdSearchQuery.GradeToRank(grade);
                rank.ShouldBeGreaterThan(
                    0,
                    $"{grade} is filterable, so it must have a positive rank");
            }
            else
            {
                Should.Throw<ArgumentOutOfRangeException>(
                    () => PerUserJobAdSearchQuery.GradeToRank(grade),
                    $"{grade} is not filterable, so GradeToRank must refuse it rather than "
                    + "invent a rank the grade filter would then select on");
            }
        }
    }

    /// <summary>
    /// The rank projection must be strictly increasing over the band.
    /// </summary>
    [Fact]
    public void The_rank_projection_agrees_with_the_enum_order()
    {
        var ranks = MatchGradeBands.Filterable.Select(PerUserJobAdSearchQuery.GradeToRank).ToList();

        ranks.ShouldBe(ranks.Order().ToList(), "ranks must ascend with the enum order");
        ranks.Distinct().Count().ShouldBe(ranks.Count, "two grades must not share a rank");
    }

    /// <summary>
    /// The value pin. Written out as literals on purpose: an expectation computed from
    /// <see cref="MatchGradeBands"/> would assert <c>x == x</c> and could not fail for its own reason.
    /// This is the independent second transcription the derived list has something to be wrong against.
    /// </summary>
    [Fact]
    public void The_bands_hold_their_declared_values_in_their_declared_order()
    {
        MatchGradeBands.Filterable.ShouldBe(
            [MatchGrade.Basic, MatchGrade.Related, MatchGrade.Good, MatchGrade.Strong]);

        MatchGradeBands.GoodOrBetter.ShouldBe([MatchGrade.Good, MatchGrade.Strong]);
    }

    /// <summary>
    /// The alignment the <c>&gt;=</c> derivation rests on, asserted separately from the values so a
    /// failure says which half moved.
    /// </summary>
    [Fact]
    public void The_filterable_band_is_declared_in_strictly_ascending_enum_order()
    {
        var band = MatchGradeBands.Filterable;

        band.ShouldBe(band.Order().ToList(), "the >= Good derivation reads the declaration order");

        Enum.GetValues<MatchGrade>().Max().ShouldBe(
            MatchGrade.Top,
            "Top must stay the maximum: a grade declared above it would be excluded from "
            + "Filterable by hand yet included by any future ordinal reasoning about the top rung");
    }
}
