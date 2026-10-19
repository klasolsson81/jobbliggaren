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
/// #1981 (ADR 0178) — the provider reads' registration, by pure <see cref="ServiceCollection"/> inspection: no host,
/// no network, no credential. It pins that the sender and the source read <c>Email:Provider</c> alike, that the source
/// needs a read key of its own, that <c>AddEmailSender</c> alone never registers the source, and that the options and
/// the read client are what ADR 0178 says.
/// </summary>
public class EmailUsageSourceRegistrationTests
{
    private const string SecretKey = "test-scaleway-secret-key";
    private const string ReadKey = "test-scaleway-read-key";
    private const string ProjectId = "11111111-2222-3333-4444-555555555555";
    private const string ReadKeyName = "Email:ScalewayRead:SecretKey";

    /// <summary>The read key's four shapes: absent, blank, its own value, and the sending key pasted in.</summary>
    public enum ReadKeyShape
    {
        Absent,
        Blank,
        Present,
        EqualToTheSendKey,
    }

    private static IConfiguration Config(string? provider, ReadKeyShape readKey = ReadKeyShape.Present)
    {
        var values = new Dictionary<string, string?>
        {
            ["Email:Provider"] = provider,
            ["Email:Scaleway:Region"] = "fr-par",
            ["Email:Scaleway:SecretKey"] = SecretKey,
            ["Email:Scaleway:ProjectId"] = ProjectId,
        };
        values[ReadKeyName] = readKey switch
        {
            ReadKeyShape.Blank => " ",
            ReadKeyShape.Present => ReadKey,
            ReadKeyShape.EqualToTheSendKey => SecretKey,
            _ => null,
        };

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

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

    private static Type? SourceType(IServiceCollection services)
    {
        var source = services.Single(d => d.ServiceType == typeof(IEmailProviderUsageSource));
        return source.ImplementationType ?? source.ImplementationInstance?.GetType();
    }

    private static Type? SenderType(IServiceCollection services) =>
        services.Single(d => d.ServiceType == typeof(IEmailSender)).ImplementationType;

    [Theory]
    [InlineData(null)]
    [InlineData("Console")]
    [InlineData("console")]
    [InlineData("Scaleway")]
    [InlineData("scaleway")]
    [InlineData("SCALEWAY")]
    public void TheSource_FollowsTheSenderAndTheReadKey(string? provider)
    {
        foreach (var readKey in new[] { ReadKeyShape.Absent, ReadKeyShape.Present })
        {
            var services = ApiComposition(Config(provider, readKey));

            var scaleway = SenderType(services) == typeof(ScalewayEmailSender);
            SourceType(services).ShouldBe(
                !scaleway ? typeof(NotConfiguredEmailProviderReads)
                : readKey == ReadKeyShape.Present ? typeof(MemoizedEmailProviderUsageSource)
                : typeof(ReadKeyMissingEmailProviderReads));
        }
    }

    [Theory]
    [InlineData(ReadKeyShape.Absent)]
    [InlineData(ReadKeyShape.Blank)]
    [InlineData(ReadKeyShape.Present)]
    [InlineData(ReadKeyShape.EqualToTheSendKey)]
    public void UnderConsole_EveryReadKeyShape_IsNotConfigured_AndNothingThrows(ReadKeyShape readKey)
    {
        var services = ApiComposition(Config("Console", readKey));

        SourceType(services).ShouldBe(typeof(NotConfiguredEmailProviderReads));
        services.ShouldNotContain(d => d.ServiceType == typeof(ScalewayReadTransport));
        services.ShouldNotContain(d => d.ImplementationType == typeof(ReadKeyMissingNotice));
    }

    [Fact]
    public void UnderScaleway_AnAbsentReadKey_IsReadKeyMissing_WithItsStartupNotice_AndNoTransport()
    {
        var services = ApiComposition(Config("Scaleway", ReadKeyShape.Absent));

        SourceType(services).ShouldBe(typeof(ReadKeyMissingEmailProviderReads));
        services.ShouldContain(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ReadKeyMissingNotice));
        services.ShouldNotContain(d => d.ServiceType == typeof(ScalewayReadTransport));
        services.ShouldNotContain(d => d.ServiceType == typeof(IProviderUsageAttemptSource));
    }

    [Fact]
    public void UnderScaleway_ABlankReadKey_FailsTheRegistration_NamingTheKey()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => ApiComposition(Config("Scaleway", ReadKeyShape.Blank)));

        thrown.Message.ShouldContain(ReadKeyName);
    }

    [Fact]
    public void UnderScaleway_AReadKeyEqualToTheSendKey_FailsTheRegistration_NamingBothKeys_AndNeitherValue()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => ApiComposition(Config("Scaleway", ReadKeyShape.EqualToTheSendKey)));

        thrown.Message.ShouldContain(ReadKeyName);
        thrown.Message.ShouldContain("Email:Scaleway:SecretKey");
        thrown.Message.ShouldNotContain(SecretKey);
    }

    [Fact]
    public void UnderScaleway_APresentReadKey_BindsTheReadOptions_FromItsOwnSection_AndTheSendersProject()
    {
        using var provider = ApiComposition(Config("Scaleway")).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ScalewayReadOptions>>().Value;

        options.SecretKey.ShouldBe(ReadKey);
        options.ProjectId.ShouldBe(ProjectId);
        provider.GetServices<IHostedService>().ShouldNotContain(service => service is ReadKeyMissingNotice);
    }

    [Fact]
    public void TheReadOptions_TakeTheProjectFromTheSender_EvenWhenTheirOwnSectionNamesAnother()
    {
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(Config("Scaleway"))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:ScalewayRead:ProjectId"] = "another-project" })
            .Build();
        using var provider = ApiComposition(configuration).BuildServiceProvider();

        provider.GetRequiredService<IOptions<ScalewayReadOptions>>().Value.ProjectId.ShouldBe(ProjectId);
    }

    [Theory]
    [InlineData("Console", ReadKeyShape.Present, typeof(NotConfiguredEmailProviderReads))]
    [InlineData("Scaleway", ReadKeyShape.Absent, typeof(ReadKeyMissingEmailProviderReads))]
    public void WhereNothingIsRead_OneInstanceAnswersEveryReadPort(string provider, ReadKeyShape readKey, Type expected)
    {
        using var serviceProvider = ApiComposition(Config(provider, readKey)).BuildServiceProvider();

        var usage = serviceProvider.GetRequiredService<IEmailProviderUsageSource>();
        usage.ShouldBeOfType(expected);
        serviceProvider.GetRequiredService<IEmailDeliveryStatisticsSource>().ShouldBeSameAs(usage);
        serviceProvider.GetRequiredService<IEmailRecentFailuresSource>().ShouldBeSameAs(usage);
    }

    [Fact]
    public void UnderScaleway_APresentReadKey_RegistersEachReadOnce_AsAMemoizedSingleton_OverItsAdapter()
    {
        var services = ApiComposition(Config("Scaleway"));

        (Type Port, Type Memo, Type Attempt, Type Adapter)[] reads =
        [
            (typeof(IEmailProviderUsageSource), typeof(MemoizedEmailProviderUsageSource), typeof(IProviderUsageAttemptSource), typeof(ScalewayUsageSource)),
            (typeof(IEmailDeliveryStatisticsSource), typeof(MemoizedEmailDeliveryStatisticsSource), typeof(IDeliveryStatisticsAttemptSource), typeof(ScalewayDeliveryStatisticsSource)),
            (typeof(IEmailRecentFailuresSource), typeof(MemoizedEmailRecentFailuresSource), typeof(IRecentFailuresAttemptSource), typeof(ScalewayRecentFailuresSource)),
        ];
        foreach (var (port, memo, attempt, adapter) in reads)
        {
            var descriptor = services.Where(d => d.ServiceType == port).ShouldHaveSingleItem();
            descriptor.ImplementationType.ShouldBe(memo);
            descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
            services.Where(d => d.ServiceType == attempt).ShouldHaveSingleItem().ImplementationType.ShouldBe(adapter);
        }
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
        services.ShouldNotContain(d => d.ServiceType == typeof(IEmailDeliveryStatisticsSource));
        services.ShouldNotContain(d => d.ServiceType == typeof(IEmailRecentFailuresSource));
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
    [InlineData(null, ReadKeyShape.Present)]
    [InlineData("Scaleway", ReadKeyShape.Present)]
    [InlineData("Scaleway", ReadKeyShape.Absent)]
    public void TheComposition_Resolves_WithScopeAndBuildValidation(string? provider, ReadKeyShape readKey)
    {
        using var serviceProvider = ApiComposition(Config(provider, readKey))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        serviceProvider.GetRequiredService<IEmailProviderUsageSource>().ShouldNotBeNull();
        serviceProvider.GetRequiredService<IEmailDeliveryStatisticsSource>().ShouldNotBeNull();
        serviceProvider.GetRequiredService<IEmailRecentFailuresSource>().ShouldNotBeNull();
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
