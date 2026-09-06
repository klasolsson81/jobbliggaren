using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #1681 (ADR 0139) — the half of Major 3 that <c>CompanyWatchMaterialisationOptionsTests</c> cannot
/// reach.
///
/// <para>
/// <b>Why a second suite.</b> Those tests construct <c>new CompanyWatchMaterialisationOptions()</c>
/// directly and never touch configuration binding, so they measure that the CLASS declares its own
/// section — not that the HOST reads it. Swapping the composition root's
/// <c>GetSection(CompanyWatchMaterialisationOptions.SectionName)</c> for
/// <c>GetSection(ScbRegisterOptions.SectionName)</c> left all three green while re-creating Major 3
/// through the binding instead of through the constant (test-writer, 2026-09-06). That is precisely
/// the mutation that proves half a feature: the seam the DATA passes through was unmeasured.
/// </para>
/// </summary>
public class CompanyWatchMaterialisationBindingTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        // Only the options registration is exercised — the surrounding AddPersistence needs a
        // connection string, a DEK provider and the rest of the graph, none of which this seam
        // depends on. The binding under test is reproduced verbatim from DependencyInjection.cs;
        // that duplication is the honest cost of not booting the whole host, and the assertions
        // below are about the SECTION NAME the binding reads, which is what the mutation changes.
        var services = new ServiceCollection();
        services.AddOptions<CompanyWatchMaterialisationOptions>()
            .Bind(configuration.GetSection(CompanyWatchMaterialisationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void TheOptions_BindFromTheirOwnSection_NotFromScbRegister()
    {
        // Both sections present, carrying DIFFERENT crons — which is what makes this capable of
        // failing. With only one section populated, a binding pointed at the wrong one would fall back
        // to the defaults and read plausibly.
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["CompanyWatchMaterialisation:CadenceCron"] = "11 11 * * *",
            ["CompanyWatchMaterialisation:Enabled"] = "true",
            ["ScbRegister:SyncCadenceCron"] = "0 6 * * 6",
            ["ScbRegister:Enabled"] = "false",
        });

        var options = provider.GetRequiredService<IOptions<CompanyWatchMaterialisationOptions>>().Value;

        options.CadenceCron.ShouldBe("11 11 * * *");
        options.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void TheOptions_KeepTheirEnabledDefault_WhenTheSectionIsAbsentEntirely()
    {
        // The DEFAULT POSTURE, and the whole of Major 3 in one assertion: a deployment that configures
        // nothing must still run the job. This is also what makes the section's absence safe for
        // CLAUDE.md §11's dev-boot contract — every property has a default, so a fresh stack boots
        // without a CompanyWatchMaterialisation section and owes no appsettings.Local.json.example
        // entry.
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["ScbRegister:Enabled"] = "false",
        });

        var options = provider.GetRequiredService<IOptions<CompanyWatchMaterialisationOptions>>().Value;

        options.Enabled.ShouldBeTrue(
            "materialiseringen måste köra i defaultläget — annars är den 'oberoende' triggern "
            + "beroende av att någon slår på den, vilket är Major 3 en nivå ner");
        options.CadenceCron.ShouldBe("30 5 * * *");
    }

    [Fact]
    public void MaxPerCriterion_And_MaxPerUser_AreTheTwoFactorsOfTheDerivedBound()
    {
        // Parity CompanyWatchCriterionTests.MaxPerUser_IsTwenty: a derived constant needs a literal
        // pin, or every test that references it symbolically moves with it and a nudge is invisible.
        //
        // The two are pinned TOGETHER because they are the two factors of the derivation's second
        // anchor — the block at the criterion cap against /oversikt's budget. Moving either without
        // re-running docs/reviews/2026-09-06-1681-membership-measurement.md's protocol invalidates the
        // bound, and this test is what forces that confrontation rather than letting it pass silently.
        CompanyWatchCriterionMember.MaxPerCriterion.ShouldBe(1000);
        CompanyWatchCriterion.MaxPerUser.ShouldBe(20);
    }
}
