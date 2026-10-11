using System.Reflection;
using Jobbliggaren.Application.NotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.NotificationFeed;

/// <summary>
/// #1666 — the bell's source list is a static registry, so this test is what keeps it whole: a source
/// class that is not listed would be dead code the bell never reads, and a listed source holding state
/// would leak one request's data into the next, since one instance serves every request.
/// </summary>
public class NotificationFeedSourcesTests
{
    private static readonly Type[] SourceTypes = typeof(INotificationFeedSource).Assembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(INotificationFeedSource).IsAssignableFrom(t))
        .ToArray();

    [Fact]
    public void All_ListsEverySourceClass_ExactlyOnce()
    {
        SourceTypes.ShouldNotBeEmpty();
        NotificationFeedSources.All.Select(s => s.GetType())
            .ShouldBe(SourceTypes, ignoreOrder: true);
        NotificationFeedSources.All.Select(s => s.GetType()).Distinct().Count()
            .ShouldBe(NotificationFeedSources.All.Count);
    }

    [Fact]
    public void All_GivesEveryKindToExactlyOneSource()
    {
        NotificationFeedSources.All.Select(s => s.Kind)
            .ShouldBe(Enum.GetValues<NotificationFeedSourceKind>(), ignoreOrder: true);
    }

    [Fact]
    public void EverySource_IsStateless()
    {
        foreach (var type in SourceTypes)
        {
            type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .ShouldBeEmpty($"{type.Name} holds instance state, but one instance serves every request");
        }
    }
}
