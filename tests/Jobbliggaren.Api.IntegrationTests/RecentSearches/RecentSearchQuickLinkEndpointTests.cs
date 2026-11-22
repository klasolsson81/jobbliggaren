using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RecentSearches;

/// <summary>
/// ADR 0188 (#2265) — <c>PUT</c> and <c>DELETE /api/v1/me/recent-searches/{id}/quick-link</c> end to end on the wired
/// API and real Postgres, in the form of <c>RecentSearchesTests</c>. Every search is stored the way production stores it,
/// the capture POST, <c>POST /api/v1/me/recent-searches</c> (#2285), and every mark goes through the route. The cap
/// is three quick links, the junior link counted while it is shown (<c>QuickLinkCap</c>); the slot a mark took is read
/// from the table, since the list answers only whether a search is a quick link.
/// </summary>
[Collection("Api")]
public sealed class RecentSearchQuickLinkEndpointTests(ApiFactory factory)
{
    private const string RecentSearchesPath = "/api/v1/me/recent-searches";
    private const string JuniorLinkPath = "/api/v1/me/junior-quick-link";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PUT_and_DELETE_quick_link_anonymous_return_401()
    {
        using var anonymous = factory.CreateClient();
        var id = Guid.NewGuid();

        (await anonymous.PutAsync(QuickLink(id), content: null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync(QuickLink(id), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PUT_quick_link_marks_the_users_own_search_and_a_repeat_keeps_its_slot()
    {
        using var client = await SignedInClientAsync();
        var marked = await CommitSearchAsync(client, "lankmarkera");
        var other = await CommitSearchAsync(client, "lankandra");

        (await MarkAsync(client, marked)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var list = await ListAsync(client);
        list.Single(s => s.Id == marked).IsQuickLink.ShouldBeTrue();
        list.Single(s => s.Id == other).IsQuickLink.ShouldBeFalse();
        var slot = (await SlotOfAsync(marked)).ShouldNotBeNull();

        (await MarkAsync(client, marked)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SlotOfAsync(marked)).ShouldBe(slot);
        (await SlotOfAsync(other)).ShouldBeNull();
    }

    // ADR 0031: another user's row answers the same 404 as an unknown one.
    [Fact]
    public async Task PUT_quick_link_on_another_users_or_an_unknown_search_is_a_404_and_marks_nothing()
    {
        using var owner = await SignedInClientAsync();
        var ownersSearch = await CommitSearchAsync(owner, "lankagare");
        using var intruder = await SignedInClientAsync();

        (await MarkAsync(intruder, ownersSearch)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MarkAsync(intruder, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await ListAsync(owner)).Single(s => s.Id == ownersSearch).IsQuickLink.ShouldBeFalse();
        (await SlotOfAsync(ownersSearch)).ShouldBeNull();
    }

    [Fact]
    public async Task PUT_quick_link_with_the_junior_link_shown_admits_two_marks_and_refuses_the_third_with_MaxTotal()
    {
        using var client = await SignedInClientAsync();
        var first = await CommitSearchAsync(client, "lanketta");
        var second = await CommitSearchAsync(client, "lanktvaa");
        var third = await CommitSearchAsync(client, "lanktrea");
        (await MarkAsync(client, first)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MarkAsync(client, second)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await MarkAsync(client, third);

        await ShouldBeMaxTotalAsync(response);
        (await ListAsync(client)).Count(s => s.IsQuickLink).ShouldBe(2);
        (await SlotOfAsync(third)).ShouldBeNull();
    }

    [Fact]
    public async Task PUT_quick_link_with_the_junior_link_hidden_admits_three_marks_and_refuses_a_fourth_with_MaxTotal()
    {
        using var client = await SignedInClientAsync();
        (await client.PutAsJsonAsync(JuniorLinkPath, new { enabled = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Guid[] searches =
        [
            await CommitSearchAsync(client, "lankfyraa"),
            await CommitSearchAsync(client, "lankfyrab"),
            await CommitSearchAsync(client, "lankfyrac"),
        ];
        var fourth = await CommitSearchAsync(client, "lankfyrad");
        foreach (var id in searches)
            (await MarkAsync(client, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await MarkAsync(client, fourth);

        await ShouldBeMaxTotalAsync(response);
        var slots = new List<int?>();
        foreach (var id in searches)
            slots.Add(await SlotOfAsync(id));
        slots.ShouldBe(new int?[] { 1, 2, 3 }, ignoreOrder: true);
        (await SlotOfAsync(fourth)).ShouldBeNull();
    }

    [Fact]
    public async Task DELETE_quick_link_unmarks_the_search_and_keeps_it_and_a_repeat_is_a_no_op()
    {
        using var client = await SignedInClientAsync();
        var id = await CommitSearchAsync(client, "lankavmarkera");
        (await MarkAsync(client, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.DeleteAsync(QuickLink(id), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListAsync(client)).ShouldHaveSingleItem().ShouldBe(new ListedSearch(id, "lankavmarkera", IsQuickLink: false));
        (await SlotOfAsync(id)).ShouldBeNull();

        (await client.DeleteAsync(QuickLink(id), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(client)).ShouldHaveSingleItem().IsQuickLink.ShouldBeFalse();
    }

    [Fact]
    public async Task DELETE_quick_link_on_another_users_search_is_a_404_and_leaves_it_marked()
    {
        using var owner = await SignedInClientAsync();
        var ownersSearch = await CommitSearchAsync(owner, "lankskyddad");
        (await MarkAsync(owner, ownersSearch)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var intruder = await SignedInClientAsync();

        (await intruder.DeleteAsync(QuickLink(ownersSearch), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await ListAsync(owner)).Single(s => s.Id == ownersSearch).IsQuickLink.ShouldBeTrue();
        (await SlotOfAsync(ownersSearch)).ShouldNotBeNull();
    }

    // The row holds the link, so deleting the search takes the link with it and its slot is free for the next mark.
    [Fact]
    public async Task DELETE_recent_search_takes_its_quick_link_with_it_and_frees_its_slot()
    {
        using var client = await SignedInClientAsync();
        (await client.PutAsJsonAsync(JuniorLinkPath, new { enabled = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var first = await CommitSearchAsync(client, "lankfriaa");
        var second = await CommitSearchAsync(client, "lankfriab");
        var third = await CommitSearchAsync(client, "lankfriac");
        var waiting = await CommitSearchAsync(client, "lankfriad");
        foreach (var id in new[] { first, second, third })
            (await MarkAsync(client, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ShouldBeMaxTotalAsync(await MarkAsync(client, waiting));
        var freed = (await SlotOfAsync(second)).ShouldNotBeNull();

        (await client.DeleteAsync($"{RecentSearchesPath}/{second}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListAsync(client)).Select(s => s.Id).ShouldNotContain(second);
        (await MarkAsync(client, waiting)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SlotOfAsync(waiting)).ShouldBe(freed);
        (await ListAsync(client)).Count(s => s.IsQuickLink).ShouldBe(3);
    }

    // GDPR Art. 5(2), security-auditor's #2265 plan read Q5: each write is audited under its own name with the row's id
    // and no payload, as DeleteRecentSearchCommand is.
    [Fact]
    public async Task PUT_and_DELETE_quick_link_each_write_one_audit_row_naming_the_search_and_carrying_no_payload()
    {
        using var client = await SignedInClientAsync();
        var id = await CommitSearchAsync(client, "lankgranskad");

        (await MarkAsync(client, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync(QuickLink(id), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var entries = await ReadAuditEntriesAsync(id);
        entries.Select(e => e.EventType)
            .ShouldBe(["RecentJobSearch.QuickLinkMarked", "RecentJobSearch.QuickLinkUnmarked"]);
        entries.ShouldAllBe(e => e.AggregateType == "RecentJobSearch" && e.Payload == null);
    }

    private static string QuickLink(Guid id) => $"{RecentSearchesPath}/{id}/quick-link";

    private static Task<HttpResponseMessage> MarkAsync(HttpClient client, Guid id) =>
        client.PutAsync(QuickLink(id), content: null, Ct);

    private static async Task ShouldBeMaxTotalAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString()
            .ShouldBe("QuickLink.MaxTotal");
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, ct: Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    // A stored search: the capture POST stores it, as for every search a user stores on /jobb (ADR 0060, #2285).
    private static async Task<Guid> CommitSearchAsync(HttpClient client, string q)
    {
        (await client.PostAsJsonAsync("/api/v1/me/recent-searches", new { q }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await ListAsync(client)).Where(s => s.Q == q).ToList().ShouldHaveSingleItem().Id;
    }

    private static async Task<List<ListedSearch>> ListAsync(HttpClient client)
    {
        var items = await client.GetFromJsonAsync<JsonElement>($"{RecentSearchesPath}?includeCount=false", Ct);
        return items.EnumerateArray()
            .Select(row => new ListedSearch(
                Guid.Parse(row.GetProperty("id").GetString()!),
                row.GetProperty("q").GetString(),
                row.GetProperty("isQuickLink").GetBoolean()))
            .ToList();
    }

    private async Task<int?> SlotOfAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.RecentJobSearches.AsNoTracking().SingleAsync(r => r.Id == new RecentJobSearchId(id), Ct);
        return row.QuickLinkSlot;
    }

    private async Task<List<AuditLogEntry>> ReadAuditEntriesAsync(Guid aggregateId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogEntries.AsNoTracking()
            .Where(a => a.AggregateId == aggregateId)
            .OrderBy(a => a.OccurredAt)
            .ToListAsync(Ct);
    }

    private sealed record ListedSearch(Guid Id, string? Q, bool IsQuickLink);
}
