using System.Reflection;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Microsoft.Extensions.Caching.Distributed;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1735 — which instance a Redis-backed type can reach is decided by its CONSTRUCTOR, and this pins
/// both directions: the types whose keys must not outlive their TTL take
/// <see cref="VolatileRedisConnection"/> and no route to the durable instance, and nothing else takes it.
/// </summary>
/// <remarks>
/// A constructor-injection scan, the shape <see cref="ErasurePortInjectionRadiusTests"/> records: a
/// minimal-API delegate parameter is a second injection form no constructor scan can see, but
/// <see cref="VolatileRedisConnection"/> is internal to Infrastructure and the Api has no
/// <c>InternalsVisibleTo</c>, so an endpoint cannot name it.
/// </remarks>
public class VolatileRedisIsolationTests
{
    private static readonly Assembly[] OwnedAssemblies =
    [
        typeof(Jobbliggaren.Application.AssemblyMarker).Assembly,
        typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly,
        typeof(Jobbliggaren.Api.Endpoints.AdminJobAdsEndpoints).Assembly,
        typeof(Jobbliggaren.Worker.Auditing.WorkerSystemUser).Assembly,
    ];

    /// <summary>The routes to the DURABLE instance: the registered multiplexer and the cache built on it.</summary>
    private static readonly Type[] DurableRoutes = [typeof(IConnectionMultiplexer), typeof(IDistributedCache)];

    // A type from that assembly, or one carrying it: Task<IDatabase> lives in CoreLib and hands out a database.
    private static bool Mentions(Type type, Assembly assembly) =>
        type.Assembly == assembly
        || (type.HasElementType && Mentions(type.GetElementType()!, assembly))
        || type.GetGenericArguments().Any(argument => Mentions(argument, assembly));

    private static List<Type> ConstructorParameterTypes(Type type) =>
        type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

    [Fact]
    public void VolatileRedisConnection_IsConstructorInjected_OnlyByTheTwoStoresAndItsHealthCheck()
    {
        var consumers = OwnedAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => ConstructorParameterTypes(t).Contains(typeof(VolatileRedisConnection)))
            .Select(t => t.FullName!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        string[] expected =
        [
            typeof(RedisLoginChallengeStore).FullName!,
            typeof(RedisRateBudget).FullName!,
            typeof(VolatileRedisHealthCheck).FullName!,
        ];

        consumers.ShouldBe(expected, ignoreOrder: true,
            customMessage: "A new consumer puts a new key class on the instance that forgets everything at "
                           + "a restart. Decide that on purpose (ADR 0142 D1) before adding it here. Found: "
                           + string.Join(", ", consumers));
    }

    [Theory]
    [InlineData(typeof(RedisLoginChallengeStore))]
    [InlineData(typeof(RedisRateBudget))]
    public void VolatileStore_TakesNoRouteToTheDurableInstance(Type store)
    {
        ConstructorParameterTypes(store).ShouldNotContain(p => DurableRoutes.Contains(p));
    }

    [Theory]
    [InlineData(typeof(RedisSessionStore))]
    [InlineData(typeof(RedisCooldownGate))]
    public void DurableConsumer_TakesADurableRoute_AndNotTheVolatileConnection(Type consumer)
    {
        // The control for the theory above: the same scan, on the types that DO stay durable, finds their route.
        var parameters = ConstructorParameterTypes(consumer);

        parameters.ShouldContain(p => DurableRoutes.Contains(p));
        parameters.ShouldNotContain(typeof(VolatileRedisConnection));
    }

    [Fact]
    public void VolatileRedisConnection_ExposesNoStackExchangeRedisType_OutsideExecuteAsync()
    {
        // The multiplexer is private and never registered: a member handing it out would be a second route
        // to the database, and that route would not translate faults.
        var redisAssembly = typeof(IConnectionMultiplexer).Assembly;
        var leaks = typeof(VolatileRedisConnection)
            .GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly)
            .Where(m => m switch
            {
                FieldInfo f => !f.IsPrivate && Mentions(f.FieldType, redisAssembly),
                PropertyInfo p => p.GetMethod is { IsPrivate: false } && Mentions(p.PropertyType, redisAssembly),
                MethodInfo m2 => !m2.IsPrivate && Mentions(m2.ReturnType, redisAssembly),
                _ => false,
            })
            .Select(m => m.Name)
            .ToList();

        leaks.ShouldBeEmpty();
        typeof(IConnectionMultiplexer).IsAssignableFrom(typeof(VolatileRedisConnection)).ShouldBeFalse();
    }
}
