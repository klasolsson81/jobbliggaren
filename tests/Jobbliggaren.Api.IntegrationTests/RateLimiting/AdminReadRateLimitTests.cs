using System.Net;
using Jobbliggaren.Api.RateLimiting;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;

namespace Jobbliggaren.Api.IntegrationTests.RateLimiting;

/// <summary>
/// #1974 (ADR 0151 D5) — the admin-read bucket on a host with the production numbers: one admin's burst ends in a
/// 429 with Retry-After and no queue, and another admin's bucket is untouched. The refused search's term reaches
/// neither its answer nor a log record.
/// </summary>
[Collection("StrictRateLimit")]
public sealed class AdminReadRateLimitTests(StrictRateLimitApiFactory factory)
{
    /// <summary>A term no account in the run has, so no mail to an address carrying it is ever logged.</summary>
    private const string Sentinel = "ratesentinel1974";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_admin_s_burst_ends_in_429_with_Retry_After_while_another_admin_still_reads()
    {
        var (first, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var (second, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var burst = new RateLimitingOptions().AdminRead.PermitLimit;

        var answered = 0;
        HttpResponseMessage? rejected = null;
        for (var attempt = 0; attempt < burst * 2 && rejected is null; attempt++)
        {
            var response = await SearchAsync(first, new { address = Sentinel }, Ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                rejected = response;
            else
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                answered++;
            }
        }

        answered.ShouldBeGreaterThanOrEqualTo(burst);
        rejected.ShouldNotBeNull();
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        (await rejected.Content.ReadAsStringAsync(Ct)).ShouldNotContain(Sentinel, Case.Insensitive);
        (await SearchAsync(second, new { address = Sentinel }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var logs = factory.Logs.Logs.ToList();
        logs.ShouldContain(log => log.Scopes.Any(scope => scope.Contains(SearchPath, StringComparison.Ordinal)));
        logs.Where(log => log.AllText.Contains(Sentinel, StringComparison.OrdinalIgnoreCase))
            .Select(log => $"{log.Category}: {log.Message}")
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task The_Backup_card_s_429_is_private_and_uncacheable_like_its_other_refusals()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var burst = new RateLimitingOptions().AdminRead.PermitLimit;

        HttpResponseMessage? rejected = null;
        for (var attempt = 0; attempt < burst * 2 && rejected is null; attempt++)
        {
            var response = await admin.GetAsync("/api/v1/admin/overview/backup", Ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                rejected = response;
        }

        rejected.ShouldNotBeNull();
        rejected.Headers.CacheControl.ShouldNotBeNull().Private.ShouldBeTrue();
        rejected.Headers.CacheControl.NoStore.ShouldBeTrue();
    }
}
