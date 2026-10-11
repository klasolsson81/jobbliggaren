using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Security;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Dev.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// Verifierar att <c>Program.cs</c> startar i Production-env utan att tippa över
/// när env-gated config är populerad. Komplement till de övriga integration-
/// testerna som tvingar Development-env via fixtures (TD-37 fix). Skyddar mot
/// regression där en ny env-gated check (HSTS, ForwardedHeaders, etc.) tyst
/// bara körs i Development och därmed bryter Production-deploy först i CI.
/// </summary>
public sealed class ProductionStartupFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private readonly RedisBoundaryFixture _redisBoundary = new();

    private string _postgresCs = string.Empty;
    private string _redisCs = string.Empty;
    private RedisTestEnvironment? _redisEnvironment;

    /// <summary>
    /// #1745 — the providers the Production composition registered, read BEFORE the neutraliser below removes every
    /// one of them (test-writer 6b form reading, Minor H): after it, an absence would be the neutraliser's, not the
    /// composition's. Captured from the first host built, the fixture's own.
    /// </summary>
    internal IReadOnlyList<Type?>? ComposedExternalProviders { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        builder.ConfigureServices(services =>
        {
            // #1735 (security-auditor Major 12) — outside Development/Test the Api refuses to boot on a sender
            // that cannot deliver, and this host would otherwise compose NullEmailSender, or a real provider from
            // a developer's Local.json. A delivering in-process fake, registered last, keeps the boot on the path
            // under test.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(new RecordingEmailSender());
            ComposedExternalProviders ??= services
                .Where(d => d.ServiceType == typeof(IExternalIdentityProvider))
                .Select(d => d.ImplementationType)
                .ToList();
            services.NeutraliseExternalLoginsFromLocalConfiguration();

            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options =>
                options
                    .UseNpgsql(_postgresCs,
                        npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                    .UseSnakeCaseNamingConvention()
                    .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning)));

            services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
            services.RemoveAll<AppIdentityDbContext>();
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseNpgsql(_postgresCs, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppIdentityDbContext).Assembly.FullName);
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity");
                }).ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning)));


            // N-2 hardening (2026-05-11): prod-seedrar (IdempotentAdminRoleSeeder
            // + ADR 0043 TaxonomySnapshotSeeder) bubblar 42P01 i Production-env
            // (CLAUDE.md §3.4 fail-loud). Fixturen kör Services.CreateScope FÖRE
            // MigrateAsync (catch-22) → seedrarna plockas bort här. Prod-defensen
            // verifieras separat via *ProdBubbleTests + *.IsSchemaInitGracePeriod.
            // Delad SPOT (ADR 0043 defekt-triage #3).
            services.RemoveStartupSeeders();
        });
    }

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redisBoundary.InitializeAsync().AsTask());

        _postgresCs = _postgres.GetConnectionString();
        _redisCs = _redisBoundary.OptionsFor(_redisBoundary.Persistent, RedisBoundaryFixture.ApiPersistent).ToString(true);

        // ASPNETCORE_ENVIRONMENT sätts FÖRE Services-access. UseEnvironment() i
        // ConfigureWebHost är otillräckligt för minimal API. Production-mode
        // är HELA poängen med denna fixture — verifiera Program.cs-startup-pipeline
        // i prod-läge med populerad config.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        // Production-defense per ForwardedHeadersConfig.EnsureSafeForEnvironment:
        // KnownNetworks får inte vara tom när Environment != Development/Test.
        // Loopback-CIDR är tillräckligt för smoke-startup (test-host gör direkt-anrop).
        Environment.SetEnvironmentVariable("ForwardedHeaders__KnownNetworks__0", "127.0.0.1/32");

        // Production-env kräver explicit ConnectionStrings:Postgres + Redis (Development
        // tolererar saknad). ApiFactory replacer DbContext + IDistributedCache via
        // ConfigureServices, men AddInfrastructure läser CS:erna direkt vid registrerings-
        // tid innan replace körs. Sätt till container-CS:erna så registreringen passerar.
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgresCs);

        // #1745, #1746 — a full Google, GitHub and LinkedIn client in the environment, as compose passes them on the box.
        Environment.SetEnvironmentVariable("Auth__OAuth__Google__ClientId", "configured-client-id");
        Environment.SetEnvironmentVariable("Auth__OAuth__Google__ClientSecret", "configured-client-secret");
        Environment.SetEnvironmentVariable("Auth__OAuth__GitHub__ClientId", "configured-github-client-id");
        Environment.SetEnvironmentVariable("Auth__OAuth__GitHub__ClientSecret", "configured-github-client-secret");
        Environment.SetEnvironmentVariable("Auth__OAuth__LinkedIn__ClientId", "configured-linkedin-client-id");
        Environment.SetEnvironmentVariable("Auth__OAuth__LinkedIn__ClientSecret", "configured-linkedin-client-secret");
        _redisEnvironment = new RedisTestEnvironment(_redisCs, _redisBoundary.OptionsFor(_redisBoundary.Volatile, RedisBoundaryFixture.ApiVolatile).ToString(true));
        // ADR 0066 (#802): fält-krypteringen är Local-only och validatorn kräver en
        // giltig master-nyckel i ALLA miljöer (även Production-smoke) — den sätts
        // systemiskt av TestSecrets-module-init (process-env-var) före boot.

        using var scope = Services.CreateScope();
        // F6 P4 — pg_trgm krävs av F6P4aJobAdTrigramIndexes (se ApiFactory).
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await appDb.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await appDb.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Database.MigrateAsync();
    }

    public new async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable("ForwardedHeaders__KnownNetworks__0", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null);
        Environment.SetEnvironmentVariable("Auth__OAuth__Google__ClientId", null);
        Environment.SetEnvironmentVariable("Auth__OAuth__Google__ClientSecret", null);
        Environment.SetEnvironmentVariable("Auth__OAuth__GitHub__ClientId", null);
        Environment.SetEnvironmentVariable("Auth__OAuth__GitHub__ClientSecret", null);
        Environment.SetEnvironmentVariable("Auth__OAuth__LinkedIn__ClientId", null);
        Environment.SetEnvironmentVariable("Auth__OAuth__LinkedIn__ClientSecret", null);
        _redisEnvironment?.Dispose();

        await Task.WhenAll(_postgres.StopAsync(), _redisBoundary.DisposeAsync().AsTask());
        await base.DisposeAsync();
    }
}

[CollectionDefinition("ProductionStartup")]
public sealed class ProductionStartupFixtureGroup : ICollectionFixture<ProductionStartupFactory>;

[Collection("ProductionStartup")]
public class ProductionStartupSmokeTests(ProductionStartupFactory factory)
{
    private readonly ProductionStartupFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GET_api_ready_returns_200_in_Production_env()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/ready", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // #796 map-gate guardrail (HARD merge gate, CLAUDE.md §12). The whole /api/v1/dev group is
    // ENVIRONMENT-gated: /api/v1/dev/accounts (ADR 0142 part 5a) and /api/v1/dev/login-code (#1735) are
    // unmapped outside Development without exception, because without authentication they open an
    // account and hand out a login code, so no configuration may widen them. A 404 (not 401/405)
    // proves the route does not exist; if a gate regressed, these turn red before deploy.

    [Fact]
    public async Task POST_dev_login_code_is_unmapped_in_Production_env()
    {
        // #1735 — the login-code seam's ENVIRONMENT gate; the route-table test below measures the whole group.
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync(
            "/api/v1/dev/login-code",
            new { email = "x@e2e.jobbliggaren.test" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_dev_accounts_is_unmapped_in_Production_env_and_its_policy_is_not_registered()
    {
        // The seed seam opens an account without an inbox proof, so both gates are measured: the map (a 404
        // for a reserved address, which the handler would otherwise answer 204) and the dev-only policy the
        // handler cannot resolve without. The route-table test below measures the whole group.
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync(
            "/api/v1/dev/accounts",
            new { email = "x@e2e.jobbliggaren.test" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetService<IDevSeedableAddressPolicy>().ShouldBeNull();
    }

    [Fact]
    public void No_route_under_api_v1_dev_is_mapped_in_Production_env()
    {
        // Universally quantified over the route table rather than enumerated: a seam added under the
        // group fails here on arrival instead of on someone remembering to add a case.
        var devRoutes = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .Where(p => p.Contains("api/v1/dev", StringComparison.OrdinalIgnoreCase))
            .ToList();

        devRoutes.ShouldBeEmpty();
    }

    // ADR 0142 D10 — outside Development/Test the Api refuses to boot on a sender that cannot deliver, whichever
    // way the registration gate stands. The real NullEmailSender is what AddEmailSender composes there with
    // Email:Provider unset.
    [Theory]
    [InlineData("false")]
    [InlineData("true")]
    public void A_host_whose_sender_cannot_deliver_refuses_to_boot_in_Production_env(string registrationsOpen)
    {
        using var host = _factory.WithWebHostBuilder(b => b
            .UseSetting("Auth:RegistrationsOpen", registrationsOpen)
            .ConfigureServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(new NullEmailSender(NullLogger<NullEmailSender>.Instance));
            }));

        var thrown = Should.Throw<Exception>(() => host.CreateClient());

        var refusal = Chain(thrown).OfType<OptionsValidationException>().FirstOrDefault();
        refusal.ShouldNotBeNull($"the boot failed, but not on the AuthOptions validation: {thrown}");
        refusal.OptionsType.ShouldBe(typeof(AuthOptions));
        refusal.Message.ShouldContain(nameof(NullEmailSender));
    }

    [Fact]
    public async Task An_open_gate_with_a_delivering_sender_boots_in_Production_env()
    {
        // The counterfactual: the same environment with the gate open and the fixture's delivering sender.
        var ct = TestContext.Current.CancellationToken;
        var logs = new CapturingLoggerProvider();
        using var host = _factory.WithWebHostBuilder(b => b
            .UseSetting("Auth:RegistrationsOpen", "true")
            .ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs)));
        using var client = host.CreateClient();

        (await client.GetAsync("/api/live", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var announcement = logs.Logs.Where(l => l.EventId.Id is 4300 or 4301).ShouldHaveSingleItem();
        announcement.EventId.Id.ShouldBe(4301);
        announcement.Level.ShouldBe(LogLevel.Warning);
    }

    private static IEnumerable<Exception> Chain(Exception root)
    {
        yield return root;
        var inner = root is AggregateException aggregate
            ? aggregate.InnerExceptions
            : root.InnerException is { } single ? [single] : (IReadOnlyCollection<Exception>)[];
        foreach (var exception in inner.SelectMany(Chain))
            yield return exception;
    }

    [Fact]
    public void Every_provider_is_composed_in_Production_env_from_its_client()
    {
        // #1745: read before the neutraliser (test-writer Minor H), so this is the Production composition's own
        // registration from the clients compose passes on the box.
        var configuration = _factory.Services.GetRequiredService<IConfiguration>();
        configuration["Auth:OAuth:GitHub:ClientId"].ShouldBe("configured-github-client-id");
        configuration["Auth:OAuth:Google:ClientId"].ShouldBe("configured-client-id");
        configuration["Auth:OAuth:LinkedIn:ClientId"].ShouldBe("configured-linkedin-client-id");

        _factory.ComposedExternalProviders.ShouldNotBeNull().ShouldBe(
            [typeof(GoogleIdentityProvider), typeof(LinkedInIdentityProvider), typeof(GitHubIdentityProvider)],
            ignoreOrder: true);
    }

    // #1735 — the login-code seam's second gate, measured the same way: neither the reader nor the capture
    // is in the container. The capturing sender is not asserted here: this host replaces IEmailSender, so
    // DevLoginCodeCaptureCompositionTests measures that half on the unswapped composition.
    [Fact]
    public void The_login_code_capture_is_not_registered_in_Production_env()
    {
        using var scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetService<IDevLoginCodeReader>().ShouldBeNull();
        scope.ServiceProvider.GetService<DevLoginCodeCapture>().ShouldBeNull();
    }
}
