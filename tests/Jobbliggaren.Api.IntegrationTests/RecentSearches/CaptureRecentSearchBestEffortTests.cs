using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.SavedSearches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RecentSearches;

/// <summary>
/// #2285 (security-auditor Minor 1, #2285 plan read) — a store that fails after it tracked its row answers
/// <c>stored: false</c> with a 200, through the real pipeline and its unit of work, and leaves no row: the handler clears
/// the tracker, so the unit of work's save after it sends nothing. A 200 also shows the exception never reached
/// <c>LoggingBehavior</c>, whose failure line logs the whole exception.
/// </summary>
/// <remarks>
/// The state is one <c>RecentJobSearchCapturer.CaptureAsync</c> produces: it adds the row, then its
/// <c>SaveChangesAsync</c> throws anything other than a unique violation, which it does not catch. The stand-in below
/// adds the row through the production factory, <see cref="RecentJobSearch.Capture"/>, and throws at that point.
/// </remarks>
[Collection("Api")]
public sealed class CaptureRecentSearchBestEffortTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_store_that_fails_after_tracking_its_row_answers_not_stored_and_leaves_no_row()
    {
        var session = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, ct: Ct);
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRecentJobSearchCapturer>();
            services.AddScoped<IRecentJobSearchCapturer, AddThenThrowCapturer>();
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);

        var response = await client.PostAsJsonAsync("/api/v1/me/recent-searches", new { q = "backend" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("stored").GetBoolean().ShouldBeFalse();

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me", Ct);
        var userId = Guid.Parse(me.GetProperty("userId").GetString()!);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = await db.JobSeekers.AsNoTracking().SingleAsync(js => js.UserId == userId, Ct);
        (await db.RecentJobSearches.AsNoTracking().CountAsync(r => r.JobSeekerId == seeker.Id, Ct)).ShouldBe(0);
    }

    private sealed class AddThenThrowCapturer(IAppDbContext db, IDateTimeProvider clock) : IRecentJobSearchCapturer
    {
        public async Task<bool> CaptureAsync(
            Guid userId, SearchCriteria criteria, int currentCount, CancellationToken cancellationToken)
        {
            var seekerId = await db.JobSeekers
                .Where(js => js.UserId == userId)
                .Select(js => js.Id)
                .SingleAsync(cancellationToken);
            db.RecentJobSearches.Add(RecentJobSearch.Capture(seekerId, criteria, currentCount, clock.UtcNow));
            throw new InvalidOperationException("the store failed after it tracked its row");
        }
    }
}
