using System.Reflection;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Jobbliggaren.Api.IntegrationTests.Auth;
using Jobbliggaren.Api.IntegrationTests.Configuration;
using Jobbliggaren.Api.IntegrationTests.Security;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Pins the set of test classes that own a Testcontainers container or network per test instance.
/// xunit instantiates a test class once per test method, so whatever its instance fields build is
/// built — and, with a lifetime on the class, started — per test. Measured 2026-09-24 on the runner
/// (#1785): that shape carried 79 % of this project's 17.6 minutes for 21 % of its tests.
///
/// <para>
/// The classifier is reflection over this assembly. A test class (one with a
/// <see cref="FactAttribute"/> method) owns a container when an instance field of its own reaches
/// an <see cref="IContainer"/> or <see cref="INetwork"/>: directly, through a generic argument
/// (<c>List&lt;INetwork&gt;</c>), or through the instance fields of a type declared in this assembly
/// (a <c>WebApplicationFactory</c> the class news up itself). A field whose type the class receives as
/// a fixture — an <see cref="IClassFixture{TFixture}"/> argument, or an
/// <see cref="ICollectionFixture{TFixture}"/> of the collection it joins — is shared, not owned, and
/// is not followed.
/// </para>
///
/// <para>
/// The allowlist is an equality, not a floor: a class that drops off it is removed here in the same
/// PR, and a new one is added only with the reason it cannot use <see cref="SharedPostgresFixture"/>,
/// <see cref="SharedVolatileRedisFixture"/> or <see cref="SharedPlainRedisFixture"/>.
/// </para>
/// </summary>
public sealed class OwnContainerPerTestTests
{
    // Each entry names why the class keeps a container of its own.
    private static readonly Type[] Allowed =
    [
        // Stops its Redis to measure the unreachable contract.
        typeof(VolatileRedisConnectionTests),
        // Fills the instance to its memory limit; the contract under test IS the container's own limit.
        typeof(VolatileRedisOutOfMemoryTests),
        // Switches the instance's persistence on (CONFIG SET appendonly) and asserts /data stays
        // empty; a flush does not undo that.
        typeof(VolatileRedisPersistenceProbeTests),
        // Boot a Production host, through a factory of their own, on process-global environment
        // variables; one test each.
        typeof(IdempotentAdminRoleSeederProdBubbleTests),
        typeof(TaxonomySnapshotSeederProdBubbleTests),
        // Builds the boundary topology itself — bridge networks and redis-server containers — per
        // test; the topology is the contract.
        typeof(RedisNetworkContractTests),
        // Tests that stop its Redis.
        typeof(RedisSessionStoreFailureTests),
    ];

    private static readonly Assembly ThisAssembly = typeof(OwnContainerPerTestTests).Assembly;

    [Fact]
    public void Only_the_allowlisted_test_classes_own_a_container_per_test()
    {
        var actual = ThisAssembly
            .GetTypes()
            .Where(HasTests)
            .Where(OwnsAContainer)
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        actual.ShouldBe(
            Allowed.Select(type => type.FullName!).Order(StringComparer.Ordinal).ToList(),
            "a test class that owns a container or network per test is either allowlisted above with "
            + "its reason, or takes SharedPostgresFixture / a SharedRedisFixture (Infrastructure/).");
    }

    private static bool HasTests(Type type) =>
        type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Any(method => method.IsDefined(typeof(FactAttribute), inherit: true));

    private static bool OwnsAContainer(Type testClass)
    {
        var shared = SharedFixtureTypes(testClass);
        var visited = new HashSet<Type>();
        return InstanceFields(testClass).Any(field => !shared.Contains(field.FieldType) && Reaches(field.FieldType, visited));
    }

    private static bool Reaches(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            return false;
        }

        if (typeof(IContainer).IsAssignableFrom(type) || typeof(INetwork).IsAssignableFrom(type))
        {
            return true;
        }

        if (type.GenericTypeArguments.Any(argument => Reaches(argument, visited)))
        {
            return true;
        }

        return type.Assembly == ThisAssembly && InstanceFields(type).Any(field => Reaches(field.FieldType, visited));
    }

    private static IEnumerable<FieldInfo> InstanceFields(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                yield return field;
            }
        }
    }

    /// <summary>The fixture types xunit injects into <paramref name="testClass"/> rather than the class building them.</summary>
    private static HashSet<Type> SharedFixtureTypes(Type testClass)
    {
        var shared = new HashSet<Type>(FixtureArguments(testClass, typeof(IClassFixture<>)));
        var collection = testClass.GetCustomAttribute<CollectionAttribute>()?.Name;
        if (collection is not null)
        {
            var definitions = ThisAssembly.GetTypes()
                .Where(type => type.GetCustomAttribute<CollectionDefinitionAttribute>()?.Name == collection);
            foreach (var definition in definitions)
            {
                shared.UnionWith(FixtureArguments(definition, typeof(ICollectionFixture<>)));
            }
        }

        return shared;
    }

    private static IEnumerable<Type> FixtureArguments(Type type, Type openFixtureInterface) =>
        type.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == openFixtureInterface)
            .Select(candidate => candidate.GenericTypeArguments[0]);
}
