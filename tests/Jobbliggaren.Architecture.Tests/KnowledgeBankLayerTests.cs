using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fas 4 STEG 7 (F4-7) anti-regression — the CV knowledge-bank contract (rubric +
/// cliché lexicon + verb mapping) respects Clean Architecture and pins the data-driven
/// shape so the rejected "thresholds as C# literals" / "score baked into the criterion"
/// branches cannot creep in (ADR 0071/0074; CLAUDE.md §5).
///
/// Goodhart parity with MatchScorerLayerTests / FullMatchScorerLayerTests:
///   - RubricCriterion is pinned to exactly its 10 named props and asserted to carry
///     NO numeric property — a criterion must never become a hidden opaque score.
///   - Rubric is pinned to EXPOSE Weights/CategoryWeights/Bands/CriticalFailIds/
///     Criteria, forcing the thresholds into DATA fields (loaded from JSON) rather than
///     C# literals inside a scorer.
///
/// HONEST LIMIT (stated for the reviewer): reflection proves the contract SHAPE — that
/// the thresholds are carried as data fields and the criterion has no numeric score. It
/// does NOT prove the VALUES originate from a JSON file rather than a C# literal
/// initialiser; a true "no inline threshold literal" guard would need a Roslyn analyzer,
/// which is out of F4-7 scope. The RubricProviderTests embedded-resource load is the
/// behavioural complement that the data IS file-sourced.
///
/// Ports live in Jobbliggaren.Application.KnowledgeBank.Abstractions; the provider/
/// loader impls are internal sealed in Jobbliggaren.Infrastructure.KnowledgeBank
/// (parity MatchScorer / OccupationCodeDeriver).
///
/// RED until the contract types ship in Application and the providers/loader ship
/// internal sealed in Infrastructure.
/// </summary>
public class KnowledgeBankLayerTests
{
    private const string ProviderNamespace = "Jobbliggaren.Infrastructure.KnowledgeBank";

    private static readonly Assembly ApplicationAsm =
        typeof(Jobbliggaren.Application.AssemblyMarker).Assembly;
    private static readonly Assembly InfrastructureAsm =
        typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

    private static string[] PublicInstancePropNames(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract") // compiler-generated on records
            .Select(p => p.Name)
            .ToArray();

    // ===============================================================
    // 1. Contract types + ports live in the Application assembly
    // ===============================================================

    [Fact]
    public void Rubric_contract_types_are_in_Application_layer()
    {
        foreach (var t in new[]
        {
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.Rubric),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.RubricCriterion),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.RubricVersion),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.ScoreBand),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.CategoryWeights),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.ClicheList),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.ClicheEntry),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.VerbMapping),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.StrongVerbGroup),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.WeakVerbMapping),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.FrameCatalog),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.CvFrame),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.FrameSlot),
        })
        {
            t.Assembly.ShouldBe(ApplicationAsm,
                $"{t.Name} ska ligga i Application-assemblyt (KnowledgeBank.Abstractions).");
        }
    }

    [Fact]
    public void Ports_are_in_Application_KnowledgeBank_Abstractions()
    {
        foreach (var port in new[]
        {
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.IRubricProvider),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.IClicheLexicon),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.IVerbMapper),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.IFrameProvider),
        })
        {
            port.Assembly.ShouldBe(ApplicationAsm,
                $"Porten {port.Name} ska ligga i Application.");
            port.Namespace.ShouldBe(
                "Jobbliggaren.Application.KnowledgeBank.Abstractions",
                $"Porten {port.Name} ska ligga i KnowledgeBank.Abstractions.");
        }
    }

    [Fact]
    public void Ports_are_not_in_Infrastructure_assembly()
    {
        foreach (var port in new[]
        {
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.IRubricProvider),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.IClicheLexicon),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.IVerbMapper),
            typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.IFrameProvider),
        })
        {
            port.Assembly.ShouldNotBe(InfrastructureAsm);
        }
    }

    // ===============================================================
    // 2. Goodhart guard pinned BY SHAPE — RubricCriterion carries NO numeric score
    // ===============================================================

    [Fact]
    public void RubricCriterion_carries_exactly_the_eleven_named_properties()
    {
        // Exactly the 11 architect-bound props — no more (a sneak field), no less.
        // NotAssessedReason added in rubric 1.0.1 (CV-UX wave STEG 1, CTO Decision D1):
        // the versioned, civic-Swedish user-facing reason a NotAssessed verdict reports
        // (ADR 0071 reasons-as-data) — a string, never a numeric (Goodhart pin below holds).
        var criterion = typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.RubricCriterion);

        PublicInstancePropNames(criterion).ShouldBe(
            [
                "Id", "Category", "Name", "Weight", "Profile", "Assessability",
                "AtsPassSignal", "AtsFailSignal", "VisualPassSignal", "VisualFailSignal",
                "NotAssessedReason",
            ],
            ignoreOrder: true,
            "RubricCriterion ska bära exakt de 11 namngivna properties — " +
            $"faktiska: [{string.Join(", ", PublicInstancePropNames(criterion))}].");
    }

    [Fact]
    public void RubricCriterion_has_no_numeric_property()
    {
        // Goodhart parity with MatchDimension: a RubricCriterion must NOT carry a
        // numeric Score/Value/Intensity (the opaque-number anti-pattern, CLAUDE.md §5).
        // NOTE: `Weight` here is the CriterionWeight ENUM (a tier label), which is fine —
        // the rejected shape is a numeric field (int/double/decimal/float/etc). The
        // numeric weight VALUES live in Rubric.Weights (data), keyed by the enum.
        var criterion = typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.RubricCriterion);

        var numericTypes = new[]
        {
            typeof(int), typeof(int?), typeof(long), typeof(long?),
            typeof(double), typeof(double?), typeof(decimal), typeof(decimal?),
            typeof(float), typeof(float?), typeof(short), typeof(short?),
            typeof(byte), typeof(byte?),
        };

        var numericProps = criterion
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => numericTypes.Contains(p.PropertyType))
            .Select(p => $"{p.Name}:{p.PropertyType.Name}")
            .ToList();

        numericProps.ShouldBeEmpty(
            "RubricCriterion får INTE bära en numerisk property (opak-poäng-" +
            "antimönster, CLAUDE.md §5 — Weight är en enum-tier, inte ett tal): " +
            $"[{string.Join(", ", numericProps)}].");
    }

    // ===============================================================
    // 3. Thresholds-carried-as-data pin — Rubric must EXPOSE the data fields
    // ===============================================================

    [Fact]
    public void Rubric_exposes_the_threshold_and_criteria_data_fields()
    {
        // The thresholds (Weights, CategoryWeights, Bands), the critical-fail set, and
        // the criteria are EXPOSED as data on the Rubric record — they cannot be omitted
        // and inlined as C# literals in a scorer. (Honest limit: see class summary —
        // this proves the carrier shape, not the literal source.)
        var rubric = typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.Rubric);
        var props = PublicInstancePropNames(rubric);

        foreach (var required in new[]
        {
            "Version", "EffectiveDate", "Weights", "CategoryWeights",
            "Bands", "CriticalFailIds", "Criteria",
        })
        {
            props.ShouldContain(required,
                $"Rubric ska exponera '{required}' (trösklar/kriterier bärs som DATA, " +
                "inte som C#-literaler i en scorer).");
        }
    }

    // ===============================================================
    // 4. Locked enum member sets (parity MatchDimensionVerdict pin)
    // ===============================================================

    [Fact]
    public void CriterionAssessability_is_the_locked_three_member_set()
    {
        Enum.GetNames<Jobbliggaren.Application.KnowledgeBank.Abstractions.CriterionAssessability>()
            .ShouldBe(["Deterministic", "DeterministicPlusNlp", "NotAssessedV1"],
                ignoreOrder: true);
    }

    [Fact]
    public void CriterionWeight_is_the_locked_four_member_set()
    {
        Enum.GetNames<Jobbliggaren.Application.KnowledgeBank.Abstractions.CriterionWeight>()
            .ShouldBe(["Critical", "High", "Medium", "Low"], ignoreOrder: true);
    }

    [Fact]
    public void RubricProfile_is_the_locked_three_member_set()
    {
        Enum.GetNames<Jobbliggaren.Application.KnowledgeBank.Abstractions.RubricProfile>()
            .ShouldBe(["Both", "AtsOnly", "VisualOnly"], ignoreOrder: true);
    }

    [Fact]
    public void RubricCategory_is_the_locked_five_member_set()
    {
        Enum.GetNames<Jobbliggaren.Application.KnowledgeBank.Abstractions.RubricCategory>()
            .ShouldBe(
                ["Content", "Structure", "Language", "AtsParsability", "VisualQuality"],
                ignoreOrder: true);
    }

    [Fact]
    public void ScoreBandLabel_is_the_locked_four_member_set()
    {
        Enum.GetNames<Jobbliggaren.Application.KnowledgeBank.Abstractions.ScoreBandLabel>()
            .ShouldBe(
                ["NotReady", "NeedsRework", "Competitive", "TopTier"],
                ignoreOrder: true);
    }

    [Fact]
    public void FrameKind_is_the_locked_two_member_set()
    {
        // Fas 4b PR-5 (ADR 0093 §D2/§D3): sentence + measure are the ONLY frame
        // mechanics — field/format fixes are algorithm (code), never frame data.
        Enum.GetNames<Jobbliggaren.Application.KnowledgeBank.Abstractions.FrameKind>()
            .ShouldBe(["Sentence", "Measure"], ignoreOrder: true);
    }

    [Fact]
    public void FrameSlotKind_is_the_locked_four_member_set()
    {
        // Each kind maps to one §D2 FromFrame provenance invariant (noun ⊆ Before-span,
        // verb ∈ list@version, number == user echo, text = user-parameterized token).
        Enum.GetNames<Jobbliggaren.Application.KnowledgeBank.Abstractions.FrameSlotKind>()
            .ShouldBe(["Noun", "Verb", "Number", "Text"], ignoreOrder: true);
    }

    // ===============================================================
    // 5. Domain does NOT depend on the KnowledgeBank contract
    // ===============================================================

    [Fact]
    public void Domain_should_not_depend_on_KnowledgeBank_abstractions()
    {
        // The CV knowledge bank is an Application-/Infrastructure-concern (versioned
        // data + ports); Domain must not gain a dependency on the rubric/cliché/verb
        // contract. Parity with the Domain-purity pins in MatchScorerLayerTests.
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Jobbliggaren.Application.KnowledgeBank.Abstractions",
                "Jobbliggaren.Infrastructure.KnowledgeBank")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Domain läcker mot KnowledgeBank-kontraktet (Clean Arch, CLAUDE.md §2.1): " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    // ===============================================================
    // 6. Provider/loader impls are internal sealed in Infrastructure
    //    (parity MatchScorer / OccupationCodeDeriver — proven BY NAME so RED
    //    requires the types to EXIST and be non-public)
    // ===============================================================

    [Fact]
    public void Providers_and_loader_exist_in_Infrastructure_KnowledgeBank()
    {
        var names = InfrastructureAsm.GetTypes()
            .Where(t => t.Namespace == ProviderNamespace)
            .Select(t => t.Name)
            .ToList();

        foreach (var expected in new[]
        {
            "RubricProvider", "ClicheLexicon", "VerbMapper", "RubricLoader",
            "FrameProvider", "FramesLoader",
        })
        {
            names.ShouldContain(expected,
                $"{expected} saknas i {ProviderNamespace} (F4-7 production-impl ej " +
                "skriven än — väntad RED).");
        }
    }

    [Fact]
    public void Provider_impls_are_internal()
    {
        // Public surface of the KnowledgeBank namespace must be empty — the impls are
        // internal (consumed via the ports + DI), parity MatchScorer (CTO V4-b).
        var publicTypes = InfrastructureAsm.GetTypes()
            .Where(t => t.Namespace == ProviderNamespace)
            .Where(t => t.IsPublic || (t.IsNested && t.IsNestedPublic))
            .Select(t => t.FullName)
            .ToList();

        publicTypes.ShouldBeEmpty(
            "KnowledgeBank-impls ska vara internal (parity MatchScorer / " +
            $"OccupationCodeDeriver). Public: {string.Join(", ", publicTypes!)}");
    }

    [Fact]
    public void Provider_impls_are_sealed()
    {
        foreach (var name in new[] { "RubricProvider", "ClicheLexicon", "VerbMapper", "FrameProvider" })
        {
            var impl = InfrastructureAsm.GetTypes()
                .Single(t => t.Namespace == ProviderNamespace && t.Name == name);

            impl.IsSealed.ShouldBeTrue(
                $"{name} ska vara sealed (internal sealed, parity OccupationCodeDeriver).");
        }
    }

    [Fact]
    public void Provider_impls_implement_their_ports()
    {
        var infra = InfrastructureAsm.GetTypes()
            .Where(t => t.Namespace == ProviderNamespace)
            .ToList();

        typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.IRubricProvider)
            .IsAssignableFrom(infra.Single(t => t.Name == "RubricProvider"))
            .ShouldBeTrue("RubricProvider ska implementera IRubricProvider.");
        typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.IClicheLexicon)
            .IsAssignableFrom(infra.Single(t => t.Name == "ClicheLexicon"))
            .ShouldBeTrue("ClicheLexicon ska implementera IClicheLexicon.");
        typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.IVerbMapper)
            .IsAssignableFrom(infra.Single(t => t.Name == "VerbMapper"))
            .ShouldBeTrue("VerbMapper ska implementera IVerbMapper.");
        typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.IFrameProvider)
            .IsAssignableFrom(infra.Single(t => t.Name == "FrameProvider"))
            .ShouldBeTrue("FrameProvider ska implementera IFrameProvider.");
    }
}
