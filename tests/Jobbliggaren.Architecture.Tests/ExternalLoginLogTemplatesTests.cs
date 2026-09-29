using System.Reflection;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1746 (dotnet-architect N2, test-writer Minor 8), a form pin: each adapter carries its own copy of the 1022 and
/// 1023 templates, and nothing but this row keeps the copies alike, which a query over the log for every provider needs.
/// </summary>
public class ExternalLoginLogTemplatesTests
{
    private static readonly Type[] Adapters =
    [
        .. typeof(GoogleIdentityProvider).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                           && typeof(IExternalIdentityProvider).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal),
    ];

    [Theory]
    [InlineData(1022)]
    [InlineData(1023)]
    public void Every_adapter_writes_the_event_with_one_template_and_all_with_the_same(int eventId)
    {
        Adapters.Length.ShouldBeGreaterThan(1, "otherwise there is nothing to compare");

        var templates = Adapters
            .Select(adapter => adapter
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
                .Where(attribute => attribute?.EventId == eventId)
                .ShouldHaveSingleItem($"{adapter.Name} has not exactly one [LoggerMessage({eventId})]")!
                .Message)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        templates.ShouldHaveSingleItem();
    }
}
