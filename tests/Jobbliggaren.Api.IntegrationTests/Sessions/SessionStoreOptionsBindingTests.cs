using Jobbliggaren.Infrastructure.Auth.Sessions;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Sessions;

// #626: the lifetime profiles live under an unusual nested key — the Session profile is
// "Session:Session". Pin that the appsettings shape actually binds, so a silent bind
// failure (which would drop every profile to its code default) can't slip through.
public class SessionStoreOptionsBindingTests
{
    private static SessionStoreOptions Bind()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Session:Legacy:SlidingTtl"] = "14.00:00:00",
                ["Session:Legacy:AbsoluteTtl"] = "30.00:00:00",
                ["Session:Legacy:RotationInterval"] = "00:00:00",
                ["Session:Session:SlidingTtl"] = "1.00:00:00",
                ["Session:Session:AbsoluteTtl"] = "1.00:00:00",
                ["Session:Session:RotationInterval"] = "00:00:00",
                ["Session:Persistent:SlidingTtl"] = "30.00:00:00",
                ["Session:Persistent:AbsoluteTtl"] = "30.00:00:00",
                ["Session:Persistent:RotationInterval"] = "1.00:00:00",
            })
            .Build();

        var options = new SessionStoreOptions();
        config.GetSection(SessionStoreOptions.SectionName).Bind(options);
        return options;
    }

    [Fact]
    public void Bind_ShouldPopulateAllThreeProfiles_FromNestedSessionSection()
    {
        var options = Bind();

        options.Legacy.SlidingTtl.ShouldBe(TimeSpan.FromDays(14));
        options.Legacy.AbsoluteTtl.ShouldBe(TimeSpan.FromDays(30));

        // The "Session:Session" key is the one that could silently fail to bind.
        options.Session.SlidingTtl.ShouldBe(TimeSpan.FromHours(24));
        options.Session.AbsoluteTtl.ShouldBe(TimeSpan.FromHours(24));

        options.Persistent.SlidingTtl.ShouldBe(TimeSpan.FromDays(30));
        options.Persistent.AbsoluteTtl.ShouldBe(TimeSpan.FromDays(30));
        options.Persistent.RotationInterval.ShouldBe(TimeSpan.FromHours(24));
    }

    [Fact]
    public void ProfileFor_ShouldMapEachLifetimeToItsProfile()
    {
        var options = Bind();

        options.ProfileFor(Application.Common.Abstractions.SessionLifetime.Legacy)
            .AbsoluteTtl.ShouldBe(TimeSpan.FromDays(30));
        options.ProfileFor(Application.Common.Abstractions.SessionLifetime.Session)
            .AbsoluteTtl.ShouldBe(TimeSpan.FromHours(24));
        options.ProfileFor(Application.Common.Abstractions.SessionLifetime.Persistent)
            .AbsoluteTtl.ShouldBe(TimeSpan.FromDays(30));
    }
}
