using Jobbliggaren.Api.Hosting;
using Jobbliggaren.Application.JobAds.Listing;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Hosting;

/// <summary>
/// The listed-ad snapshot's composition (ADR 0174): one singleton serving the reader and the tick, no scoped
/// dependency for <c>ValidateScopes</c> to refuse, and the hosted service registered once. No web host: only the
/// registration. That it is composed by the Api alone is <c>ListedJobAdSnapshotCompositionTests</c>'.
/// </summary>
public sealed class ListedJobAdSnapshotRegistrationTests
{
    [Fact]
    public void AddListedJobAdSnapshot_TheSampler_IsOneSingletonServingTheReaderAndTheTick_AndTheServiceIsHosted()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IDateTimeProvider>());
        services.AddListedJobAdSnapshot();
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var sampler = provider.GetRequiredService<ListedJobAdSnapshotSampler>();

        provider.GetRequiredService<IListedJobAdSnapshotReader>().ShouldBeSameAs(sampler);
        provider.GetRequiredService<IListedJobAdSnapshotSampler>().ShouldBeSameAs(sampler);
        provider.GetServices<IHostedService>().OfType<ListedJobAdSnapshotService>().ShouldHaveSingleItem();
        sampler.Current.ShouldBeNull("nothing is known before the first read");
    }
}
