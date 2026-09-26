using Jobbliggaren.Infrastructure.CompanyRegister;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #1681 (ADR 0139; security-auditor Major 3, 2026-09-06) — pins the materialisation job's TRIGGER
/// INDEPENDENCE as a build gate rather than a comment.
///
/// <para>
/// The finding was that tying the materialisation cadence to the register sync inherits
/// <c>ScbRegister:Enabled</c>, which defaults <c>false</c> — so in the default posture the job never
/// runs, the only refresh is the user's own edit, and a de-registered company is counted on her
/// <c>/oversikt</c> indefinitely (DPIA R-D6, Art. 5(1)(d)). That is the very mitigation this job
/// exists to replace, because materialisation removes the read path's own positive-polarity
/// <c>status = @status</c> predicate from the picture.
/// </para>
///
/// <para>
/// <b>What makes these assertions worth their green.</b> The independence is expressed in three
/// places — a separate section name, a default that is on, and a cron of this section's own — and
/// each is a one-token edit away from silently re-coupling. A comment cannot notice that edit; this
/// can. The default in particular is the non-obvious one: it is the INVERSE of its neighbour's, and
/// anyone tidying the two options classes toward consistency would flip it and reintroduce Major 3
/// exactly.
/// </para>
/// </summary>
public class CompanyWatchMaterialisationOptionsTests
{
    [Fact]
    public void SectionName_IsItsOwn_NotAChildOfScbRegister()
    {
        // A nested section (e.g. "ScbRegister:Materialisation") would bind fine and read fine, and
        // would quietly invite the next person to gate it on the parent's Enabled.
        CompanyWatchMaterialisationOptions.SectionName.ShouldBe("CompanyWatchMaterialisation");
        CompanyWatchMaterialisationOptions.SectionName
            .ShouldNotStartWith(ScbRegisterOptions.SectionName);
    }

    [Fact]
    public void Enabled_DefaultsTrue_UnlikeScbRegisterEnabled_AndThatIsTheWholePoint()
    {
        // The two defaults are deliberately opposite. ScbRegisterOptions.Enabled guards a metered,
        // certificate-gated call to an external authority, so OFF is its safe default. This one
        // guards a local recompute over tables we already hold, so OFF would not be safe — it would
        // be Major 3 one level down: an independent trigger that is independently switched off is not
        // an independent trigger.
        new CompanyWatchMaterialisationOptions().Enabled.ShouldBeTrue();
        new ScbRegisterOptions().Enabled.ShouldBeFalse(
            "kontrollen: den här assertionen är bara meningsfull så länge grannens default är false — "
            + "ändras den, ändras hela argumentet för att de två skiljer sig");
    }

    [Fact]
    public void MaxPerCriterion_And_MaxPerUser_AreTheTwoFactorsOfTheDerivedBound()
    {
        // Parity CompanyWatchCriterionTests.MaxPerUser_IsTwenty: a DERIVED constant needs a literal
        // pin, or every test that references it symbolically moves with it and a nudge is invisible.
        //
        // The two are pinned TOGETHER because they are the two factors of the derivation's second
        // anchor — the block cost at the criterion cap against /oversikt's budget. Moving either
        // without re-running the protocol invalidates the bound, and this is what forces that
        // confrontation instead of letting it pass silently. #1706 is what that confrontation looks
        // like when it fires: the pin held, the bound was re-derived rather than nudged, and the
        // protocol it was re-derived under is docs/reviews/2026-09-08-1706-form-cto.md P1–P5 with the
        // measurement in docs/reviews/2026-09-08-1706-bound-rederivation.md.
        CompanyWatchCriterionMember.MaxPerCriterion.ShouldBe(2500);
        Jobbliggaren.Domain.CompanyWatches.CompanyWatchCriterion.MaxPerUser.ShouldBe(20);
    }

    [Fact]
    public void CadenceCron_DefaultsToADailySlot_NotTheWeeklyRegisterCadence()
    {
        var options = new CompanyWatchMaterialisationOptions();

        options.CadenceCron.ShouldBe("30 5 * * *");
        options.CadenceCron.ShouldNotBe(new ScbRegisterOptions().SyncCadenceCron,
            "kadensen måste täcka BÅDA ingångarna: registret rör sig veckovis, men sparade kriterier "
            + "ändras när som helst — ett kriterium skapat på en måndag skulle vänta till helgen "
            + "under en veckokadens");
    }
}
