using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Rubric v1.2 (Fas 4b PR-5, #654, CTO-bind D1 N-1 corollary + loader guardrails) — the LOADER
/// half of the thresholds/styleOnly contract. Two facts:
///
/// <list type="number">
/// <item><b>N-1 tolerance.</b> An older (v1.1.0) asset that PRE-DATES thresholds/styleOnly must
/// still deserialise + map through the REAL loader without throwing; a criterion authored without
/// the fields maps to <see cref="RubricCriterion.Thresholds"/> == null and
/// <see cref="RubricCriterion.StyleOnly"/> == false (fail-closed). Parity
/// <see cref="RubricNotAssessedReasonBackcompatTests"/>. The live engine's RequiredThreshold
/// fail-loud (which the SHIPPED asset satisfies) is exercised elsewhere — the synthetic N-1
/// fixture is only ever driven through the loader, so fail-loud never fires in production.</item>
/// <item><b>Fail-loud loader guardrails (RED).</b> A styleOnly criterion that is also a critical
/// fail, and a negative threshold value, are corrupt assets the loader must reject (fail-loud).
/// NaN cannot be represented in JSON, so it is not testable here (documented skip).</item>
/// </list>
///
/// The N-1 fixture is embedded in THIS assembly:
///   LogicalName = Jobbliggaren.Application.UnitTests.KnowledgeBank.Fixtures.rubric.v1.1.0-no-thresholds-synthetic.json
/// </summary>
public class RubricThresholdsBackcompatTests
{
    private const string FixtureResourceName =
        "Jobbliggaren.Application.UnitTests.KnowledgeBank.Fixtures.rubric.v1.1.0-no-thresholds-synthetic.json";

    private static Stream OpenFixture()
    {
        var asm = typeof(RubricThresholdsBackcompatTests).Assembly;
        return asm.GetManifestResourceStream(FixtureResourceName)
            ?? throw new InvalidOperationException(
                $"Synthetic N-1 (no-thresholds) fixture saknas: {FixtureResourceName}. " +
                "Verifiera <EmbeddedResource> + <LogicalName> i Jobbliggaren.Application.UnitTests.csproj.");
    }

    private static MemoryStream InlineRubric(string json) =>
        new(System.Text.Encoding.UTF8.GetBytes(json));

    // ── N-1 tolerance ─────────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_ShouldNotThrow_WhenThresholdsAndStyleOnlyAbsent()
    {
        // An N-1 document that pre-dates thresholds/styleOnly is READ, not rejected (default
        // System.Text.Json tolerance + the trailing-optional contract params).
        using var stream = OpenFixture();

        var act = () => RubricLoader.LoadFrom(stream);

        var rubric = act.ShouldNotThrow();
        rubric.Criteria.ShouldNotBeEmpty();
    }

    [Fact]
    public void LoadFrom_ShouldMapMissingThresholdsToNull_AndStyleOnlyToFalse_WhenAbsent()
    {
        // A criterion authored WITHOUT the fields maps to Thresholds == null (the live engine's
        // fail-loud covers the SHIPPED asset; the loader never invents a threshold) and
        // StyleOnly == false (fail-closed — a criterion missing the flag can never be silenced).
        using var stream = OpenFixture();

        var rubric = RubricLoader.LoadFrom(stream);

        rubric.Criteria.ShouldAllBe(c => c.Thresholds == null);
        rubric.Criteria.ShouldAllBe(c => c.StyleOnly == false);
    }

    // ── Fail-loud loader guardrails (RED) ─────────────────────────────────

    [Fact]
    public void LoadFrom_ShouldThrow_WhenAStyleOnlyCriterionIsAlsoACriticalFail()
    {
        // CTO-bind D2: a critical-fail criterion can never be style-ignorable (fail-loud) — the
        // flag silences COSMETIC rules only. A1 is a criticalFailId AND styleOnly → reject.
        using var stream = InlineRubric(StyleOnlyCriticalRubricJson);

        var ex = Should.Throw<InvalidOperationException>(() => { RubricLoader.LoadFrom(stream); });

        // The message names the offending styleOnly-critical criterion (fail-loud).
        ex.Message.ShouldContain("A1");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenAThresholdValueIsNegative()
    {
        // CTO-bind D1 loader guardrail: thresholds must be finite and non-negative (a negative
        // ratio is a corrupt asset). NaN cannot be represented in JSON, so that arm is not
        // testable here (documented skip, per the task).
        using var stream = InlineRubric(NegativeThresholdRubricJson);

        var ex = Should.Throw<InvalidOperationException>(() => { RubricLoader.LoadFrom(stream); });

        // The message names the criterion whose threshold value is invalid (fail-loud).
        ex.Message.ShouldContain("A1");
    }

    // A minimal, otherwise-valid single-criterion rubric where the Båda criterion A1 is BOTH a
    // criticalFailId AND styleOnly:true — the sole invalidity the guardrail must catch.
    private const string StyleOnlyCriticalRubricJson =
        """
        {
          "rubricVersion": "1.2.0",
          "effectiveDate": "2026-07-05",
          "criticalFailIds": ["A1"],
          "criteria": [
            {
              "id": "A1",
              "category": "Innehåll",
              "name": "Syntetiskt",
              "weight": "Kritisk",
              "profile": "Båda",
              "assessability": "deterministic",
              "atsPassSignal": "x",
              "atsFailSignal": "x",
              "visualPassSignal": "x",
              "visualFailSignal": "x",
              "styleOnly": true
            }
          ]
        }
        """;

    // A minimal, otherwise-valid single-criterion rubric whose A1 carries a NEGATIVE threshold —
    // the sole invalidity the value guardrail must catch (criticalFailIds empty, so only the
    // threshold validation can throw).
    private const string NegativeThresholdRubricJson =
        """
        {
          "rubricVersion": "1.2.0",
          "effectiveDate": "2026-07-05",
          "criticalFailIds": [],
          "criteria": [
            {
              "id": "A1",
              "category": "Innehåll",
              "name": "Syntetiskt",
              "weight": "Kritisk",
              "profile": "Båda",
              "assessability": "deterministic",
              "atsPassSignal": "x",
              "atsFailSignal": "x",
              "visualPassSignal": "x",
              "visualFailSignal": "x",
              "thresholds": { "failRatio": -0.5 }
            }
          ]
        }
        """;
}
