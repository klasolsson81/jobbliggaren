using System.Net;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Admin.Email;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// #1981 (ADR 0178) — the allowance source's registration, by pure <see cref="ServiceCollection"/> inspection: no host,
/// no network, no credential. It pins that the sender and the source read <c>Email:Provider</c> alike, that
/// <c>AddEmailSender</c> alone never registers the source, and that the options and the read client are what ADR 0178
/// says.
/// </summary>
public class EmailUsageSourceRegistrationTests
{
    private const string SecretKey = "test-scaleway-secret-key";
    private const string ProjectId = "11111111-2222-3333-4444-555555555555";

    private static IConfiguration Config(string? provider) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Provider"] = provider,
                ["Email:Scaleway:Region"] = "fr-par",
                ["Email:Scaleway:SecretKey"] = SecretKey,
                ["Email:Scaleway:ProjectId"] = ProjectId,
            })
            .Build();

    private static IHostEnvironment Development()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Development);
        return env;
    }

    /// <summary>The API's composition of the two: the sender, then the source.</summary>
    private static ServiceCollection ApiComposition(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IDateTimeProvider>());
        services.AddEmailSender(configuration, Development());
        services.AddEmailUsageSource(configuration);
        return services;
    }

    private static Type? SourceType(IServiceCollection services) =>
        services.Single(d => d.ServiceType == typeof(IEmailProviderUsageSource)).ImplementationType;

    private static Type? SenderType(IServiceCollection services) =>
        services.Single(d => d.ServiceType == typeof(IEmailSender)).ImplementationType;

    [Theory]
    [InlineData(null)]
    [InlineData("Console")]
    [InlineData("console")]
    [InlineData("Scaleway")]
    [InlineData("scaleway")]
    [InlineData("SCALEWAY")]
    public void TheSourceIsScalewaysExactly_WhenTheSenderIs(string? provider)
    {
        var services = ApiComposition(Config(provider));

        var scaleway = SenderType(services) == typeof(ScalewayEmailSender);
        SourceType(services).ShouldBe(scaleway ? typeof(MemoizedEmailProviderUsageSource) : typeof(NotConfiguredEmailProviderUsageSource));
    }

    [Fact]
    public void TheSourceIsRegisteredOnce_AsASingleton()
    {
        var services = ApiComposition(Config("Scaleway"));

        var descriptor = services.Where(d => d.ServiceType == typeof(IEmailProviderUsageSource)).ShouldHaveSingleItem();
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        services.Where(d => d.ServiceType == typeof(IProviderUsageAttemptSource)).ShouldHaveSingleItem()
            .ImplementationType.ShouldBe(typeof(ScalewayUsageSource));
    }

    [Fact]
    public void TheWorkersPath_AddEmailSenderAlone_NeverRegistersTheSource()
    {
        var services = new ServiceCollection();
        services.AddEmailSender(Config("Scaleway"), Development());

        services.ShouldNotContain(d => d.ServiceType == typeof(IEmailProviderUsageSource));
        services.ShouldNotContain(d => d.ServiceType == typeof(IProviderUsageAttemptSource));
    }

    [Fact]
    public void TheReadClient_HasItsOwnNameRegionAndFiveSecondTimeout_AndTheSendClientKeepsItsOwn()
    {
        using var provider = ApiComposition(Config("Scaleway")).BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var read = factory.CreateClient(ScalewayClientRegistration.UsageHttpClientName);
        var send = factory.CreateClient(ScalewayClientRegistration.HttpClientName);

        read.BaseAddress.ShouldBe(new Uri("https://api.scaleway.com/transactional-email/v1alpha1/regions/fr-par/"));
        read.Timeout.ShouldBe(TimeSpan.FromSeconds(5));
        send.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void TheReadClient_FollowsNoRedirect()
    {
        using var provider = ApiComposition(Config("Scaleway")).BuildServiceProvider();

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(ScalewayClientRegistration.UsageHttpClientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        handler.ShouldBeOfType<SocketsHttpHandler>().AllowAutoRedirect.ShouldBeFalse();
    }

    [Fact]
    public async Task TheSendClient_SendsA503ExactlyOnce()
    {
        var transport = new CountingTransport(HttpStatusCode.ServiceUnavailable);
        var services = ApiComposition(Config("Scaleway"));
        services.AddHttpClient(ScalewayClientRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => transport);
        using var provider = services.BuildServiceProvider();

        using var response = await provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ScalewayClientRegistration.HttpClientName)
            .GetAsync(new Uri("emails", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        transport.Requests.ShouldBe(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Scaleway")]
    public void TheComposition_Resolves_WithScopeAndBuildValidation(string? provider)
    {
        using var serviceProvider = ApiComposition(Config(provider))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        serviceProvider.GetRequiredService<IEmailProviderUsageSource>().ShouldNotBeNull();
        serviceProvider.GetRequiredService<IOptions<EmailAllowanceOptions>>().Value.IncludedMonthlyEmails.ShouldBe(300);
    }

    [Fact]
    public void AnInvalidAllowanceOverride_FailsValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["EmailAllowance:IncludedMonthlyEmails"] = "-1" })
            .Build();
        using var provider = ApiComposition(configuration).BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<EmailAllowanceOptions>>().Value);
    }

    private sealed class CountingTransport(HttpStatusCode status) : HttpMessageHandler
    {
        private int _requests;

        public int Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
