using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Fas 4 STEG 7 (F4-7) — forward-compatible deserialisation of an older (N-1) rubric
/// document. The loader must read a v0.9 rubric that PRE-DATES the assessability and
/// bands/critical-fail fields without throwing, and apply SENSIBLE DEFAULTS rather
/// than failing or silently producing nulls — so a future v2 reader of a v1 file (and
/// today's v1 reader of an authored draft) stays robust.
///
/// To exercise the REAL deserialise + map path against a synthetic fixture the test
/// assembly owns (NOT the committed v1 resource), the production loader MUST expose a
/// Stream-based seam — mirroring how TaxonomySnapshotSeeder.LoadSnapshot() is
/// internal-callable from this test assembly via InternalsVisibleTo:
///
///   internal static Rubric RubricLoader.LoadFrom(Stream stream)
///
/// (or the split pair internal static RubricFile Deserialize(Stream) +
/// internal static Rubric MapToContract(RubricFile) — see the agent report).
///
/// The fixture is embedded in THIS assembly:
///   LogicalName = Jobbliggaren.Application.UnitTests.KnowledgeBank.Fixtures.rubric.v0.9-synthetic.json
///
/// RED until RubricLoader.LoadFrom(Stream) ships internal in
/// Jobbliggaren.Infrastructure.KnowledgeBank.
/// </summary>
public class RubricBackcompatTests
{
    private const string FixtureResourceName =
        "Jobbliggaren.Application.UnitTests.KnowledgeBank.Fixtures.rubric.v0.9-synthetic.json";

    private static Stream OpenFixture()
    {
        var asm = typeof(RubricBackcompatTests).Assembly;
        return asm.GetManifestResourceStream(FixtureResourceName)
            ?? throw new InvalidOperationException(
                $"Synthetic N-1 fixture saknas: {FixtureResourceName}. " +
                "Verifiera <EmbeddedResource> + <LogicalName> i " +
                "Jobbliggaren.Application.UnitTests.csproj.");
    }

    [Fact]
    public void GetRubric_ShouldParseSyntheticN1Fixture_WithSensibleDefaults()
    {
        // Runs the synthetic v0.9 fixture through the REAL deserialise+map path.
        using var stream = OpenFixture();

        var act = () => RubricLoader.LoadFrom(stream);

        // 1) Must NOT throw — an older document is read, not rejected.
        var rubric = act.ShouldNotThrow();

        // The version IS read from the data (it is present in the fixture).
        rubric.Version.ShouldBe(RubricVersion.Parse("0.9.0"));

        // 2) Criteria missing `assessability` default to NotAssessedV1 — the honest
        //    fallback (never silently "deterministically assessed", ADR 0071 OQ3).
        rubric.Criteria.ShouldNotBeEmpty();
        rubric.Criteria.ShouldAllBe(c =>
            c.Assessability == CriterionAssessability.NotAssessedV1);
    }

    [Fact]
    public void GetRubric_ShouldDefaultMissingBandsAndCriticalFailIdsToEmptyLists_WhenAbsent()
    {
        // 3) Missing `bands` / `criticalFailIds` → empty lists, NOT null. A consumer
        //    enumerating Bands/CriticalFailIds must never NRE on an older document.
        using var stream = OpenFixture();

        var rubric = RubricLoader.LoadFrom(stream);

        rubric.Bands.ShouldNotBeNull();
        rubric.Bands.ShouldBeEmpty();
        rubric.CriticalFailIds.ShouldNotBeNull();
        rubric.CriticalFailIds.ShouldBeEmpty();
    }

    [Fact]
    public void GetRubric_ShouldIgnoreUnknownJsonMembers_WhenPresent()
    {
        // 4) Skip-unknown: the fixture carries a bogus top-level member ("legacyNote")
        //    and a bogus per-criterion member ("legacyUnknownPerCriterion"). The loader
        //    must ignore both (forward-compat with newer fields) and still map the
        //    known content — proven by the criteria surviving with their ids intact.
        using var stream = OpenFixture();

        var act = () => RubricLoader.LoadFrom(stream);

        var rubric = act.ShouldNotThrow();
        rubric.Criteria.Select(c => c.Id).ShouldContain("A1");
        rubric.Criteria.Select(c => c.Id).ShouldContain("D1");
    }
}
