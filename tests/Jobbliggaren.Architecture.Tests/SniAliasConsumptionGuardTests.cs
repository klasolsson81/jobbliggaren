using System.Reflection;
using System.Runtime.CompilerServices;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1115 / #560 bind 4 — the alias layer is a LOOKUP AID over the picker's filter view, never a
/// mapping between taxonomies. <c>CriterionReferenceAliasTests</c> pins that on the DATA (no SSYK
/// concept id in the asset); this pins it on the consumption path.
///
/// <para>
/// Why it is needed: <see cref="ICriterionReferenceProvider"/> now carries <c>Aliases</c> for every
/// consumer of the port, including the existence-validators that decide what may be STORED on a
/// criterion, and the register query that decides which companies come back. A future
/// <c>reference.Aliases.TermsFor(...)</c> in either would compile, would widen what a saved
/// criterion means, and nothing else in the suite would turn red — the asset would still be
/// innocent.
/// </para>
///
/// <para>
/// <b>Both assemblies, and compiler-generated types rolled up to their owner.</b> Two holes in the
/// first version of this guard were found by review, and each let through the shape that would
/// actually occur: scanning only Application missed the raw register predicate in
/// <c>CompanyWatchBrowseQuery</c>, and discarding names containing <c>&lt;</c> missed the idiomatic
/// FluentValidation form, where the dependency lands in a <c>&lt;&gt;c__DisplayClass</c> whose name
/// never ends in "Validator". A guard that catches only the naive form is worse than none, because
/// it reads as covered.
/// </para>
/// </summary>
public class SniAliasConsumptionGuardTests
{
    /// <summary>The only types that may know aliases exist, and why each one may.</summary>
    private static readonly string[] Allowed =
    [
        // Projects the catalog onto the picker tree — the read model, and the only place the terms
        // reach a wire DTO at all.
        "GetCriterionReferenceQueryHandler",

        // The DTOs that carry them to the picker.
        "CriterionReferenceDto",
        "SniSectionDto",
        "SniDivisionDto",
        "SniLeafDto",

        // The port and the catalog types themselves.
        "ICriterionReferenceProvider",
        "SniAliasCatalog",
        "SniAlias",

        // Infrastructure: the loader that reads the embedded asset, its deserialisation form, and
        // the provider that holds both catalogs and cross-checks them at host build.
        "CriterionReferenceLoader",
        "CriterionReferenceProvider",
        "SniAliasFile",
    ];

    private static readonly Assembly[] Scanned =
    [
        typeof(SniAliasCatalog).Assembly,
        typeof(Jobbliggaren.Infrastructure.CompanyRegister.Reference.CriterionReferenceProvider).Assembly,
    ];

    private static bool IsCompilerGenerated(Type t) =>
        t.Name.StartsWith('<') || t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    /// <summary>
    /// The type a reader would blame. A lambda's captured state lives in a nested
    /// <c>&lt;&gt;c__DisplayClass</c>, so the dependency is recorded against a type whose name says
    /// nothing about who wrote it; walking out to the first non-generated declaring type restores
    /// the answer to "which class reads aliases".
    /// </summary>
    private static string LogicalOwner(Type t)
    {
        var type = t;
        while (IsCompilerGenerated(type) && type.DeclaringType is not null)
            type = type.DeclaringType;
        return type.Name;
    }

    /// <summary>Every type that depends on the catalog, rolled up to its logical owner. Returned
    /// UNFILTERED so callers can prove the query still sees something — a NetArchTest query that
    /// silently matches nothing makes every <c>ShouldBeEmpty</c> below pass for the wrong reason.</summary>
    private static List<string> AllConsumers() =>
        Scanned
            .SelectMany(asm => Types.InAssembly(asm)
                .That()
                .HaveDependencyOn(typeof(SniAliasCatalog).FullName)
                .GetTypes())
            .Select(LogicalOwner)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static List<string> ConsumersOutsideTheAllowlist() =>
        AllConsumers()
            .Where(n => !Allowed.Contains(n, StringComparer.Ordinal))
            .ToList();

    [Fact]
    public void OnlyTheReadModelAndTheLoaderKnowAliasesExist()
    {
        // POSITIVE CONTROL on the instrument, not merely a floor: both assertions in this file are
        // ShouldBeEmpty over a NetArchTest query, so a query that stops matching — a rename, a
        // namespace move, a change in how HaveDependencyOn reads IL — turns the whole guard green
        // and silent. Anchoring a known consumer makes the reader prove it can still see.
        AllConsumers().ShouldContain("GetCriterionReferenceQueryHandler");

        ConsumersOutsideTheAllowlist().ShouldBeEmpty(
            "en ny konsument av alias-katalogen får aliaset att betyda något annat än ett sökstöd. "
            + "Lägg till typen i Allowed ovan bara efter att ha svarat på om den gör sökordet till "
            + "ett påstående — #560 bind 4 förbjuder SNI↔SSYK-mappningen, och det som håller den "
            + "gränsen är att aliaset aldrig når en validator, ett query-predikat eller sni-axeln.");
    }

    [Fact]
    public void NeitherTheValidatorsNorTheRegisterQueryReadAliases()
    {
        // Named separately from the allowlist test because THESE are the two specific regressions
        // that would change what a stored criterion means or which companies it returns, and a
        // reader should find them by name. The owner rollup is what makes a lambda-borne read count.
        // Same anti-vacuity reason as above, and sharper here: this set is filtered by TWO
        // predicates, so it passes if EITHER finds nothing.
        AllConsumers().ShouldContain("CriterionReferenceProvider");

        var offenders = ConsumersOutsideTheAllowlist()
            .Where(n => n.EndsWith("Validator", StringComparison.Ordinal)
                || n.Contains("Query", StringComparison.Ordinal))
            .ToList();

        offenders.ShouldBeEmpty(
            "en validator eller ett register-query som läser aliasen skulle låta ett sökord avgöra "
            + "vad som får SPARAS på ett kriterium, eller vilka företag som kommer tillbaka. "
            + "Aliasen widenar vad filtret VISAR och ingenting annat.");
    }
}
