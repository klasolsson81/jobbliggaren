using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// What the account directory reads (#1974, ADR 0151): every account, its status by the login classifier's
/// rule, its counts and its dates, searched, filtered, sorted and paged. Every account a test makes carries
/// the test's token, and the admin a different one, so each search here is exact in the shared database.
/// </summary>
[Collection("Api")]
public sealed class AdminAccountsDirectoryTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_status_comes_from_its_actor_and_agrees_with_the_login_classifier()
    {
        var token = NewToken();
        var (client, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var active = await OpenActiveAsync(factory, Address(token, "active"), Ct);
        var missing = await CreateWithoutProfileAsync(factory, Address(token, "missing"), Ct);
        var pending = await CreatePendingDeletionAsync(factory, Address(token, "pending"), Ct);

        var byId = Items(await SearchOkAsync(client, new { address = token }, Ct))
            .ToDictionary(item => item.GetProperty("id").GetGuid());

        byId[active].GetProperty("status").GetString().ShouldBe("Active");
        byId[missing].GetProperty("status").GetString().ShouldBe("ProfileMissing");
        byId[pending].GetProperty("status").GetString().ShouldBe("PendingDeletion");

        await using var scope = factory.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<LoginSubjectResolver>();
        (await resolver.ResolveAsync(Address(token, "active"), Ct)).ShouldBeOfType<LoginSubject.Active>();
        (await resolver.ResolveAsync(Address(token, "missing"), Ct)).ShouldBeOfType<LoginSubject.ProfileMissing>();
        (await resolver.ResolveAsync(Address(token, "pending"), Ct)).ShouldBeOfType<LoginSubject.PendingDeletion>();
    }

    [Fact]
    public async Task Dates_come_from_the_profile_and_an_account_without_one_has_none()
    {
        var token = NewToken();
        var (client, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var active = await OpenActiveAsync(factory, Address(token, "active"), Ct);
        var missing = await CreateWithoutProfileAsync(factory, Address(token, "missing"), Ct);
        var pending = await CreatePendingDeletionAsync(factory, Address(token, "pending"), Ct);

        var byId = Items(await SearchOkAsync(client, new { address = token }, Ct))
            .ToDictionary(item => item.GetProperty("id").GetGuid());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var profiles = await db.JobSeekers.IgnoreQueryFilters().AsNoTracking()
            .Where(seeker => seeker.UserId == active || seeker.UserId == pending)
            .ToDictionaryAsync(seeker => seeker.UserId, Ct);

        byId[active].GetProperty("registeredAt").GetDateTimeOffset().ShouldBe(profiles[active].CreatedAt, TimeSpan.FromMilliseconds(1));
        byId[active].GetProperty("deletionEarliest").ValueKind.ShouldBe(JsonValueKind.Null);
        byId[missing].GetProperty("registeredAt").ValueKind.ShouldBe(JsonValueKind.Null);
        byId[missing].GetProperty("deletionEarliest").ValueKind.ShouldBe(JsonValueKind.Null);

        var deletedAt = profiles[pending].DeletedAt!.Value;
        byId[pending].GetProperty("deletionEarliest").GetString()
            .ShouldBe(AccountRestoreWindow.PermanentDeletionEarliest(deletedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Counts_follow_the_term_but_not_the_status_filter()
    {
        var token = NewToken();
        var (client, _, _) = await AdminAsync(factory, NewToken(), Ct);
        foreach (var label in new[] { "active-a", "active-b", "active-c" })
            await OpenActiveAsync(factory, Address(token, label), Ct);
        foreach (var label in new[] { "missing-a", "missing-b" })
            await CreateWithoutProfileAsync(factory, Address(token, label), Ct);
        await CreatePendingDeletionAsync(factory, Address(token, "pending"), Ct);

        var filtered = await SearchOkAsync(client, new { address = token, status = "Active" }, Ct);

        Items(filtered).ShouldAllBe(item => item.GetProperty("status").GetString() == "Active");
        filtered.GetProperty("accounts").GetProperty("totalCount").GetInt32().ShouldBe(3);
        var counts = filtered.GetProperty("counts");
        counts.GetProperty("total").GetInt32().ShouldBe(6);
        counts.GetProperty("active").GetInt32().ShouldBe(3);
        counts.GetProperty("pendingDeletion").GetInt32().ShouldBe(1);
        counts.GetProperty("profileMissing").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task The_term_is_a_case_insensitive_substring_whose_wildcards_are_literal()
    {
        var token = NewToken();
        var (client, _, _) = await AdminAsync(factory, NewToken(), Ct);
        await OpenActiveAsync(factory, Address(token, "under_score"), Ct);
        await OpenActiveAsync(factory, Address(token, "underxscore"), Ct);

        Emails(await SearchOkAsync(client, new { address = token.ToUpperInvariant() }, Ct)).Count.ShouldBe(2);
        Emails(await SearchOkAsync(client, new { address = $"under_score-{token}" }, Ct))
            .ShouldBe([Address(token, "under_score")]);
        Emails(await SearchOkAsync(client, new { address = $"%{token}" }, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Pages_are_bounded_complete_and_in_a_stable_order()
    {
        var token = NewToken();
        var (client, _, _) = await AdminAsync(factory, NewToken(), Ct);
        for (var index = 0; index < 5; index++)
            await OpenActiveAsync(factory, Address(token, $"page{index}"), Ct);

        var whole = Emails(await SearchOkAsync(client, new { address = token, pageSize = 100 }, Ct));
        whole.Count.ShouldBe(5);

        var paged = new List<string>();
        foreach (var page in new[] { 1, 2, 3 })
        {
            var search = await SearchOkAsync(client, new { address = token, page, pageSize = 2 }, Ct);
            search.GetProperty("accounts").GetProperty("totalCount").GetInt32().ShouldBe(5);
            search.GetProperty("accounts").GetProperty("page").GetInt32().ShouldBe(page);
            search.GetProperty("accounts").GetProperty("totalPages").GetInt32().ShouldBe(3);
            paged.AddRange(Emails(search));
        }
        paged.ShouldBe(whole);

        var pastTheEnd = await SearchOkAsync(client, new { address = token, page = 4, pageSize = 2 }, Ct);
        Items(pastTheEnd).ShouldBeEmpty();
        pastTheEnd.GetProperty("accounts").GetProperty("totalCount").GetInt32().ShouldBe(5);
    }

    [Fact]
    public async Task Each_sort_orders_by_its_key_then_by_id()
    {
        var token = NewToken();
        var (client, _, _) = await AdminAsync(factory, NewToken(), Ct);
        foreach (var label in new[] { "cc", "aa", "bb" })
            await OpenActiveAsync(factory, Address(token, label), Ct);

        Emails(await SearchOkAsync(client, new { address = token, sort = "AddressAscending" }, Ct))
            .ShouldBe([Address(token, "aa"), Address(token, "bb"), Address(token, "cc")]);
        Emails(await SearchOkAsync(client, new { address = token, sort = "AddressDescending" }, Ct))
            .ShouldBe([Address(token, "cc"), Address(token, "bb"), Address(token, "aa")]);

        var newest = Items(await SearchOkAsync(client, new { address = token, sort = "RegisteredNewest" }, Ct));
        var oldest = Items(await SearchOkAsync(client, new { address = token, sort = "RegisteredOldest" }, Ct));
        Ordered(newest, descending: true).ShouldBe(newest.Select(Id).ToList());
        Ordered(oldest, descending: false).ShouldBe(oldest.Select(Id).ToList());
    }

    [Fact]
    public async Task An_unknown_registration_sorts_last_both_ways_and_a_tie_breaks_on_the_id()
    {
        var token = NewToken();
        var (client, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var first = await OpenActiveAsync(factory, Address(token, "tie-a"), Ct);
        var second = await OpenActiveAsync(factory, Address(token, "tie-b"), Ct);
        var missing = await CreateWithoutProfileAsync(factory, Address(token, "missing"), Ct);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // The clock is the actor: two registrations in one tick give two profiles one created_at.
            var tick = DateTimeOffset.UtcNow.AddMinutes(-5);
            await scope.ServiceProvider.GetRequiredService<IAppDbContext>().JobSeekers
                .Where(seeker => seeker.UserId == first || seeker.UserId == second)
                .ExecuteUpdateAsync(set => set.SetProperty(seeker => seeker.CreatedAt, tick), Ct);
        }

        // Postgres orders a uuid by its bytes, which is the canonical string's order.
        var tied = new[] { first, second }.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToList();
        foreach (var sort in new[] { "RegisteredNewest", "RegisteredOldest" })
        {
            Items(await SearchOkAsync(client, new { address = token, sort }, Ct)).Select(Id)
                .ShouldBe([.. tied, missing], sort);
        }
    }

    [Fact]
    public async Task The_role_reads_the_admin_role_and_every_other_account_is_a_user()
    {
        var adminToken = NewToken();
        var (client, adminId, _) = await AdminAsync(factory, adminToken, Ct);
        var user = await OpenActiveAsync(factory, Address(adminToken, "plain"), Ct);

        var byId = Items(await SearchOkAsync(client, new { address = adminToken }, Ct))
            .ToDictionary(item => item.GetProperty("id").GetGuid());

        byId[adminId].GetProperty("role").GetString().ShouldBe("Admin");
        byId[user].GetProperty("role").GetString().ShouldBe("User");
    }

    [Fact]
    public async Task An_active_account_shows_its_live_counts_and_others_show_none()
    {
        var token = NewToken();
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var email = Address(token, "busy");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: Ct);
        var owner = factory.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        var applications = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            var created = await owner.PostAsJsonAsync(
                "/api/v1/applications", new { jobAdId = (Guid?)null, coverLetter = (string?)null }, Ct);
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
            applications.Add((await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid());
        }
        // The owner removes one: a removed application is no longer live and is not counted.
        (await owner.DeleteAsync($"/api/v1/applications/{applications[0]}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.PostAsJsonAsync("/api/v1/resumes", new { name = "CV", fullName = "Test" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        foreach (var name in new[] { "Sökning A", "Sökning B", "Sökning C" })
        {
            (await owner.PostAsJsonAsync("/api/v1/saved-searches", SavedSearch(name), Ct))
                .StatusCode.ShouldBe(HttpStatusCode.Created);
        }
        var busy = await UserIdAsync(factory, email, Ct);
        var idle = await OpenActiveAsync(factory, Address(token, "idle"), Ct);
        var missing = await CreateWithoutProfileAsync(factory, Address(token, "missing"), Ct);

        var detail = await DetailAsync(admin, busy);
        detail.GetProperty("applicationCount").GetInt32().ShouldBe(2);
        detail.GetProperty("resumeCount").GetInt32().ShouldBe(1);
        detail.GetProperty("savedSearchCount").GetInt32().ShouldBe(3);
        var rows = Items(await SearchOkAsync(admin, new { address = token }, Ct)).ToDictionary(Id);
        rows[busy].GetProperty("applicationCount").GetInt32().ShouldBe(2);
        rows[idle].GetProperty("applicationCount").GetInt32().ShouldBe(0);

        var none = await DetailAsync(admin, missing);
        none.GetProperty("status").GetString().ShouldBe("ProfileMissing");
        foreach (var count in new[] { "applicationCount", "resumeCount", "savedSearchCount" })
            none.GetProperty(count).ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_pending_deletion_shows_no_counts()
    {
        var token = NewToken();
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var pending = await CreatePendingDeletionAsync(factory, Address(token, "pending"), Ct);

        var detail = await DetailAsync(admin, pending);

        detail.GetProperty("status").GetString().ShouldBe("PendingDeletion");
        foreach (var count in new[] { "applicationCount", "resumeCount", "savedSearchCount" })
            detail.GetProperty(count).ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_list_and_the_detail_carry_exactly_their_fields_and_nothing_of_the_login()
    {
        var token = NewToken();
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var email = Address(token, "linked");
        var userId = await OpenActiveAsync(factory, email, Ct);
        string stamp;
        const string providerSubject = "provider-subject-sentinel-1974";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var linked = await scope.ServiceProvider.GetRequiredService<IExternalLoginWriter>()
                .LinkAsync(userId, ExternalProviderKey.Google, ExternalSubject.TryCreate(providerSubject)!.Value, Ct);
            linked.ShouldBe(ExternalLinkResult.Linked);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            stamp = (await users.FindByIdAsync(userId.ToString()))!.SecurityStamp!;
        }

        var listText = JsonSerializer.Serialize(await SearchOkAsync(admin, new { address = token }, Ct));
        var item = Items(await SearchOkAsync(admin, new { address = token }, Ct)).Single();
        item.EnumerateObject().Select(property => property.Name).ShouldBe(
            ["id", "email", "role", "status", "emailConfirmed", "registeredAt", "deletionEarliest", "applicationCount"],
            ignoreOrder: true);

        var detailResponse = await admin.GetAsync(DetailPath(userId), Ct);
        var detailText = await detailResponse.Content.ReadAsStringAsync(Ct);
        JsonDocument.Parse(detailText).RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(
            [
                "id", "email", "role", "status", "emailConfirmed", "registeredAt", "deletionEarliest",
                "applicationCount", "resumeCount", "savedSearchCount",
            ],
            ignoreOrder: true);

        foreach (var text in new[] { listText, detailText })
        {
            text.ShouldNotContain(providerSubject);
            text.ShouldNotContain(stamp);
        }
    }

    [Fact]
    public async Task Email_confirmed_reads_the_column()
    {
        // The current writer creates every account confirmed; only the retired password registration
        // (ADR 0142) left accounts unconfirmed, so that state is written here as that registration left it.
        var token = NewToken();
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var confirmed = await OpenActiveAsync(factory, Address(token, "confirmed"), Ct);
        var legacy = await OpenActiveAsync(factory, Address(token, "legacy"), Ct);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(legacy.ToString()))!;
            user.EmailConfirmed = false;
            (await users.UpdateAsync(user)).Succeeded.ShouldBeTrue();
        }

        var byId = Items(await SearchOkAsync(admin, new { address = token }, Ct))
            .ToDictionary(item => item.GetProperty("id").GetGuid());

        byId[confirmed].GetProperty("emailConfirmed").GetBoolean().ShouldBeTrue();
        byId[legacy].GetProperty("emailConfirmed").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task The_current_writer_creates_every_account_confirmed()
    {
        var created = await CreateWithoutProfileAsync(factory, Address(NewToken(), "fresh"), Ct);

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.FindByIdAsync(created.ToString()))!.EmailConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_search_records_no_recent_search()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var before = await RecentSearchCountAsync(factory, Ct);

        await SearchOkAsync(admin, new { address = NewToken() }, Ct);

        (await RecentSearchCountAsync(factory, Ct)).ShouldBe(before);
    }

    private static async Task<JsonElement> DetailAsync(HttpClient admin, Guid id)
    {
        var response = await admin.GetAsync(DetailPath(id), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static Guid Id(JsonElement item) => item.GetProperty("id").GetGuid();

    private static object SavedSearch(string name) => new
    {
        name,
        occupationGroup = new[] { "grp_12345" },
        municipality = (string[]?)null,
        region = (string[]?)null,
        q = "backend",
        sortBy = 0,
        notificationEnabled = false,
    };

    /// <summary>The items' ids in the order the sort promises: registration time, then id.</summary>
    private static List<Guid> Ordered(IReadOnlyList<JsonElement> items, bool descending)
    {
        var keyed = items.Select(item => (Registered: item.GetProperty("registeredAt").GetDateTimeOffset(), Id: Id(item)));
        var byTime = descending ? keyed.OrderByDescending(entry => entry.Registered) : keyed.OrderBy(entry => entry.Registered);
        return byTime.ThenBy(entry => entry.Id.ToString(), StringComparer.Ordinal).Select(entry => entry.Id).ToList();
    }
}
