using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Admin.Accounts;
using Jobbliggaren.Application.Admin.Accounts.Queries.GetAccountOverview;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

[Collection("Api")]
public sealed class AdminAccountOverviewTests(ApiFactory factory, ITestOutputHelper output)
{
    private const string OverviewPath = "/api/v1/admin/overview/accounts";
    private static readonly string[] StatusFields = ["active", "profileMissing", "pendingDeletion", "suspended"];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Overview_ShouldPartitionRetainedAccounts_WhenProductionTransitionsOverlap()
    {
        var admin = await AccountEmailChangeKit.AdminAsync(factory, NewToken(), Ct);
        var before = await OverviewAsync(admin.Client);
        var token = NewToken();
        await OpenActiveAsync(factory, Address(token, "active"), Ct);
        await CreateWithoutProfileAsync(factory, Address(token, "missing"), Ct);
        var pending = await CreatePendingDeletionAsync(factory, Address(token, "pending"), Ct);
        var suspended = await OpenActiveAsync(factory, Address(token, "suspended"), Ct);
        await SuspendAsync(admin, suspended);
        // The admin suspension command admits an already pending-deletion account; deletion takes precedence.
        await SuspendAsync(admin, pending, subsequent: true);

        var after = await OverviewAsync(admin.Client);

        after.GetProperty("sampledAt").GetDateTimeOffset().ShouldBeGreaterThanOrEqualTo(
            before.GetProperty("sampledAt").GetDateTimeOffset());
        var countsBefore = before.GetProperty("counts");
        var countsAfter = after.GetProperty("counts");
        foreach (var field in StatusFields)
            countsAfter.GetProperty(field).GetInt32().ShouldBe(countsBefore.GetProperty(field).GetInt32() + 1);
        countsAfter.GetProperty("total").GetInt32().ShouldBe(countsBefore.GetProperty("total").GetInt32() + 4);
        countsAfter.GetProperty("total").GetInt32().ShouldBe(
            StatusFields.Sum(field => countsAfter.GetProperty(field).GetInt32()));

        var listed = await SearchOkAsync(admin.Client, new { address = token }, Ct);
        Items(listed).Count.ShouldBe(4);
        foreach (var field in StatusFields)
            listed.GetProperty("counts").GetProperty(field).GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Directory_ShouldCountProfileRegistrations_WhenSuspendedAndDeletedProfilesAreRetained()
    {
        var admin = await AccountEmailChangeKit.AdminAsync(factory, NewToken(), Ct);
        var token = NewToken();
        var day = new DateOnly(2026, 7, 15);
        var window = new SwedishCalendar().DayWindow(day);
        var before = await ReadDirectoryAsync(window);
        var active = await OpenActiveAsync(factory, Address(token, "active"), Ct);
        var suspended = await OpenActiveAsync(factory, Address(token, "suspended"), Ct);
        var pending = await CreatePendingDeletionAsync(factory, Address(token, "pending"), Ct);
        await CreateWithoutProfileAsync(factory, Address(token, "missing"), Ct);
        await SuspendAsync(admin, suspended);
        await SetRegistrationTimeAsync([active, suspended, pending], window.Start.AddHours(1));

        var after = await ReadDirectoryAsync(window);

        after.Counts.Total.ShouldBe(before.Counts.Total + 4);
        after.Days.Single(row => row.Date == day).NewAccounts
            .ShouldBe(before.Days.Single(row => row.Date == day).NewAccounts + 3);
        var list = await SearchOkAsync(admin.Client, new
        {
            address = token,
            registeredFrom = window.Start,
            registeredBefore = window.End,
        }, Ct);
        Items(list).Select(row => row.GetProperty("id").GetGuid()).ShouldBe([active, suspended, pending], ignoreOrder: true);
        list.GetProperty("counts").GetProperty("total").GetInt32().ShouldBe(3);
        list.GetProperty("counts").GetProperty("profileMissing").GetInt32().ShouldBe(0);
        var statusFiltered = await SearchOkAsync(admin.Client, new
        {
            address = token,
            registeredFrom = window.Start,
            registeredBefore = window.End,
            status = "Suspended",
        }, Ct);
        Items(statusFiltered).ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(suspended);
        statusFiltered.GetProperty("accounts").GetProperty("totalCount").GetInt32().ShouldBe(1);
        statusFiltered.GetProperty("counts").GetProperty("total").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task Directory_ShouldDegradeToProfileMissing_WhenAnUnreachableSuspendedOrphanBreaksTheInvariant()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var token = NewToken();
        var orphan = await CreateWithoutProfileAsync(factory, Address(token, "broken"), Ct);
        // Unreachable: the suspension command refuses a missing profile. This deliberately broken historical
        // orphan asserts only that the read side degrades safely, retaining ProfileMissing over suspension.
        await using (var scope = factory.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Users.Where(user => user.Id == orphan)
                .ExecuteUpdateAsync(update => update.SetProperty(user => user.IsSuspended, true), Ct)).ShouldBe(1);

        var result = await SearchOkAsync(admin, new { address = token }, Ct);

        Items(result).ShouldHaveSingleItem().GetProperty("status").GetString().ShouldBe("ProfileMissing");
        result.GetProperty("counts").GetProperty("profileMissing").GetInt32().ShouldBe(1);
        result.GetProperty("counts").GetProperty("suspended").GetInt32().ShouldBe(0);
    }

    [Theory]
    [InlineData("2026-03-29", 23)]
    [InlineData("2026-10-25", 25)]
    public async Task DirectoryAndDrillDown_ShouldUseHalfOpenCalendarBounds_WhenTheDayHasUnequalHours(
        string civilDate, int expectedHours)
    {
        var day = DateOnly.Parse(civilDate, System.Globalization.CultureInfo.InvariantCulture);
        var window = new SwedishCalendar().DayWindow(day);
        (window.End - window.Start).ShouldBe(TimeSpan.FromHours(expectedHours));
        var before = await ReadDirectoryAsync(window);
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var token = NewToken();
        var leftOut = await OpenActiveAsync(factory, Address(token, "before"), Ct);
        var atStart = await OpenActiveAsync(factory, Address(token, "start"), Ct);
        var last = await OpenActiveAsync(factory, Address(token, "last"), Ct);
        var atEnd = await OpenActiveAsync(factory, Address(token, "end"), Ct);
        await SetRegistrationTimeAsync([leftOut], window.Start.AddMilliseconds(-1));
        await SetRegistrationTimeAsync([atStart], window.Start);
        await SetRegistrationTimeAsync([last], window.End.AddMilliseconds(-1));
        await SetRegistrationTimeAsync([atEnd], window.End);

        var aggregate = await ReadDirectoryAsync(window);
        aggregate.Days.Single(row => row.Date == day).NewAccounts
            .ShouldBe(before.Days.Single(row => row.Date == day).NewAccounts + 2);
        foreach (var sort in new[] { "RegisteredNewest", "RegisteredOldest", "AddressAscending" })
        {
            var first = await SearchOkAsync(admin, new
            {
                address = token,
                registeredFrom = window.Start,
                registeredBefore = window.End,
                sort,
                pageSize = 1,
            }, Ct);
            var second = await SearchOkAsync(admin, new
            {
                address = token,
                registeredFrom = window.Start,
                registeredBefore = window.End,
                sort,
                pageSize = 1,
                page = 2,
            }, Ct);
            var ids = Items(first).Concat(Items(second)).Select(row => row.GetProperty("id").GetGuid());
            ids.ShouldBe([atStart, last], ignoreOrder: true);
            first.GetProperty("accounts").GetProperty("totalCount").GetInt32().ShouldBe(2);
            first.GetProperty("counts").GetProperty("total").GetInt32().ShouldBe(2);
        }
    }

    [Fact]
    public async Task Directory_ShouldExcludeAnErasedAccount_WhenTheHardDeleterAdmitsItsPendingProfile()
    {
        var token = NewToken();
        var pending = await CreatePendingDeletionAsync(factory, Address(token, "erased"), Ct);
        var window = new SwedishCalendar().DayWindow(new DateOnly(2026, 1, 15));
        await SetRegistrationTimeAsync([pending], window.Start.AddHours(1));
        var before = await ReadDirectoryAsync(window);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var now = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().UtcNow.AddDays(32);
            var clock = new MutableFakeDateTimeProvider { UtcNow = now };
            var deleter = ActivatorUtilities.CreateInstance<AccountHardDeleter>(scope.ServiceProvider, clock);
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var profileId = await db.JobSeekers.IgnoreQueryFilters().Where(row => row.UserId == pending)
                .Select(row => row.Id).SingleAsync(Ct);
            (await deleter.GetAccountsReadyForHardDeleteAsync(now.AddDays(-30), Ct)).ShouldContain(profileId.Value);
            await deleter.HardDeleteAccountAsync(profileId.Value, Ct);
        }

        var after = await ReadDirectoryAsync(window);

        after.Counts.Total.ShouldBe(before.Counts.Total - 1);
        after.Counts.PendingDeletion.ShouldBe(before.Counts.PendingDeletion - 1);
        after.Days.Single(row => row.Date == window.Day).NewAccounts
            .ShouldBe(before.Days.Single(row => row.Date == window.Day).NewAccounts - 1);
    }

    [Fact]
    public async Task Overview_ShouldMatchTheUnfilteredDirectory_AndCarryOnlyAggregatesAndBoundedDays()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var overview = await OverviewAsync(admin);
        var listed = await SearchOkAsync(admin, new { pageSize = 1 }, Ct);

        overview.EnumerateObject().Select(field => field.Name)
            .ShouldBe(["sampledAt", "counts", "newAccounts", "days"], ignoreOrder: true);
        foreach (var field in new[] { "active", "profileMissing", "pendingDeletion", "suspended", "total" })
            overview.GetProperty("counts").GetProperty(field).GetInt32()
                .ShouldBe(listed.GetProperty("counts").GetProperty(field).GetInt32());
        var days = overview.GetProperty("days").EnumerateArray().ToArray();
        days.Length.ShouldBe(90);
        var calendar = new SwedishCalendar();
        var today = calendar.DayOf(overview.GetProperty("sampledAt").GetDateTimeOffset());
        for (var index = 0; index < days.Length; index++)
        {
            days[index].EnumerateObject().Select(field => field.Name).ShouldBe(["date", "newAccounts"], ignoreOrder: true);
            days[index].GetProperty("date").GetString().ShouldBe(
                today.AddDays(index - 89).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            days[index].GetProperty("newAccounts").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        }
        var rollups = overview.GetProperty("newAccounts");
        rollups.GetProperty("last7Days").GetProperty("count").GetInt32()
            .ShouldBe(days.TakeLast(7).Sum(row => row.GetProperty("newAccounts").GetInt32()));
        rollups.GetProperty("last30Days").GetProperty("count").GetInt32()
            .ShouldBe(days.TakeLast(30).Sum(row => row.GetProperty("newAccounts").GetInt32()));
    }

    [Fact]
    public async Task Overview_ShouldObservePlanAndLatency_WhenTheRetainedMvpPopulationIsPresent()
    {
        const int populationSize = 200;
        const int handlerWarmups = 20;
        const int handlerSamples = 100;
        const int httpWarmups = 2;
        const int httpSamples = 20;
        const double handlerBudgetMilliseconds = 300;
        var report = new List<string>();
        var admin = await AccountEmailChangeKit.AdminAsync(factory, NewToken(), Ct);
        var baseline = await OverviewAsync(admin.Client);
        var baselineTotal = baseline.GetProperty("counts").GetProperty("total").GetInt32();
        var token = NewToken();
        var calendar = new SwedishCalendar();
        var fixtureTime = factory.Services.GetRequiredService<IDateTimeProvider>().UtcNow;
        var today = calendar.DayOf(fixtureTime);
        var suspensions = 0;
        for (var index = 0; index < populationSize; index++)
        {
            var email = Address(token, $"probe{index}");
            if (index % 10 == 0)
            {
                await CreateWithoutProfileAsync(factory, email, Ct);
                continue;
            }
            var account = index % 10 == 1
                ? await CreatePendingDeletionAsync(factory, email, Ct)
                : await OpenActiveAsync(factory, email, Ct);
            if (index % 10 == 2 && index < 40)
                await SuspendAsync(admin, account, subsequent: suspensions++ > 0);
            var day = today.AddDays(-(index % 90));
            var registrationTime = day == today ? fixtureTime.AddSeconds(-1) : calendar.DayWindow(day).Start.AddHours(12);
            await SetRegistrationTimeAsync([account], registrationTime);
        }

        await using var host = factory.WithWebHostBuilder(builder => builder.UseUrls("http://127.0.0.1:0"));
        host.UseKestrel(0);
        using var startupClient = host.CreateClient();
        var server = host.Services.GetRequiredService<IServer>();
        var listener = server.Features.Get<IServerAddressesFeature>().ShouldNotBeNull().Addresses
            .Select(address => new Uri(address, UriKind.Absolute))
            .Where(address => address.Scheme == Uri.UriSchemeHttp && address.IsLoopback && address.Port > 0)
            .ShouldHaveSingleItem();

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(Ct);
        // The fixture pins the post-auto-analyze statistics regime, rather than depending on its timer.
        await using (var analyze = new NpgsqlCommand("ANALYZE identity.\"AspNetUsers\"; ANALYZE public.job_seekers", connection))
            await analyze.ExecuteNonQueryAsync(Ct);
        var handler = ActivatorUtilities.CreateInstance<GetAccountOverviewQueryHandler>(scope.ServiceProvider);
        var capturedPlans = new List<JsonElement>();
        void CapturePlan(object? sender, NpgsqlNoticeEventArgs args)
        {
            var message = args.Notice.MessageText;
            var opening = message.IndexOf('{');
            if (opening < 0)
                return;
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(message[opening..]));
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("Query Text", out var query)
                && query.GetString()!.Contains(", totals AS (", StringComparison.Ordinal)
                && query.GetString()!.Contains(", daily AS (", StringComparison.Ordinal))
                capturedPlans.Add(document.RootElement.Clone());
        }
        connection.Notice += CapturePlan;
        try
        {
            // auto_explain instruments the actual raw Npgsql command, with its real bindings; no SQL copy is used.
            await using var explainSettings = new NpgsqlCommand("""
                LOAD 'auto_explain';
                SET auto_explain.log_analyze = on;
                SET auto_explain.log_buffers = on;
                SET auto_explain.log_format = 'json';
                SET auto_explain.log_level = 'notice';
                SET auto_explain.log_parameter_max_length = 0;
                SET auto_explain.log_min_duration = 0;
                """, connection);
            await explainSettings.ExecuteNonQueryAsync(Ct);
            var observed = await handler.Handle(new GetAccountOverviewQuery(), Ct);
            observed.Counts.Total.ShouldBe(baselineTotal + populationSize);
            observed.Days.Count.ShouldBe(90);
        }
        finally
        {
            connection.Notice -= CapturePlan;
            await using var reset = new NpgsqlCommand("RESET ALL", connection);
            await reset.ExecuteNonQueryAsync(Ct);
        }
        var captured = capturedPlans.ShouldHaveSingleItem();
        var sql = captured.GetProperty("Query Text").GetString().ShouldNotBeNull();
        var rootPlan = captured.GetProperty("Plan");
        rootPlan.GetProperty("Actual Rows").GetDouble().ShouldBe(90d);
        rootPlan.TryGetProperty("Shared Hit Blocks", out _).ShouldBeTrue();
        report.Add(FormattableString.Invariant($"EXPLAIN ANALYZE/BUFFERS: server auto_explain notice; actual_sql_template_sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)))}."));
        var printedNodes = 0;
        AppendPlanNodes(rootPlan, report, depth: 0, ref printedNodes);

        for (var sample = 0; sample < handlerWarmups; sample++)
            await handler.Handle(new GetAccountOverviewQuery(), Ct);
        var handlerTimes = new double[handlerSamples];
        for (var sample = 0; sample < handlerTimes.Length; sample++)
        {
            var started = Stopwatch.GetTimestamp();
            var result = await handler.Handle(new GetAccountOverviewQuery(), Ct);
            handlerTimes[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            result.Counts.Total.ShouldBe(baselineTotal + populationSize);
        }
        Array.Sort(handlerTimes);
        var handlerP95 = Percentile(handlerTimes, 0.95);
        report.Add(FormattableString.Invariant($"OBSERVE-ONLY handler: warmup={handlerWarmups}; samples={handlerSamples}; p50_ms={Percentile(handlerTimes, 0.50):F3}; p95_ms={handlerP95:F3}; p99_ms={Percentile(handlerTimes, 0.99):F3}."));
        report.Add($"ADR 0045 read/list handler p95 budget=300 ms; verdict={(handlerP95 <= handlerBudgetMilliseconds ? "within_budget" : "over_budget_requires_verdict")}; no timing assertion.");

        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
        {
            BaseAddress = listener,
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.SessionId);
        for (var sample = 0; sample < httpWarmups; sample++)
            await ObserveHttpAsync(client);
        var httpTimes = new double[httpSamples];
        for (var sample = 0; sample < httpTimes.Length; sample++)
            httpTimes[sample] = await ObserveHttpAsync(client);
        Array.Sort(httpTimes);
        report.Add(FormattableString.Invariant($"OBSERVE-ONLY authenticated loopback HTTP: warmup={httpWarmups}; samples={httpSamples}; all=200; p50_ms={Percentile(httpTimes, 0.50):F3}; p95_ms={Percentile(httpTimes, 0.95):F3}; p99_ms={Percentile(httpTimes, 0.99):F3}."));
        report.Add(FormattableString.Invariant($"Population: baseline_retained={baselineTotal}; added=200 (active=156, pending_deletion=20, suspended=4, historical_profile_missing=20); observed_total={baselineTotal + populationSize}; registration dates span 90 Swedish days."));
        report.Add("Provenance: AccountRegistrar.OpenAsync, real self-service deletion and admin suspension; historical incomplete rows cite AccountRegistrationAtomicityTests; the clock supplies registration instants. Tables analyzed once to reproduce a statistics regime continuous production DML can produce.");
        report.Add("Limits: 200 added accounts for the current MVP; shared local hardware and Development host; warmed direct handler with reused DbContext/connection; HTTP separately includes real socket transport, auth and limiter. No TLS, concurrency or 10k-capacity conclusion; no production auto-analyze claim.");
        var reportPath = Environment.GetEnvironmentVariable("JBL_ADMIN_OVERVIEW_PERFORMANCE_OUTPUT")
            ?? Path.Combine(AppContext.BaseDirectory, "admin-overview-performance.txt");
        var parent = Path.GetDirectoryName(Path.GetFullPath(reportPath)).ShouldNotBeNull();
        Directory.CreateDirectory(parent);
        await File.WriteAllLinesAsync(reportPath, report, Ct);
        foreach (var line in report)
            output.WriteLine(line);
        output.WriteLine($"Performance artifact: {Path.GetFullPath(reportPath)}");
    }

    private static void AppendPlanNodes(JsonElement plan, List<string> report, int depth, ref int printed)
    {
        if (printed >= 32 || depth > 12)
            return;
        printed++;
        static string Number(JsonElement node, string field) =>
            node.TryGetProperty(field, out var value) ? value.GetRawText() : "0";
        report.Add(FormattableString.Invariant($"plan_node={printed}; depth={depth}; type={plan.GetProperty("Node Type").GetString()}; estimated_rows={Number(plan, "Plan Rows")}; actual_rows={Number(plan, "Actual Rows")}; loops={Number(plan, "Actual Loops")}; total_ms={Number(plan, "Actual Total Time")}; shared_hits={Number(plan, "Shared Hit Blocks")}; shared_reads={Number(plan, "Shared Read Blocks")}; temp_reads={Number(plan, "Temp Read Blocks")}; temp_writes={Number(plan, "Temp Written Blocks")}."));
        if (plan.TryGetProperty("Plans", out var children))
            foreach (var child in children.EnumerateArray())
                AppendPlanNodes(child, report, depth + 1, ref printed);
    }

    private static double Percentile(double[] sorted, double fraction) => sorted[(int)Math.Ceiling(sorted.Length * fraction) - 1];

    private static async Task<double> ObserveHttpAsync(HttpClient client)
    {
        var started = Stopwatch.GetTimestamp();
        using var response = await client.GetAsync(OverviewPath, HttpCompletionOption.ResponseContentRead, Ct);
        var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull().Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
        return milliseconds;
    }

    private async Task SuspendAsync(AccountEmailChangeKit.Admin admin, Guid target, bool subsequent = false)
    {
        if (subsequent)
            await ReauthTestHelpers.LetTheCooldownLapseAsync(factory, admin.Email, Ct);
        var grant = await ReauthTestHelpers.MintGrantAsync(factory, admin.Client, admin.SessionId, admin.Email, Ct);
        var response = await admin.Client.PostAsJsonAsync($"/api/v1/admin/accounts/{target}/suspend", new { reauthGrant = grant }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task<AccountDirectoryOverview> ReadDirectoryAsync(CivilDayWindow target)
    {
        // Production supplies 90 days ending at the clock's current partial day. The target is yesterday here.
        var calendar = new SwedishCalendar();
        var today = target.Day.AddDays(1);
        var days = Enumerable.Range(0, 90).Select(index => calendar.DayWindow(today.AddDays(index - 89))).ToArray();
        days[^1] = days[^1] with { End = days[^1].Start.AddHours(1) };
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountDirectory>().GetOverviewAsync(days, Ct);
    }

    private async Task SetRegistrationTimeAsync(IReadOnlyList<Guid> accounts, DateTimeOffset instant)
    {
        // The clock is the actor: registration at this instant writes the profile's created_at.
        // IgnoreQueryFilters retains profiles soft-deleted through the real self-service deletion above.
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IAppDbContext>().JobSeekers.IgnoreQueryFilters()
            .Where(profile => accounts.Contains(profile.UserId))
            .ExecuteUpdateAsync(update => update.SetProperty(profile => profile.CreatedAt, instant), Ct))
            .ShouldBe(accounts.Count);
    }

    private static async Task<JsonElement> OverviewAsync(HttpClient admin)
    {
        var response = await admin.GetAsync(OverviewPath, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        response.Headers.CacheControl.ShouldNotBeNull().Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
