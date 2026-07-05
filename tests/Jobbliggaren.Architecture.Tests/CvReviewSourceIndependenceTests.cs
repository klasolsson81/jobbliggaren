using System.Reflection;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fas 4b PR-4 (#653, ADR 0093 §D8; CTO-bind PR-4 Q6 DoD 1-2).
///
/// <para>
/// The D8 fitness function — the enforceable expression of "the review engine depends on
/// reviewable content + linear text + section geometry, NOT on which aggregate supplied
/// it". The engine namespace (<c>Jobbliggaren.Infrastructure.Resumes.Review</c>, incl.
/// the rules) must have NO type dependency on the two SOURCE AGGREGATES and their
/// content roots (<c>Resume</c>/<c>ResumeContent</c>, <c>ParsedResume</c>/
/// <c>ParsedResumeContent</c>) — all source data flows through the unified Application
/// <see cref="CvReviewContext"/>, built by exactly two adapters. PII-safe value objects
/// the context re-exposes (<c>PersonnummerScanOutcome</c>, <c>ParsedSectionKind</c>,
/// <c>ParseFallbackReason</c>, <c>ParsedExperience</c> for the shared bullet-unit
/// overload) are deliberately permitted — the forbid targets the aggregates, not the
/// vocabulary.
/// </para>
/// </summary>
public class CvReviewSourceIndependenceTests
{
    private const string EngineNamespace = "Jobbliggaren.Infrastructure.Resumes.Review";

    private static readonly Type[] ForbiddenSourceTypes =
    [
        typeof(Resume),
        typeof(ResumeContent),
        typeof(ParsedResume),
        typeof(ParsedResumeContent),
    ];

    [Fact]
    public void Engine_namespace_has_no_dependency_on_the_source_aggregates()
    {
        var engineAssembly = typeof(Jobbliggaren.Infrastructure.DependencyInjection).Assembly;
        var engineTypes = engineAssembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith(EngineNamespace, StringComparison.Ordinal) == true)
            .ToList();

        // Staleness anchor: the namespace must exist and hold the engine, else this
        // guard has drifted from the code (loud, not silent).
        engineTypes.ShouldContain(t => t.Name == "CvReviewEngine");

        var violations = new List<string>();
        foreach (var type in engineTypes)
        {
            foreach (var referenced in ReferencedTypes(type))
            {
                if (ForbiddenSourceTypes.Contains(referenced))
                    violations.Add($"{type.Name} → {referenced.Name}");
            }
        }

        violations.ShouldBeEmpty(
            "The review engine + rules must stay source-aggregate-independent (ADR 0093 " +
            "§D8): every content read goes through the unified Application " +
            "CvReviewContext (built by FromParsed/FromCanonical) — a direct reference to " +
            "Resume/ResumeContent/ParsedResume/ParsedResumeContent re-forks the engine " +
            "by lifecycle stage. Violations: " + string.Join(", ", violations.Distinct()));
    }

    [Fact]
    public void Port_input_is_the_unified_Application_CvReviewContext()
    {
        // Q6 DoD 2: pin the NEW port surface — ICvReviewEngine.ReviewAsync takes the
        // unified CvReviewContext (Application layer), not either aggregate.
        var reviewMethod = typeof(ICvReviewEngine).GetMethod("ReviewAsync");
        reviewMethod.ShouldNotBeNull();

        var firstParameter = reviewMethod!.GetParameters().First().ParameterType;
        firstParameter.ShouldBe(typeof(CvReviewContext),
            "ICvReviewEngine.ReviewAsync must take the unified CvReviewContext (ADR " +
            "0093 §D8 — one engine, two adapters); reverting the input to a source " +
            "aggregate re-forks the assessment path.");

        typeof(CvReviewContext).Namespace.ShouldBe(
            "Jobbliggaren.Application.Resumes.Review.Abstractions",
            "the unified context lives beside the port + result types (CTO-bind PR-4 Q6).");
    }

    [Fact]
    public void Unified_context_is_built_by_exactly_two_adapters()
    {
        // ADR 0093 documents the dual-adapter widening as a DELIBERATE, closed contract
        // ("two contracts explicitly documented as closed ... widened deliberately and
        // narrowly"). A third public factory arm is a design decision, not a drive-by.
        var factories = typeof(CvReviewContext)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(CvReviewContext))
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        factories.ShouldBe(["FromCanonical", "FromParsed"],
            "the unified CvReviewContext is built by exactly the two D8 adapters " +
            "(staging + canonical) — a new arm must be recorded as an architectural " +
            "decision (ADR 0093 closed-contract discipline).");
    }

    /// <summary>
    /// Every type reachable from the declared surface of <paramref name="type"/>:
    /// fields (incl. private/backing), properties, constructor/method parameters and
    /// return types, with Nullable/array/generic-argument unwrapping.
    /// </summary>
    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(declared))
            foreach (var t in Unwrap(field.FieldType))
                yield return t;

        foreach (var property in type.GetProperties(declared))
            foreach (var t in Unwrap(property.PropertyType))
                yield return t;

        foreach (var ctor in type.GetConstructors(declared))
            foreach (var parameter in ctor.GetParameters())
                foreach (var t in Unwrap(parameter.ParameterType))
                    yield return t;

        foreach (var method in type.GetMethods(declared))
        {
            foreach (var t in Unwrap(method.ReturnType))
                yield return t;
            foreach (var parameter in method.GetParameters())
                foreach (var t in Unwrap(parameter.ParameterType))
                    yield return t;
        }
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            type = underlying;

        if (type.IsArray && type.GetElementType() is { } element)
            type = element;

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                foreach (var t in Unwrap(argument))
                    yield return t;
        }

        yield return type;
    }
}
