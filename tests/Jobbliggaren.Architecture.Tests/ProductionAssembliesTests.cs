using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1981 (senior-cto-advisor r2 §3, finding 11) — the list every architecture rule reads names every project under
/// <c>src/</c>, each by a type that assembly holds, and every assembly a rule leaves out is named with its reason. A new
/// project fails here until it joins the list, and then every rule reads it.
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
        var exclusions = DeclaredExclusions().ToList();

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
    /// No test outside <see cref="ProductionAssemblies"/> builds a set of assemblies of its own, which is how
    /// TaxonomyAclLayerTests came to scan everything but the Mailbox. The IL of this project holds no
    /// <see cref="Assembly"/> array and no generic type built over <see cref="Assembly"/> outside the source, and an
    /// <see cref="Exclusion"/> is built only in a static initialiser, where the inventory above finds it. The scan does
    /// not see a theory of marker types, or handles held in separate fields and joined later.
    /// </summary>
    [Fact]
    public void No_test_builds_a_set_of_assemblies_of_its_own()
    {
        var sites = HandBuiltSets();

        sites.ShouldContain($"{Fixture}.{nameof(AHandBuiltSet)}: Assembly[]", "the scan must see an Assembly array");
        sites.ShouldContain($"{Fixture}.{nameof(AHandBuiltList)}: List<Assembly>", "the scan must see a generic set");
        sites.ShouldContain($"{Fixture}.{nameof(AnExclusionOutsideAnInitialiser)}: Exclusion", "the scan must see an Exclusion");
        sites.Where(site => !site.StartsWith(Fixture + ".", StringComparison.Ordinal)).ShouldBeEmpty(Bound);
    }

    private static readonly string Fixture = typeof(ProductionAssembliesTests).FullName!;

    private static Assembly[] AHandBuiltSet() => [ProductionAssemblies.Domain.Assembly, ProductionAssemblies.Api.Assembly];

    private static List<Assembly> AHandBuiltList() => [ProductionAssemblies.Domain.Assembly];

    private static Exclusion AnExclusionOutsideAnInitialiser() => new(ProductionAssemblies.Domain, "a fixture for the scan");

    private static List<string> HandBuiltSets()
    {
        using var tests = AssemblyDefinition.ReadAssembly(typeof(ProductionAssembliesTests).Assembly.Location);
        var sites = new List<string>();
        string[] source = [typeof(ProductionAssemblies).FullName!, typeof(ProductionAssembly).FullName!, typeof(Exclusion).FullName!];
        foreach (var method in tests.MainModule.GetTypes()
                     .Where(type => !source.Contains(ProductionTypeSweep.OwnerOf(type).FullName))
                     .SelectMany(type => type.Methods)
                     .Where(method => method.HasBody))
        {
            var owner = JobAdLifecycleReadRegistryTests.LogicalOwnerKey(method);
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Newarr
                    && instruction.Operand is TypeReference { FullName: "System.Reflection.Assembly" })
                    sites.Add($"{owner}: Assembly[]");
                else if (instruction.OpCode == OpCodes.Newobj && instruction.Operand is MethodReference constructor)
                {
                    // A delegate's constructor takes (object, IntPtr); a lambda returning an Assembly is not a set.
                    var delegateConstructor = constructor.Parameters is [_, { ParameterType.FullName: "System.IntPtr" }];
                    if (constructor.DeclaringType is GenericInstanceType generic && !delegateConstructor
                        && generic.GenericArguments.Any(argument => argument.FullName == typeof(Assembly).FullName))
                        sites.Add($"{owner}: {generic.ElementType.Name.Split('`')[0]}<Assembly>");
                    else if (constructor.DeclaringType.FullName == typeof(Exclusion).FullName && !(method.IsConstructor && method.IsStatic))
                        sites.Add($"{owner}: Exclusion");
                }
            }
        }

        return sites;
    }

    private static IEnumerable<(string Rule, Exclusion Exclusion)> DeclaredExclusions()
    {
        const BindingFlags declared = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var type in typeof(ProductionAssembliesTests).Assembly.GetTypes().Where(type => !type.ContainsGenericParameters))
        {
            foreach (var field in type.GetFields(declared))
            {
                if (field.FieldType == typeof(Exclusion))
                    yield return (type.Name, (Exclusion)field.GetValue(null)!);
                else if (field.FieldType == typeof(Exclusion[]))
                {
                    foreach (var exclusion in (Exclusion[])field.GetValue(null)!)
                        yield return (type.Name, exclusion);
                }
            }
        }
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
