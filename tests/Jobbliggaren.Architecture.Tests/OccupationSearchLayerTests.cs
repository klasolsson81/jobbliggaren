using Jobbliggaren.Application.JobAds.Queries.SearchOccupations;
using Jobbliggaren.Infrastructure.Taxonomy;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #2198 (ADR 0180) — the occupation pickers' search respects the layers, adds no index, and never
/// logs or forwards the term.
/// <list type="bullet">
/// <item>The port, its rows and the row kind are Application abstractions; the index, the rule and
/// the searcher are internal to Infrastructure.</item>
/// <item>The frozen occupation-name→ssyk-level-4 map has exactly one runtime reader: the loader,
/// whose only dependent is <c>OccupationNameIndex</c>. A second dependent would be a second
/// occupation index beside the shared one, which is what "no third mechanism" forbids.</item>
/// <item>None of the types on the search path, nor the deriver beside it, depends on
/// <c>Microsoft.Extensions.Logging</c> or <c>System.Net.Http</c>, so the term is not logged by them
/// and does not leave the process (ADR 0043 Beslut A).</item>
/// </list>
/// Each dependency pin normalises a compiler-generated type (an async state machine, a lambda
/// closure) to its outermost declaring type, so a reference made inside an async method still
/// counts against the class that wrote it.
/// </summary>
public class OccupationSearchLayerTests
{
    private const string TaxonomyNamespace = "Jobbliggaren.Infrastructure.Taxonomy";

    private static readonly System.Reflection.Assembly Application =
        typeof(Jobbliggaren.Application.AssemblyMarker).Assembly;

    private static readonly System.Reflection.Assembly Infrastructure =
        typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

    [Fact]
    public void The_port_its_rows_and_the_row_kind_are_in_the_Application_layer()
    {
        typeof(Jobbliggaren.Application.JobAds.Abstractions.IOccupationSearcher).Assembly.ShouldBe(Application);
        typeof(Jobbliggaren.Application.JobAds.Abstractions.OccupationSearchResult).Assembly.ShouldBe(Application);
        typeof(Jobbliggaren.Application.JobAds.Abstractions.OccupationSearchRow).Assembly.ShouldBe(Application);
        typeof(Jobbliggaren.Application.JobAds.Abstractions.OccupationSearchVariant).Assembly.ShouldBe(Application);
        typeof(Jobbliggaren.Application.JobAds.Abstractions.OccupationSearchRowKind).Assembly.ShouldBe(Application);
        typeof(Jobbliggaren.Application.JobAds.Abstractions.IOccupationSearcher).Assembly.ShouldNotBe(Infrastructure);
    }

    [Fact]
    public void The_index_the_rule_and_the_searcher_are_internal_to_Infrastructure()
    {
        Type[] types = [typeof(OccupationNameIndex), typeof(OccupationSearchRule), typeof(OccupationSearcher)];

        types.ShouldAllBe(t => t.Assembly == Infrastructure && t.Namespace == TaxonomyNamespace);
        types.Where(t => t.IsPublic).Select(t => t.FullName).ShouldBeEmpty();
    }

    [Fact]
    public void The_frozen_map_loader_has_exactly_one_dependent_the_shared_index()
    {
        const string loader = $"{TaxonomyNamespace}.OccupationGroupMappingLoader";

        var dependents = OutermostTypesDependingOn(Types.InAssembly(Infrastructure), loader)
            .Where(t => t.FullName != loader);

        dependents.ShouldBe([typeof(OccupationNameIndex)]);
    }

    [Theory]
    [InlineData("Microsoft.Extensions.Logging")]
    [InlineData("System.Net.Http")]
    public void Nothing_on_the_search_path_depends_on(string forbiddenNamespace)
    {
        Type[] searchPath =
        [
            typeof(OccupationNameIndex), typeof(OccupationSearchRule), typeof(OccupationSearcher),
            typeof(OccupationCodeDeriver), typeof(SearchOccupationsQueryHandler),
        ];

        var offenders = ProductionAssemblies.All
            .SelectMany(production => OutermostTypesDependingOn(production.ArchTypes(), forbiddenNamespace))
            .Where(searchPath.Contains)
            .Select(t => t.FullName)
            .ToList();

        offenders.ShouldBeEmpty(
            $"the occupation search path must not depend on {forbiddenNamespace}: the term is never " +
            "logged and never leaves the process (ADR 0180).");
    }

    // The distinct outermost declaring types of every type in the assembly that depends on the given
    // type or namespace — nested and compiler-generated types fold into the class that declares them.
    private static List<Type> OutermostTypesDependingOn(Types types, string dependency) =>
        types
            .That()
            .HaveDependencyOn(dependency)
            .GetTypes()
            .Select(Outermost)
            .Distinct()
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

    private static Type Outermost(Type type)
    {
        while (type.DeclaringType is not null)
            type = type.DeclaringType;
        return type;
    }
}
