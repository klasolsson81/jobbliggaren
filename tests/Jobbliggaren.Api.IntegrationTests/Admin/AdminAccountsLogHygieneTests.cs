using System.Net;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Admin.Accounts;
using Jobbliggaren.Infrastructure.Admin.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// The search term names a person, so it reaches no log record, no response body and no SQL text
/// (#1974, ADR 0151). The capture records each record's exception and scopes too, and the first check
/// proves it sees the request's own scope, so an absence here can fail.
/// </summary>
[Collection("Api")]
public sealed class AdminAccountsLogHygieneTests(ApiFactory factory)
{
    /// <summary>A term no account in the run has, so no mail to an address carrying it is ever logged.</summary>
    private const string Sentinel = "termsentinel1974";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_term_reaches_no_log_record_on_the_allowed_refused_and_invalid_paths()
    {
        var host = factory.GetRegistrationsClosedHost();
        var (admin, _, _) = await AdminAsync(host, NewToken(), Ct);
        var user = await UserAsync(host, NewToken(), Ct);

        (await SearchAsync(admin, new { address = Sentinel }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var invalid = await SearchAsync(admin, new { address = new string('q', 250) + Sentinel }, Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync(Ct)).ShouldNotContain(Sentinel);
        var refused = await SearchAsync(user, new { address = Sentinel }, Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync(Ct)).ShouldNotContain(Sentinel);

        var logs = factory.ClosedHostLogs.ToList();
        logs.ShouldContain(log => log.Scopes.Any(scope => scope.Contains(SearchPath, StringComparison.Ordinal)));
        logs.Where(log => log.AllText.Contains(Sentinel, StringComparison.OrdinalIgnoreCase))
            .Select(log => $"{log.Category}: {log.Message}")
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task The_term_is_bound_as_a_parameter_and_never_written_into_the_sql()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var directory = (SqlAccountDirectory)scope.ServiceProvider.GetRequiredService<IAccountDirectory>();
        var address = directory.NormalizedAddress(Sentinel);
        await using var connection = new NpgsqlConnection();

        await using var page = directory.PageCommand(
            connection, address, new AccountDirectorySearch(Sentinel, AccountStatus.Active, AccountSort.AddressAscending, 1, 25));
        await using var counts = directory.CountsCommand(connection, address);

        foreach (var command in new[] { page, counts })
        {
            command.CommandText.ShouldNotContain(Sentinel, Case.Insensitive);
            command.Parameters["@address"].Value.ShouldBe($"%{Sentinel.ToUpperInvariant()}%");
        }
    }

    [Fact]
    public async Task The_term_s_like_wildcards_are_escaped_and_a_blank_term_is_no_filter()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var directory = (SqlAccountDirectory)scope.ServiceProvider.GetRequiredService<IAccountDirectory>();

        directory.NormalizedAddress(@"a%b_c\d").ShouldBe(@"%A\%B\_C\\D%");
        directory.NormalizedAddress("   ").ShouldBeNull();
        directory.NormalizedAddress(null).ShouldBeNull();
    }
}
