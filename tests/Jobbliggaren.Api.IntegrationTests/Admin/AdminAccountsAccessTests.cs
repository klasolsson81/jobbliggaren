using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.Endpoints;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Admin.Accounts.Queries.SearchAccounts;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Validation;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// Who may read the account directory (#1974, ADR 0151): the Admin policy, resolved on every request, and
/// the request's own bounds, answered before the directory runs.
/// </summary>
[Collection("Api")]
public sealed class AdminAccountsAccessTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Both_reads_answer_401_without_a_session()
    {
        var client = factory.CreateClient();

        (await SearchAsync(client, new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync(DetailPath(Guid.NewGuid()), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Both_reads_answer_403_to_an_account_without_the_admin_role()
    {
        var client = await UserAsync(factory, NewToken(), Ct);

        (await SearchAsync(client, new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync(DetailPath(Guid.NewGuid()), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_admin_whose_role_is_removed_is_refused_on_the_next_request_of_the_same_session()
    {
        var (client, userId, _) = await AdminAsync(factory, NewToken(), Ct);
        (await SearchAsync(client, new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync(DetailPath(userId), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await DemoteAsync(factory, userId);

        (await SearchAsync(client, new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync(DetailPath(userId), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_admin_who_logged_out_gets_401()
    {
        var (client, userId, _) = await AdminAsync(factory, NewToken(), Ct);

        (await client.PostAsync("/api/v1/auth/logout", content: null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await SearchAsync(client, new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync(DetailPath(userId), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Every_answer_is_private_and_never_stored_and_a_missing_account_is_a_plain_404()
    {
        var (client, _, _) = await AdminAsync(factory, NewToken(), Ct);

        var found = await SearchAsync(client, new { }, Ct);
        ShouldBePrivateAndUnstored(found);

        var missing = await client.GetAsync(DetailPath(Guid.NewGuid()), Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ShouldBePrivateAndUnstored(missing);

        var refused = await SearchAsync(client, new { pageSize = SearchAccountsQuery.MaxPageSize + 1 }, Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ShouldBePrivateAndUnstored(refused);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(SearchAccountsQuery.MaxPage + 1, 25)]
    [InlineData(1, 0)]
    [InlineData(1, SearchAccountsQuery.MaxPageSize + 1)]
    public async Task A_page_outside_the_bounds_is_400(int page, int pageSize)
    {
        var (client, _, _) = await AdminAsync(factory, NewToken(), Ct);

        (await SearchAsync(client, new { page, pageSize }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_term_as_long_as_an_address_may_be_is_read()
    {
        var (client, _, _) = await AdminAsync(factory, NewToken(), Ct);

        (await SearchAsync(client, new { address = new string('q', EmailAddressRules.MaximumLength) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_term_longer_than_an_address_or_with_a_control_character_is_400_and_never_echoed()
    {
        var (client, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var tooLong = new string('q', EmailAddressRules.MaximumLength - 16) + "-sentinel-term-15";
        var withControl = "sentinel\u0001term";

        foreach (var address in new[] { tooLong, withControl })
        {
            var response = await SearchAsync(client, new { address }, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("sentinel");
        }
    }

    [Fact]
    public async Task An_unknown_status_or_sort_is_400()
    {
        var (client, _, _) = await AdminAsync(factory, NewToken(), Ct);

        (await SearchAsync(client, new { status = 99 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SearchAsync(client, new { sort = 99 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static void ShouldBePrivateAndUnstored(HttpResponseMessage response)
    {
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl.Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
    }

    [Fact]
    public void The_search_request_prints_its_term_redacted()
    {
        var request = new AdminAccountsEndpoints.AccountSearchRequest(Address: "sentinel-request-1974");

        request.ToString().ShouldNotContain("sentinel-request-1974");
        request.ToString().ShouldContain("redacted");
    }

    [Fact]
    public async Task An_account_that_was_removed_answers_the_same_404_as_one_that_never_existed()
    {
        var (client, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var removed = await CreateWithoutProfileAsync(factory, Address(NewToken(), "removed"), Ct);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // Removed by the registrar's own compensating delete (AccountRegistrar), a path production runs.
            await scope.ServiceProvider.GetRequiredService<IPasswordlessAccountCreator>().DeleteAsync(removed, Ct);
        }

        var gone = await client.GetAsync(DetailPath(removed), Ct);
        var never = await client.GetAsync(DetailPath(Guid.NewGuid()), Ct);

        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        never.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        gone.Headers.CacheControl?.ToString().ShouldBe(never.Headers.CacheControl?.ToString());
        (await gone.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(await never.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task An_empty_id_is_400()
    {
        var (client, _, _) = await AdminAsync(factory, NewToken(), Ct);

        (await client.GetAsync(DetailPath(Guid.Empty), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_search_is_not_a_get()
    {
        var (client, _, _) = await AdminAsync(factory, NewToken(), Ct);

        (await client.GetAsync(SearchPath, Ct)).StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }
}
