using Jobbliggaren.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// #1737 — the Api and the Worker compose Identity separately over the SAME rows. With the user name being the
/// address, a composition left on Identity's default ASCII user-name charset would refuse, in one host, an account
/// the other host created.
/// </summary>
public class IdentityUserNameCharsetParityTests
{
    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Read at registration time, and absence throws.
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=jobbliggaren;Username=x;Password=y",
                ["ConnectionStrings:Redis"] = "localhost:6379,user=api-persistent,password=configuration-only",
                [$"ConnectionStrings:{DependencyInjection.VolatileRedisConnectionStringName}"] = "localhost:6381,user=api-volatile,password=configuration-only",
            })
            .Build();

    private static string CharsetOf(Action<IServiceCollection> compose)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        compose(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<IdentityOptions>>().Value.User.AllowedUserNameCharacters;
    }

    [Fact]
    public void The_api_composition_has_no_user_name_charset() =>
        CharsetOf(services => services.AddIdentityAndSessions(Configuration())).ShouldBeEmpty();

    [Fact]
    public void The_worker_composition_has_no_user_name_charset() =>
        CharsetOf(services => services.AddCoreIdentityForWorker(Configuration())).ShouldBeEmpty();
}
