using Jobbliggaren.Infrastructure.CompanyRegister;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #1681 (ADR 0139; security-auditor Major 4, 2026-09-06) — the member table's OWN
/// legal-entities-only guard, unit-tested without a DB, HTTP or DI, exactly like
/// <c>ScbLegalEntityFilterTests</c> covers the ingest-side twin.
///
/// <para>
/// <b>Why this suite exists when the register is already pnr-free.</b> It is not testing that the
/// register contains no personnummer — <c>ScbLegalEntityFilter</c> owns that, at ingest. It is testing
/// that this table does not INHERIT the guarantee: security-auditor's Major 4 is that a materialised
/// member table would otherwise be the repo's first at-rest store whose pnr-freedom rests wholly on
/// another subsystem's ingest invariant, "exactly what the repo declined to do for
/// <c>CompanyLookupDto</c> (#454)". So the property under test is the FILTER's behaviour on inputs the
/// register is not supposed to produce — which is the only behaviour that matters if the register ever
/// does.
/// </para>
/// </summary>
public class CompanyWatchCriterionMemberFilterTests
{
    // Third digit >= 2 = legal entity (Skatteverket group number for a legal person is 2-9).
    private const string LegalEntity = "5560125790";

    [Fact]
    public void Apply_KeepsLegalEntities_AndCountsNothingExcluded()
    {
        var result = CompanyWatchCriterionMemberFilter.Apply([LegalEntity, "5592804784"]);

        result.OrganizationNumbers.ShouldBe([LegalEntity, "5592804784"]);
        result.ExcludedPersonnummerShaped.ShouldBe(0);
        result.ExcludedInvalid.ShouldBe(0);
    }

    [Theory]
    // THE BOUNDARY, both sides, and it is the whole heuristic. A personnummer YYMMDD-NNNN has as its
    // third digit the TENS digit of the birth month: 0 for months 01-09, 1 for months 10-12. A legal
    // org.nr never has 0 or 1 there. So the guard is "third digit < 2", and these four cases are the
    // two admitted values and the two rejected ones either side of the line. Pinning only one side
    // would leave a mutant that flips < to <= (or drops the "1" arm) fully green — the same
    // both-boundary discipline ScbLegalEntityFilterTests applies to its own copy of this rule.
    [InlineData("5510125790", true)]  // third digit 1 — pnr-shaped (months 10-12)
    [InlineData("5500125790", true)]  // third digit 0 — pnr-shaped (months 01-09)
    [InlineData("5520125790", false)] // third digit 2 — the lowest legal group number
    [InlineData("5590125790", false)] // third digit 9 — the highest
    public void Apply_ExcludesPersonnummerShaped_AtTheExactThirdDigitBoundary(
        string orgNr, bool expectedExcluded)
    {
        var result = CompanyWatchCriterionMemberFilter.Apply([orgNr]);

        if (expectedExcluded)
        {
            result.OrganizationNumbers.ShouldBeEmpty();
            result.ExcludedPersonnummerShaped.ShouldBe(1);
        }
        else
        {
            result.OrganizationNumbers.ShouldBe([orgNr]);
            result.ExcludedPersonnummerShaped.ShouldBe(0);
        }
    }

    [Theory]
    // Not 10 ASCII digits => rejected as INVALID by OrganizationNumber.Create before the shape guard
    // is ever consulted. The fullwidth case is #865's lesson carried forward: .NET's \d matches the
    // whole Unicode decimal-digit category, so a fullwidth "org.nr" would pass a \d-based guard, be
    // stored, and then never equality-match the ASCII job_ads.organization_number — a member that
    // silently matches NOTHING forever. The VO's regex is [0-9]; this pins that the member path
    // inherits that strictness rather than re-deriving a looser one.
    [InlineData("556012579")]            // 9 digits
    [InlineData("55601257901")]          // 11 digits
    [InlineData("556012-5790")]          // the written form, not the stored one
    [InlineData("５５６０１２５７９０")] // fullwidth digits
    [InlineData("")]
    [InlineData("   ")]
    public void Apply_ExcludesAnythingThatIsNotTenAsciiDigits_AsInvalid(string orgNr)
    {
        var result = CompanyWatchCriterionMemberFilter.Apply([orgNr]);

        result.OrganizationNumbers.ShouldBeEmpty();
        result.ExcludedInvalid.ShouldBe(1);
        result.ExcludedPersonnummerShaped.ShouldBe(0,
            "en ogiltig org.nr ska räknas som ogiltig, inte som personnummerformad — annars kan de "
            + "två räknarna inte skilja ett ingest-hål från skräpdata");
    }

    [Fact]
    public void Apply_CountsEachExclusionReasonSeparately_OverAMixedBatch()
    {
        // The counters are the audited evidence (they ride out on the state row and the run result),
        // so they must be separable: a non-zero pnr count is a SECURITY signal that the ingest guard
        // has a hole, while a non-zero invalid count is a data-quality note. Collapsing them into one
        // number would make the security signal unreadable.
        var result = CompanyWatchCriterionMemberFilter.Apply(
            [LegalEntity, "5500125790", "556012579", "5592804784", "5510125790"]);

        result.OrganizationNumbers.ShouldBe([LegalEntity, "5592804784"]);
        result.ExcludedPersonnummerShaped.ShouldBe(2);
        result.ExcludedInvalid.ShouldBe(1);
    }

    [Fact]
    public void Apply_OnAnEmptyBatch_ReturnsEmpty_WithoutThrowing()
    {
        // A criterion that matches nothing is a legitimate, honest outcome — it must materialise as
        // an EMPTY set (state Materialised, member_count 0), not as a failure and not as an absent
        // state row, because those two mean different things to the read side.
        var result = CompanyWatchCriterionMemberFilter.Apply([]);

        result.OrganizationNumbers.ShouldBeEmpty();
        result.ExcludedPersonnummerShaped.ShouldBe(0);
        result.ExcludedInvalid.ShouldBe(0);
    }

    [Fact]
    public void Apply_OnNull_Throws()
    {
        Should.Throw<ArgumentNullException>(() => CompanyWatchCriterionMemberFilter.Apply(null!));
    }
}
