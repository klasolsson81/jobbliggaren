using System.Reflection;
using Mono.Cecil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1981 (senior-cto-advisor r2 §3, finding 11) — the one list of production assemblies the architecture rules read.
/// It names every project under <c>src/</c>, and <see cref="ProductionAssembliesTests"/> pins it against the build
/// files. A rule about who may hold or call a type finds nothing in an assembly that cannot reference that type, so
/// reading it claims nothing; only a rule about what an assembly contains may leave one out, through an
/// <see cref="Exclusion"/> declared beside that rule.
/// </summary>
internal static class ProductionAssemblies
{
    internal static readonly ProductionAssembly Domain =
        new("Jobbliggaren.Domain", typeof(Jobbliggaren.Domain.JobSeekers.JobSeeker));

    internal static readonly ProductionAssembly Application =
        new("Jobbliggaren.Application", typeof(Jobbliggaren.Application.AssemblyMarker));

    internal static readonly ProductionAssembly Infrastructure =
        new("Jobbliggaren.Infrastructure", typeof(Jobbliggaren.Infrastructure.AssemblyMarker));

    internal static readonly ProductionAssembly InfrastructureMailbox =
        new("Jobbliggaren.Infrastructure.Mailbox", typeof(Jobbliggaren.Infrastructure.Mailbox.Dmarc.DmarcMailboxCollectionExtensions));

    internal static readonly ProductionAssembly Api =
        new("Jobbliggaren.Api", typeof(Jobbliggaren.Api.Endpoints.AuthProblem));

    internal static readonly ProductionAssembly Worker =
        new("Jobbliggaren.Worker", typeof(Jobbliggaren.Worker.Auditing.WorkerSystemUser));

    internal static readonly ProductionAssembly Migrate =
        new("Jobbliggaren.Migrate", typeof(Jobbliggaren.Migrate.ConnectionStringFactory));

    /// <summary>Its types sit in the <c>Jobbliggaren.Migrate</c> namespace; the assembly is its own.</summary>
    internal static readonly ProductionAssembly MigrateProvisioning =
        new("Jobbliggaren.Migrate.Provisioning", typeof(Jobbliggaren.Migrate.PhaseASchemaGrants));

    /// <summary>Declared after the fields it lists, because static initialisers run in source order.</summary>
    internal static readonly IReadOnlyList<ProductionAssembly> All =
    [
        Domain,
        Application,
        Infrastructure,
        InfrastructureMailbox,
        Api,
        Worker,
        Migrate,
        MigrateProvisioning,
    ];

    internal static ProductionAssembly Named(string name) =>
        All.SingleOrDefault(assembly => assembly.Name == name)
        ?? throw new ArgumentOutOfRangeException(nameof(name), name, "Not a production assembly.");

    /// <summary>Every production type, read by reflection.</summary>
    internal static IEnumerable<Type> ReflectedTypes() => All.SelectMany(assembly => assembly.ReflectedTypes());

    /// <summary>The theory rows of a rule about what an assembly contains: every production assembly.</summary>
    internal static TheoryData<string> Names() => Rows([]);

    /// <summary>Every production assembly but the exclusions <typeparamref name="TRule"/> declares as static fields.</summary>
    internal static TheoryData<string> NamesExcept<TRule>() => Rows(DeclaredOn(RuleOf<TRule>()));

    /// <summary>The assemblies <typeparamref name="TRule"/> excludes.</summary>
    internal static TheoryData<string> ExcludedBy<TRule>()
    {
        var rows = new TheoryData<string>();
        foreach (var exclusion in DeclaredOn(RuleOf<TRule>()))
            rows.Add(exclusion.Assembly.Name);
        return rows;
    }

    private static Type RuleOf<TRule>() =>
        typeof(TRule).IsGenericType
            ? throw new ArgumentException($"{typeof(TRule)} is generic; a rule's exclusions are declared on the rule itself.")
            : typeof(TRule);

    /// <summary>The static <see cref="Exclusion"/> and <see cref="Exclusion"/>[] fields <paramref name="rule"/> declares.</summary>
    internal static IReadOnlyList<Exclusion> DeclaredOn(Type rule)
    {
        const BindingFlags declared = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var exclusions = new List<Exclusion>();
        foreach (var field in rule.GetFields(declared))
        {
            if (field.FieldType == typeof(Exclusion))
                exclusions.Add((Exclusion)field.GetValue(null)!);
            else if (field.FieldType == typeof(Exclusion[]))
                exclusions.AddRange((Exclusion[])field.GetValue(null)!);
        }

        return exclusions;
    }

    private static TheoryData<string> Rows(IReadOnlyList<Exclusion> except)
    {
        var rows = new TheoryData<string>();
        foreach (var assembly in All.Where(assembly => except.All(exclusion => exclusion.Assembly != assembly)))
            rows.Add(assembly.Name);
        return rows;
    }

    /// <summary>The check every opener runs before it hands anything out: the assembly it read holds the anchor.</summary>
    internal static void ShouldHoldTheAnchor(string name, Type anchor, bool held) =>
        held.ShouldBeTrue(
            $"{name} was read without its anchor {anchor.FullName}, so a rule over it would pass over the wrong file "
            + "or over nothing");
}

/// <summary>
/// One project under <c>src/</c>, found by its <see cref="Name"/>, and a top-level type in it. The anchor is what each
/// opener proves it read before it hands out a type; it never decides which file is read.
/// </summary>
internal sealed record ProductionAssembly(string Name, Type Anchor)
{
    internal Assembly Assembly => System.Reflection.Assembly.Load(new AssemblyName(Name));

    internal string Location => Assembly.Location;

    /// <summary>Plain <see cref="Assembly.GetTypes"/>: a type that fails to load fails the rule rather than leaving it.</summary>
    internal Type[] ReflectedTypes()
    {
        var types = Assembly.GetTypes();
        ProductionAssemblies.ShouldHoldTheAnchor(Name, Anchor, types.Contains(Anchor));
        return types;
    }

    /// <summary>The assembly as IL. The caller disposes it.</summary>
    internal AssemblyDefinition ReadDefinition(ReaderParameters? parameters = null)
    {
        var definition = AssemblyDefinition.ReadAssembly(Location, parameters ?? new ReaderParameters());
        var held = definition.MainModule.GetType(Anchor.FullName) is not null;
        if (!held)
            definition.Dispose();
        ProductionAssemblies.ShouldHoldTheAnchor(Name, Anchor, held);
        return definition;
    }

    /// <summary>The assembly as NetArchTest reads it, which maps types through its own loader.</summary>
    internal NetArchTest.Rules.Types ArchTypes()
    {
        var types = NetArchTest.Rules.Types.InAssembly(Assembly);
        ProductionAssemblies.ShouldHoldTheAnchor(Name, Anchor, types.GetTypes().Contains(Anchor));
        return types;
    }

    public override string ToString() => Name;
}

/// <summary>
/// A production assembly a content rule leaves out, and why. It is declared as a static field of the rule it argues
/// for, and the rule that declares it owes a check that it is still needed.
/// </summary>
internal sealed record Exclusion(ProductionAssembly Assembly, string Reason);
