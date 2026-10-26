using System.Reflection;
using System.Runtime.CompilerServices;
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
            [],
            ignoreOrder: true,
            Bound);
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
