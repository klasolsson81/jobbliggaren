using System.Text;
using System.Text.Json;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds.Junior;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Junior;

/// <summary>
/// #2264 — the junior rule set's shape checks, driven through the real <c>LoadFrom(Stream)</c> seam with synthetic
/// files. Every malformed shape must fail loud at host build, because a malformed rule set is a list that silently
/// shows the wrong ads. Plus the shipped Data/IT file against its own contract.
/// </summary>
public class JuniorRuleSetLoaderTests
{
    private static readonly string[] TopLevelKeys =
    [
        "//", "ruleSetVersion", "occupationFieldConceptId", "enabled", "reading", "patterns", "candidatePatterns",
        "candidateWhenNoExperienceRequired", "years", "rules", "candidateFallbackRuleId", "noSignalRuleId", "timeoutRuleId",
    ];

    internal const string MinimalValid = """
        {
          "ruleSetVersion": "1.2",
          "occupationFieldConceptId": "field-1",
          "enabled": true,
          "reading": { "ruleId": "reading-test", "junior": ["90000001"], "notJunior": ["90000002"] },
          "patterns": {
            "candidate": "\\bjunior|\\btrainee",
            "juniorTitle": "\\bjunior",
            "mentor": "handleda junior",
            "years": "\\b(?<from>\\d{1,2}|tre|två)\\s*(-\\s*(?<to>\\d{1,2}))?\\s*års? erfarenhet"
          },
          "candidatePatterns": ["candidate"],
          "candidateWhenNoExperienceRequired": true,
          "years": { "patterns": ["years"], "numberWords": { "två": 2, "tre": 3 }, "plausibleMin": 1, "plausibleMax": 15 },
          "rules": [
            { "id": "years-3-plus", "verdict": "NotJunior", "auto": true, "all": [ { "on": "yearsAtLeast", "years": 3 } ] },
            { "id": "text-mentor", "verdict": "NotJunior", "auto": false, "all": [ { "on": "text", "pattern": "mentor" } ] },
            { "id": "title-junior", "verdict": "Junior", "auto": true,
              "all": [ { "on": "title", "pattern": "juniorTitle" }, { "on": "text", "pattern": "mentor", "not": true } ] },
            { "id": "flag-no-experience", "verdict": "Junior", "auto": false,
              "all": [ { "on": "experienceRequired", "flag": false }, { "on": "yearsStated", "not": true } ] }
          ],
          "candidateFallbackRuleId": "candidate-only",
          "noSignalRuleId": "no-signal",
          "timeoutRuleId": "match-timeout"
        }
        """;

    internal static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    /// <summary>Injects a defect and proves it landed, since an unmatched Replace is a silent no-op.</summary>
    private static MemoryStream Mutated(string find, string replaceWith)
    {
        MinimalValid.Contains(find, StringComparison.Ordinal).ShouldBeTrue($"'{find}' is not in MinimalValid.");
        return Json(MinimalValid.Replace(find, replaceWith, StringComparison.Ordinal));
    }

    [Fact]
    public void LoadFrom_MapsTheContract_WhenTheFileIsWellFormed()
    {
        var set = JuniorRuleSetLoader.LoadFrom(Json(MinimalValid));

        set.Version.ShouldStartWith("1.2+");
        set.Version.Length.ShouldBe("1.2+".Length + 8);
        set.OccupationFieldConceptId.ShouldBe("field-1");
        set.Enabled.ShouldBeTrue();
        set.Reading.RuleId.ShouldBe("reading-test");
        set.Reading.JuniorExternalIds.ShouldBe(["90000001"]);
        set.Reading.NotJuniorExternalIds.ShouldBe(["90000002"]);
        set.Rules.Select(r => r.Id).ShouldBe(["years-3-plus", "text-mentor", "title-junior", "flag-no-experience"]);
        set.Rules[0].Verdict.ShouldBe(JuniorVerdict.NotJunior);
        set.Rules[0].Auto.ShouldBeTrue();
        set.Rules[2].All.Count.ShouldBe(2);
        set.Rules[2].All[1].Negated.ShouldBeTrue();
        set.CandidateFallbackRuleId.ShouldBe("candidate-only");
        set.NoSignalRuleId.ShouldBe("no-signal");
        set.TimeoutRuleId.ShouldBe("match-timeout");
    }

    [Fact]
    public void LoadFrom_TheVersionFollowsTheContent_EvenWhenTheDeclaredVersionStaysTheSame()
    {
        // Flipping one rule's auto flag without raising ruleSetVersion must still re-classify every open ad.
        var before = JuniorRuleSetLoader.LoadFrom(Json(MinimalValid)).Version;
        var after = JuniorRuleSetLoader.LoadFrom(Mutated(
            "\"verdict\": \"NotJunior\", \"auto\": false", "\"verdict\": \"NotJunior\", \"auto\": true")).Version;

        after.ShouldStartWith("1.2+");
        after.ShouldNotBe(before);
    }

    [Theory]
    [InlineData("\"candidate\": \"\\\\bjunior|\\\\btrainee\"", "\"candidate\": \"junior(?=x)\"")] // a lookahead: not linear-time
    [InlineData("\"candidate\": \"\\\\bjunior|\\\\btrainee\"", "\"candidate\": \"(junior\"")] // does not compile at all
    [InlineData("\"candidate\": \"\\\\bjunior|\\\\btrainee\"", "\"candidate\": \"(\\\\w)\\\\1\"")] // a backreference
    [InlineData("\"id\": \"text-mentor\"", "\"id\": \"years-3-plus\"")] // a rule id twice
    [InlineData("\"id\": \"text-mentor\"", "\"id\": \"no-signal\"")] // a rule id equal to a fallback id
    [InlineData("\"id\": \"text-mentor\"", "\"id\": \"Text_Mentor\"")] // not lower-case kebab
    [InlineData("\"verdict\": \"NotJunior\", \"auto\": false", "\"verdict\": \"Maybe\", \"auto\": false")] // unknown verdict
    [InlineData("\"verdict\": \"NotJunior\", \"auto\": false", "\"verdict\": \"Unclear\", \"auto\": false")] // Unclear is not a rule's verdict
    [InlineData("{ \"on\": \"text\", \"pattern\": \"mentor\" } ] },", "{ \"on\": \"body\", \"pattern\": \"mentor\" } ] },")] // unknown target
    [InlineData("{ \"on\": \"text\", \"pattern\": \"mentor\" } ] },", "{ \"on\": \"text\", \"pattern\": \"nothing\" } ] },")] // pattern nothing defines
    [InlineData("\"junior\": [\"90000001\"]", "\"junior\": [\"9000000x\"]")] // not a Platsbanken id
    [InlineData("\"junior\": [\"90000001\"]", "\"junior\": [\"90000002\"]")] // one id in both directions
    [InlineData("\"junior\": [\"90000001\"]", "\"junior\": [\"90000001\", \"90000001\"]")] // one id twice
    [InlineData("\"years\": 3 }", "\"years\": 0 }")] // years without a positive bound
    [InlineData("\"flag\": false }", "\"flag\": null }")] // the experience mark without a flag
    [InlineData("(?<from>", "(?<lower>")] // a years pattern without the from group
    [InlineData("\"ruleSetVersion\": \"1.2\"", "\"ruleSetVersion\": \"\"")] // no version
    [InlineData("\"ruleSetVersion\": \"1.2\"", "\"ruleSetVersion\": \"v1\"")] // not major[.minor[.patch]]
    [InlineData("\"timeoutRuleId\": \"match-timeout\"", "\"timeoutRuleId\": \"\"")] // no timeout rule id
    [InlineData("\"occupationFieldConceptId\": \"field-1\"", "\"occupationFieldConceptId\": \" \"")] // no field
    public void LoadFrom_FailsLoud_OnAMalformedShape(string find, string replaceWith)
    {
        Should.Throw<InvalidOperationException>(() => JuniorRuleSetLoader.LoadFrom(Mutated(find, replaceWith)));
    }

    [Fact]
    public void LoadDataIt_LoadsTheShippedFile_ForTheDataItField()
    {
        var set = JuniorRuleSetLoader.LoadDataIt();

        set.OccupationFieldConceptId.ShouldBe("apaJ_2ja_LuF");
        set.Enabled.ShouldBeTrue();
        set.Rules.ShouldNotBeEmpty();
    }

    [Fact]
    public void TheShippedFile_CarriesExactlyItsTopLevelKeys()
    {
        using var stream = typeof(JuniorRuleSetLoader).Assembly
            .GetManifestResourceStream(JuniorRuleSetLoader.DataItResourceName)!;
        using var document = JsonDocument.Parse(stream);

        document.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(TopLevelKeys, ignoreOrder: true);
    }
}
