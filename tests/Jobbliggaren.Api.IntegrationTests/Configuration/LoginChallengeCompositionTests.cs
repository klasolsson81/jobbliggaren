using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// The login challenge's consumer lives in the Api composition only (ADR 0142 D2, ADR 0023): its store
/// protects with the Api's Data-Protection keyring and runs on the Api's volatile Redis connection. No other guard
/// catches a mis-registration — WorkerLayerTests scans the Worker assembly and the consumer lives in
/// Infrastructure — so this pair is the pin. The positive half is the control the negative half is measured
/// against: two absences with no presence beside them would pass against a build that registers nothing.
/// </summary>
public sealed class LoginChallengeCompositionTests
{
    private static readonly string VolatileRedisKey =
        $"ConnectionStrings:{DependencyInjection.VolatileRedisConnectionStringName}";

    // Read at registration time, and absence throws. The durable Redis is ALWAYS present here, so every
    // refusal below is a refusal with a working `ConnectionStrings:Redis` to fall back on.
    private static Dictionary<string, string?> DurableOnly() => new()
    {
        ["ConnectionStrings:Postgres"] = "Host=localhost;Database=jobbliggaren;Username=x;Password=y",
        ["ConnectionStrings:Redis"] = "localhost:6379",
    };

    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IConfiguration ApiConfiguration()
    {
        var values = DurableOnly();
        values[VolatileRedisKey] = "localhost:6381";
        return Build(values);
    }

    [Fact]
    public void AddIdentityAndSessions_registers_the_dispatcher_its_consumer_the_store_the_budget_and_the_inbox_proof()
    {
        var services = new ServiceCollection();

        services.AddIdentityAndSessions(ApiConfiguration());

        services.ShouldContain(d => d.ServiceType == typeof(ILoginChallengeDispatcher));
        services.ShouldContain(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(LoginChallengeDispatchService));
        services.ShouldContain(d => d.ServiceType == typeof(ILoginChallengeStore));
        services.ShouldContain(d => d.ServiceType == typeof(IRateBudget));
        services.ShouldContain(d => d.ServiceType == typeof(IInboxProofRecorder));
        services.ShouldContain(d => d.ServiceType == typeof(VolatileRedisConnection));
    }

    [Fact]
    public void AddCoreIdentityForWorker_registers_none_of_them_and_needs_no_volatile_connection_string()
    {
        var services = new ServiceCollection();

        // DurableOnly on purpose: the deploy stack hands the key to `api` alone, so the Worker composes
        // without it, and a refusal leaking into this composition would take the Worker down.
        services.AddCoreIdentityForWorker(Build(DurableOnly()));

        services.ShouldNotContain(d => d.ServiceType == typeof(ILoginChallengeDispatcher));
        services.ShouldNotContain(d => d.ImplementationType == typeof(LoginChallengeDispatchService));
        services.ShouldNotContain(d => d.ServiceType == typeof(ILoginChallengeStore));
        services.ShouldNotContain(d => d.ServiceType == typeof(IRateBudget));
        services.ShouldNotContain(d => d.ServiceType == typeof(IInboxProofRecorder));
        services.ShouldNotContain(d => d.ServiceType == typeof(VolatileRedisConnection));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddIdentityAndSessions_without_a_volatile_connection_string_refuses_and_never_falls_back(string? value)
    {
        // "" is the form compose renders for an unset variable, and `??` lets it through.
        var values = DurableOnly();
        if (value is not null)
            values[VolatileRedisKey] = value;

        var refusal = Should.Throw<InvalidOperationException>(
            () => new ServiceCollection().AddIdentityAndSessions(Build(values)));

        refusal.Message.ShouldContain(VolatileRedisKey);
    }

    [Fact]
    public void AddIdentityAndSessions_registers_exactly_one_IConnectionMultiplexer_keyed_or_not()
    {
        // The volatile connection is a TYPE, never a second IConnectionMultiplexer: an unkeyed second
        // registration is last-wins, and RedisSessionStore would silently move onto an instance that forgets
        // every session at each restart.
        var services = new ServiceCollection();

        services.AddIdentityAndSessions(ApiConfiguration());

        services.Count(d => d.ServiceType == typeof(IConnectionMultiplexer)).ShouldBe(1);
        services.ShouldNotContain(d => d.IsKeyedService && d.ServiceType == typeof(IConnectionMultiplexer));
    }
}
