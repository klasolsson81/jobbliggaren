using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1981 (senior-cto-advisor r2 §3, finding 11) — the list of production assemblies names every project under
/// <c>src/</c>, each by a type that assembly holds, and every assembly a rule leaves out is named with its reason. A new
/// project fails here until it joins the list.
/// </summary>
public class ProductionAssembliesTests
{
    private const string Bound =
        "CTO r2 §3 (#1981 finding 11): the architecture rules take their production assemblies from "
        + "ProductionAssemblies alone.";

    public static TheoryData<string> EveryAssembly() => ProductionAssemblies.Names();

    [Fact]
    public void The_list_names_every_project_under_src()
    {
        var projects = Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension);

        ProductionAssemblies.All.Select(assembly => assembly.Name).ShouldBe(projects, ignoreOrder: true, Bound);
    }

    [Fact]
    public void The_list_names_each_assembly_once()
    {
        ProductionAssemblies.All.Select(assembly => assembly.Name).ShouldBeUnique(Bound);
        ProductionAssemblies.All.Select(assembly => assembly.Location).ShouldBeUnique(Bound);
    }

    [Theory]
    [MemberData(nameof(EveryAssembly))]
    public void Each_anchor_is_a_top_level_type_with_a_body_in_the_assembly_it_names(string name)
    {
        var assembly = ProductionAssemblies.Named(name);

        assembly.Anchor.Assembly.GetName().Name.ShouldBe(name);
        assembly.Anchor.IsNested.ShouldBeFalse($"{name}: IL and reflection spell a nested type's name differently");
        assembly.Anchor.IsGenericType.ShouldBeFalse($"{name}: IL and reflection spell a generic type's name differently");
        using var definition = assembly.ReadDefinition();
        definition.MainModule.GetType(assembly.Anchor.FullName).Methods.ShouldContain(
            method => method.HasBody,
            $"{name}: an anchor with no method body cannot show that a rule reads the assembly's IL");
    }

    [Theory]
    [MemberData(nameof(EveryAssembly))]
    public void Every_opener_reads_the_assembly_by_its_name(string name)
    {
        var assembly = ProductionAssemblies.Named(name);

        assembly.ReflectedTypes().ShouldContain(assembly.Anchor);
        assembly.ArchTypes().GetTypes().ShouldContain(assembly.Anchor);
        using var definition = assembly.ReadDefinition();
        definition.Name.Name.ShouldBe(name);
    }

    /// <summary>The name decides the file; an anchor from another assembly is refused by every opener.</summary>
    [Fact]
    public void Every_opener_refuses_an_assembly_whose_anchor_lives_elsewhere()
    {
        var misnamed = new ProductionAssembly(
            ProductionAssemblies.MigrateProvisioning.Name, ProductionAssemblies.Migrate.Anchor);

        Should.Throw<ShouldAssertException>(() => misnamed.ReflectedTypes());
        Should.Throw<ShouldAssertException>(() => misnamed.ArchTypes());
        Should.Throw<ShouldAssertException>(() => misnamed.ReadDefinition().Dispose());
    }

    /// <summary>
    /// Every exclusion in this project, found where it is declared. A content rule may leave an assembly out only with a
    /// reason; the table below is where a new one shows up in review.
    /// </summary>
    [Fact]
    public void Every_exclusion_is_named_here_with_its_reason()
    {
        var exclusions = typeof(ProductionAssembliesTests).Assembly.GetTypes()
            .Where(type => !type.ContainsGenericParameters)
            .SelectMany(type => ProductionAssemblies.DeclaredOn(type).Select(exclusion => (Rule: type.Name, Exclusion: exclusion)))
            .ToList();

        exclusions.ShouldAllBe(declared => !string.IsNullOrWhiteSpace(declared.Exclusion.Reason), Bound);
        exclusions.ShouldAllBe(declared => ProductionAssemblies.All.Contains(declared.Exclusion.Assembly), Bound);
        exclusions.Select(declared => $"{declared.Rule}: {declared.Exclusion.Assembly.Name}").ShouldBe(
            [
                $"{nameof(NoPasswordSymbolTests)}: Jobbliggaren.Migrate",
                $"{nameof(NoPasswordSymbolTests)}: Jobbliggaren.Infrastructure.Mailbox",
            ],
            ignoreOrder: true,
            Bound);
    }

    /// <summary>
    /// Outside <see cref="ProductionAssemblies"/>, the IL of this project holds no array of, no generic type built over,
    /// and no LINQ filter on <see cref="Assembly"/>, <see cref="ProductionAssembly"/> or <see cref="AssemblyDefinition"/>.
    /// The scan does not see a theory of marker types, or handles held in separate fields and joined later.
    /// </summary>
    [Fact]
    public void Outside_the_source_the_IL_holds_no_array_generic_or_filter_of_assembly_handles()
    {
        var sites = HandBuiltSets();

        sites.Where(site => site.StartsWith(Fixture + ".", StringComparison.Ordinal)).ShouldBe(
            [
                $"{Fixture}.{nameof(AnAssemblyArray)}: Assembly[]",
                $"{Fixture}.{nameof(AnAssemblyList)}: List<Assembly>",
                $"{Fixture}.{nameof(AHandleArray)}: ProductionAssembly[]",
                $"{Fixture}.{nameof(AHandleList)}: List<ProductionAssembly>",
                $"{Fixture}.{nameof(ADefinitionList)}: List<AssemblyDefinition>",
                $"{Fixture}.{nameof(AFilteredSet)}: Where<ProductionAssembly>",
            ],
            ignoreOrder: true,
            "the scan must see each shape it forbids, here and nowhere else in this class");
        sites.Where(site => !site.StartsWith(Fixture + ".", StringComparison.Ordinal)).ShouldBeEmpty(Bound);
    }

    private static readonly string Fixture = typeof(ProductionAssembliesTests).FullName!;

    private static readonly string[] FilteringMethods =
        ["Where", "Except", "ExceptBy", "Skip", "SkipWhile", "SkipLast", "Take", "TakeWhile", "TakeLast"];

    private static Assembly[] AnAssemblyArray() => [ProductionAssemblies.Domain.Assembly];

    private static List<Assembly> AnAssemblyList() => [ProductionAssemblies.Domain.Assembly];

    private static ProductionAssembly[] AHandleArray() => [ProductionAssemblies.Domain];

    private static List<ProductionAssembly> AHandleList() => [ProductionAssemblies.Domain];

    private static List<AssemblyDefinition> ADefinitionList() => [];

    private static IEnumerable<ProductionAssembly> AFilteredSet() =>
        ProductionAssemblies.All.Where(assembly => assembly != ProductionAssemblies.Api);

    private static List<string> HandBuiltSets()
    {
        string[] handles = [typeof(Assembly).FullName!, typeof(ProductionAssembly).FullName!, typeof(AssemblyDefinition).FullName!];
        string[] source = [typeof(ProductionAssemblies).FullName!, typeof(ProductionAssembly).FullName!, typeof(Exclusion).FullName!];
        using var tests = AssemblyDefinition.ReadAssembly(typeof(ProductionAssembliesTests).Assembly.Location);
        var sites = new List<string>();
        foreach (var method in tests.MainModule.GetTypes()
                     .Where(type => !source.Contains(ProductionTypeSweep.OwnerOf(type).FullName))
                     .SelectMany(type => type.Methods)
                     .Where(method => method.HasBody))
        {
            var owner = JobAdLifecycleReadRegistryTests.LogicalOwnerKey(method);
            // A delegate's constructor takes (object, IntPtr); a lambda over a handle is not a set.
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Newarr
                    && instruction.Operand is TypeReference element && handles.Contains(element.FullName))
                    sites.Add($"{owner}: {element.Name}[]");
                else if (instruction.OpCode == OpCodes.Newobj && instruction.Operand is MethodReference constructor
                         && constructor.DeclaringType is GenericInstanceType generic
                         && constructor.Parameters is not [_, { ParameterType.FullName: "System.IntPtr" }]
                         && generic.GenericArguments.FirstOrDefault(argument => handles.Contains(argument.FullName)) is { } wrapped)
                    sites.Add($"{owner}: {generic.ElementType.Name.Split('`')[0]}<{wrapped.Name}>");
                else if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                         && instruction.Operand is GenericInstanceMethod { DeclaringType.FullName: "System.Linq.Enumerable" } filter
                         && FilteringMethods.Contains(filter.Name)
                         && handles.Contains(filter.GenericArguments[0].FullName))
                    sites.Add($"{owner}: {filter.Name}<{filter.GenericArguments[0].Name}>");
            }
        }

        return sites;
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
