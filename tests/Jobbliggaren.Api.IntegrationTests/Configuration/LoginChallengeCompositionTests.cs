using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// The login challenge's consumer lives in the Api composition only (ADR 0142 D2, ADR 0023): its store
/// protects with the Api's Data-Protection keyring and runs on the Api's Redis multiplexer. No other guard
/// catches a mis-registration — WorkerLayerTests scans the Worker assembly and the consumer lives in
/// Infrastructure — so this pair is the pin. The positive half is the control the negative half is measured
/// against: two absences with no presence beside them would pass against a build that registers nothing.
/// </summary>
public sealed class LoginChallengeCompositionTests
{
    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Read at registration time, and absence throws.
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=jobbliggaren;Username=x;Password=y",
                ["ConnectionStrings:Redis"] = "localhost:6379",
            })
            .Build();

    [Fact]
    public void AddIdentityAndSessions_registers_the_dispatcher_its_consumer_the_store_the_budget_and_the_inbox_proof()
    {
        var services = new ServiceCollection();

        services.AddIdentityAndSessions(Configuration());

        services.ShouldContain(d => d.ServiceType == typeof(ILoginChallengeDispatcher));
        services.ShouldContain(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(LoginChallengeDispatchService));
        services.ShouldContain(d => d.ServiceType == typeof(ILoginChallengeStore));
        services.ShouldContain(d => d.ServiceType == typeof(IRateBudget));
        services.ShouldContain(d => d.ServiceType == typeof(IInboxProofRecorder));
    }

    [Fact]
    public void AddCoreIdentityForWorker_registers_none_of_them()
    {
        var services = new ServiceCollection();

        services.AddCoreIdentityForWorker(Configuration());

        services.ShouldNotContain(d => d.ServiceType == typeof(ILoginChallengeDispatcher));
        services.ShouldNotContain(d => d.ImplementationType == typeof(LoginChallengeDispatchService));
        services.ShouldNotContain(d => d.ServiceType == typeof(ILoginChallengeStore));
        services.ShouldNotContain(d => d.ServiceType == typeof(IRateBudget));
        services.ShouldNotContain(d => d.ServiceType == typeof(IInboxProofRecorder));
    }
}
