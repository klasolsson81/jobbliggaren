using System.Text.Json;
using Jobbliggaren.Application.NotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// ADR 0175 D9 — every kind the Api sends is one the web's closed Zod enum parses, since a kind it does not name fails
/// the bell's whole read.
/// </summary>
public class NotificationFeedSourceKindWireContractTests
{
    private const string FrontendDtoRelativePath = "web/jobbliggaren-web/src/lib/dto/notification-feed.ts";
    private const string FrontendSchemaName = "notificationFeedSourceKindSchema";

    [Fact]
    public void EveryKindTheApiSendsIsOneTheWebParses()
    {
        var web = RepoSourceText.QuotedList(
            FrontendDtoRelativePath,
            FrontendSchemaName,
            $@"\b{FrontendSchemaName}\s*=\s*z\.enum\(\[([^\]]*)\]");

        var sent = Enum.GetValues<NotificationFeedSourceKind>().Select(WireName).ToArray();

        sent.ShouldBeSubsetOf(
            web,
            $"{FrontendSchemaName} in {FrontendDtoRelativePath} names {string.Join(", ", web)}.");
    }

    private static string WireName(NotificationFeedSourceKind kind)
    {
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(new NotificationFeedSourceDto(kind, null, null), JsonSerializerOptions.Web));
        return json.RootElement.GetProperty("kind").GetString()!;
    }
}
