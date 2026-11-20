using System.Collections;
using System.Reflection;
using Jobbliggaren.Application.Common.Abstractions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1663 PR 3 (ADR 0173 D7, ADR 0175 D8; senior-cto-advisor's ruling of 2026-11-20) — the bell's precedence runs one way:
/// match, then a followed company's ad, then a Branschbevakning's. A lower source leaves out what every higher source holds,
/// through that source's <c>HeldAdIds</c>, and no source reads a lower one, so the sources form a chain without a cycle
/// (the Acyclic Dependencies Principle). ADR 0173 D7 left this to the docblock and the review; with three sources it is
/// pinned here, on the IL of each source and its compiler-generated types (state machines and closures).
/// </summary>
public sealed class NotificationFeedSourcePrecedenceTests
{
    private const string HeldAdIds = "HeldAdIds";

    [Fact]
    public void Each_source_leaves_out_exactly_what_every_higher_source_holds_and_reads_no_lower_one()
    {
        var order = SourceTypesInReadOrder();
        order.Count.ShouldBeGreaterThanOrEqualTo(3, "the match, the follow and the Branschbevakning source");
        var rank = order.Select((type, index) => (type.FullName!, index)).ToDictionary(p => p.Item1, p => p.index);

        using var module = ModuleDefinition.ReadModule(typeof(IAppDbContext).Assembly.Location);

        for (var k = 0; k < order.Count; k++)
        {
            var source = module.GetType(order[k].FullName!);
            source.ShouldNotBeNull($"{order[k].FullName} in the module");

            var held = MethodsIncludingNestedTypes(source)
                .Where(method => method.HasBody)
                .SelectMany(method => method.Body.Instructions)
                .Where(instruction => instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                .Select(instruction => instruction.Operand)
                .OfType<MethodReference>()
                .Where(target => target.Name == HeldAdIds && rank.ContainsKey(target.DeclaringType.FullName))
                .Select(target => rank[target.DeclaringType.FullName])
                .Distinct()
                .Order()
                .ToList();

            held.ShouldBe(
                Enumerable.Range(0, k).ToList(),
                $"{order[k].Name} must leave out what every higher source holds, and read no source below it");
        }
    }

    // NotificationFeedSources is internal to Application, so its list is read by reflection rather than referenced.
    private static List<Type> SourceTypesInReadOrder()
    {
        var registry = typeof(IAppDbContext).Assembly.GetType(
            "Jobbliggaren.Application.NotificationFeed.Sources.NotificationFeedSources", throwOnError: true)!;
        var all = registry.GetProperty("All", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)
            .ShouldBeAssignableTo<IEnumerable>()!;
        return [.. all.Cast<object>().Select(source => source.GetType())];
    }

    private static IEnumerable<MethodDefinition> MethodsIncludingNestedTypes(TypeDefinition type) =>
        type.Methods.Concat(type.NestedTypes.SelectMany(MethodsIncludingNestedTypes));
}
