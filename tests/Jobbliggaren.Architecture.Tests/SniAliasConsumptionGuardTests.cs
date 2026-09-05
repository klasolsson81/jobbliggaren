using Jobbliggaren.Application.CompanyWatches.Abstractions;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1115 / #560 bind 4 — the alias layer is a LOOKUP AID over the picker's filter view, never a
/// mapping between taxonomies. <c>CriterionReferenceAliasTests</c> pins that on the DATA (no SSYK
/// concept id in the asset); this pins it on the CONSUMPTION PATH, which is the half a data check
/// cannot see.
///
/// <para>
/// Why it is needed: <see cref="ICriterionReferenceProvider"/> now carries <c>Aliases</c> for every
/// consumer of the port, including the four existence-validators that decide what may be STORED on
/// a criterion. A future <c>reference.Aliases.TermsFor(...)</c> inside
/// <c>CompanyWatchCriteriaInputValidator</c> would compile, would widen what a saved criterion
/// means, and nothing else in the suite would turn red — the asset would still be innocent.
/// </para>
///
/// <para>
/// The allowlist is deliberately tiny and names each entry's reason. Adding to it is the moment to
/// ask whether the new consumer turns a search hint into a claim.
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

        // The DTO that carries them to the picker, plus the query record declared in the same file.
        "CriterionReferenceDto",
        "SniSectionDto",
        "SniDivisionDto",
        "SniLeafDto",

        // The port and the catalog types themselves.
        "ICriterionReferenceProvider",
        "SniAliasCatalog",
        "SniAlias",
    ];

    [Fact]
    public void OnlyTheReadModelKnowsAliasesExist()
    {
        var consumers = Types.InAssembly(typeof(SniAliasCatalog).Assembly)
            .That()
            .HaveDependencyOn(typeof(SniAliasCatalog).FullName)
            .GetTypes()
            .Select(static t => t.Name)
            .Where(static n => !Allowed.Contains(n, StringComparer.Ordinal))
            // Compiler-generated closure/display classes carry their outer type's dependencies.
            .Where(static n => !n.Contains('<', StringComparison.Ordinal))
            .ToList();

        consumers.ShouldBeEmpty(
            "en ny konsument av alias-katalogen får aliaset att betyda något annat än ett sökstöd. "
            + "Lägg till typen i Allowed ovan bara efter att ha svarat på om den gör sökordet till "
            + "ett påstående — #560 bind 4 förbjuder SNI↔SSYK-mappningen, och det som håller den "
            + "gränsen är att aliaset aldrig når en validator, ett query-predikat eller sni-axeln.");
    }

    [Fact]
    public void TheExistenceValidatorsDoNotReadAliases()
    {
        // Named separately from the allowlist test because THIS is the specific regression that
        // would change what a stored criterion means, and a reader should find it by name.
        var validators = Types.InAssembly(typeof(SniAliasCatalog).Assembly)
            .That()
            .HaveNameEndingWith("Validator")
            .And()
            .HaveDependencyOn(typeof(SniAliasCatalog).FullName)
            .GetTypes();

        validators.ShouldBeEmpty(
            "en validator som läser aliasen skulle låta ett sökord avgöra vad som får SPARAS på ett "
            + "kriterium. Aliasen widenar vad filtret VISAR och ingenting annat.");
    }
}
