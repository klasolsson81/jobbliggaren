using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

[CollectionDefinition("AccountAccessLifecycle")]
public sealed class AccountAccessLifecycleTestGroup;

// Each case owns migrated PostgreSQL and real Redis; Admin-role counts never depend on the shared Api collection.
[Collection("AccountAccessLifecycle")]
public sealed class LastEffectiveAdministratorTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string DeletePath = "/api/v1/me/delete";
    private static string SuspendPath(Guid userId) => $"/api/v1/admin/accounts/{userId}/suspend";

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Delete_ShouldRefuseTheSoleEffectiveAdministrator_AfterGenuineInboxReauthentication()
    {
        var admin = await BootstrapAdministratorAsync();
        await AddProviderLoginAsync(admin.UserId);
        var grant = await GrantAsync(admin);
        var before = await PrimaryAsync(admin.UserId);
        (await EffectiveAdministratorsAsync()).ShouldBe([admin.UserId]);

        var response = await ReauthTestHelpers.PostAsSessionAsync(
            admin.Client, admin.SessionId, DeletePath, new { reauthGrant = grant }, Ct);

        await ShouldBeLastAdministratorAsync(response);
        (await PrimaryAsync(admin.UserId)).ShouldBe(before);
        before.Access.HasLiveProfile.ShouldBeTrue();
        before.ProviderLogins.ShouldBe(1);
        before.DeletionAudits.ShouldBe(0);
        (await EffectiveAdministratorsAsync()).ShouldBe([admin.UserId]);
        (await MeAsync(admin)).ShouldBe(HttpStatusCode.OK);
    }

    public enum ExcludedHolder { OrdinaryAccount, Suspended, PendingDeletion, HistoricalMissingProfile }

    [Theory]
    [InlineData(ExcludedHolder.OrdinaryAccount)]
    [InlineData(ExcludedHolder.Suspended)]
    [InlineData(ExcludedHolder.PendingDeletion)]
    [InlineData(ExcludedHolder.HistoricalMissingProfile)]
    public async Task Delete_ShouldStillRefuseTheLastEffectiveAdministrator_WhenAnotherAccountIsNotEffective(
        ExcludedHolder excluded)
    {
        var admin = await BootstrapAdministratorAsync();
        Account other;
        if (excluded == ExcludedHolder.HistoricalMissingProfile)
        {
            // Split AccountRegistrar at 22aefd8db left Identity after a failed profile save; the current pin is
            // AccountRegistrationAtomicityTests.OpenAsync_ShouldLeaveNoIdentityOrProfile_WhenAuditSaveFails.
            var email = Address("orphan");
            var id = await AdminAccountsKit.CreateWithoutProfileAsync(_factory, email, Ct);
            other = new Account(_factory.CreateClient(), id, email, string.Empty);
        }
        else
            other = await AccountAsync("other");

        if (excluded != ExcludedHolder.OrdinaryAccount)
        {
            await AccountEmailChangeKit.GrantAdminAsTheRetiredSeederDidAsync(_factory, other.UserId);
            await using var roleRead = _factory.Services.CreateAsyncScope();
            var users = roleRead.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.IsInRoleAsync((await users.FindByIdAsync(other.UserId.ToString())).ShouldNotBeNull(), Roles.Admin))
                .ShouldBeTrue();
        }

        if (excluded == ExcludedHolder.Suspended)
        {
            var grant = await GrantAsync(admin);
            (await ReauthTestHelpers.PostAsSessionAsync(admin.Client, admin.SessionId, SuspendPath(other.UserId),
                new { reauthGrant = grant }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await PrimaryAsync(other.UserId)).Access.IsSuspended.ShouldBeTrue();
            await ReauthTestHelpers.LetTheCooldownLapseAsync(_factory, admin.Email, Ct);
        }
        else if (excluded == ExcludedHolder.PendingDeletion)
        {
            var grant = await GrantAsync(other);
            (await ReauthTestHelpers.PostAsSessionAsync(other.Client, other.SessionId, DeletePath,
                new { reauthGrant = grant }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await PrimaryAsync(other.UserId)).Access.DeletedAt.ShouldNotBeNull();
        }

        var excludedAccess = (await PrimaryAsync(other.UserId)).Access;
        excludedAccess.IsEffectiveAdmin.ShouldBeFalse();
        if (excluded == ExcludedHolder.HistoricalMissingProfile)
            excludedAccess.HasProfile.ShouldBeFalse();
        if (excluded == ExcludedHolder.OrdinaryAccount)
            excludedAccess.IsAdmin.ShouldBeFalse();
        (await EffectiveAdministratorsAsync()).ShouldBe([admin.UserId]);
        var ownGrant = await GrantAsync(admin);
        var before = await PrimaryAsync(admin.UserId);

        var response = await ReauthTestHelpers.PostAsSessionAsync(admin.Client, admin.SessionId, DeletePath,
            new { reauthGrant = ownGrant }, Ct);

        await ShouldBeLastAdministratorAsync(response);
        (await PrimaryAsync(admin.UserId)).ShouldBe(before);
        (await MeAsync(admin)).ShouldBe(HttpStatusCode.OK);
        (await EffectiveAdministratorsAsync()).ShouldBe([admin.UserId]);
    }

    [Fact]
    public async Task ReadGuard_ShouldDenyRemovingTheSoleEffectiveAdministrator_WhenAnOperatorBreaksAnotherHoldersInbox()
    {
        var admin = await BootstrapAdministratorAsync();
        var other = await AccountAsync("operator-damaged");
        await AccountEmailChangeKit.GrantAdminAsTheRetiredSeederDidAsync(_factory, other.UserId);
        // Unreachable through current writers: an operator erases another role holder's Identity inbox.
        await using (var damage = _factory.Services.CreateAsyncScope())
        {
            await using var access = await damage.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>()
                .BeginAsync([other.UserId], true, Ct);
            (await damage.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Users
                .Where(user => user.Id == other.UserId)
                .ExecuteUpdateAsync(update => update.SetProperty(user => user.Email, (string?)null), Ct)).ShouldBe(1);
            await access.CommitAsync(Ct);
        }

        await using var read = _factory.Services.CreateAsyncScope();
        await using var guard = await read.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>()
            .BeginAsync([admin.UserId], true, Ct);
        (await read.ServiceProvider.GetRequiredService<IAccountAccessReader>().ReadAsync(other.UserId, Ct))
            .ShouldNotBeNull().IsEffectiveAdmin.ShouldBeFalse();
        (await read.ServiceProvider.GetRequiredService<IAccountAccessWriter>().CanRemoveAccessAsync(admin.UserId, Ct))
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SuspendAndOwnDelete_ShouldSerializeRealLifecycleWrites_AndKeepTheEffectiveAdministrator(
        bool suspendFirst)
    {
        var admin = await BootstrapAdministratorAsync();
        var owner = await AccountAsync("race-owner");
        await AddProviderLoginAsync(owner.UserId);
        using var gate = new AccountLifecycleRaceGate(owner.UserId,
            suspendFirst ? SuspendPath(owner.UserId) : DeletePath,
            suspendFirst ? DeletePath : SuspendPath(owner.UserId));
        using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(gate);
            services.RemoveAll<IAccountAccessCoordinator>();
            services.AddScoped<IAccountAccessCoordinator, LifecycleRaceCoordinator>();
        }));
        using var client = host.CreateClient();
        var adminGrant = await ReauthTestHelpers.MintGrantAsync(_factory, client, admin.SessionId, admin.Email, Ct);
        var ownerGrant = await ReauthTestHelpers.MintGrantAsync(_factory, client, owner.SessionId, owner.Email, Ct);
        var adminBefore = await PrimaryAsync(admin.UserId);

        Task<HttpResponseMessage> SuspendAsync() => ReauthTestHelpers.PostAsSessionAsync(client, admin.SessionId,
            SuspendPath(owner.UserId), new { reauthGrant = adminGrant }, Ct);
        Task<HttpResponseMessage> DeleteAsync() => ReauthTestHelpers.PostAsSessionAsync(client, owner.SessionId,
            DeletePath, new { reauthGrant = ownerGrant }, Ct);

        var first = suspendFirst ? SuspendAsync() : DeleteAsync();
        var held = await gate.FirstHeld.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        held.ShouldBe(new AccountLifecycleRaceGate.HeldTransaction(true, true, true, true));
        var second = suspendFirst ? DeleteAsync() : SuspendAsync();
        await gate.SecondAttempted.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        await WaitForAdvisoryLockWaiterAsync();
        second.IsCompleted.ShouldBeFalse();
        gate.Release();
        var completed = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var suspended = completed[suspendFirst ? 0 : 1];
        var deleted = completed[suspendFirst ? 1 : 0];

        suspended.StatusCode.ShouldBe(HttpStatusCode.OK, await suspended.Content.ReadAsStringAsync(Ct));
        var receipt = await suspended.Content.ReadFromJsonAsync<JsonElement>(Ct);
        receipt.GetProperty("isSuspended").GetBoolean().ShouldBeTrue();
        receipt.GetProperty("pendingDeletion").GetBoolean().ShouldBe(!suspendFirst);
        deleted.StatusCode.ShouldBe(suspendFirst ? HttpStatusCode.Unauthorized : HttpStatusCode.NoContent,
            await deleted.Content.ReadAsStringAsync(Ct));
        var ownerAfter = await PrimaryAsync(owner.UserId);
        ownerAfter.Access.IsSuspended.ShouldBeTrue();
        ownerAfter.Access.AccessRevision.ShouldBe(1);
        ownerAfter.Access.DeletedAt.HasValue.ShouldBe(!suspendFirst);
        ownerAfter.DeletionAudits.ShouldBe(suspendFirst ? 0 : 1);
        ownerAfter.ProviderLogins.ShouldBe(suspendFirst ? 1 : 0);
        ownerAfter.SuspensionAudits.ShouldBe(1);
        (await PrimaryAsync(admin.UserId)).ShouldBe(adminBefore with { Epoch = ownerAfter.Epoch });
        (await EffectiveAdministratorsAsync()).ShouldBe([admin.UserId]);
        (await MeAsync(admin)).ShouldBe(HttpStatusCode.OK);
        (await MeAsync(owner)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task WaitForAdvisoryLockWaiterAsync()
    {
        await using var read = _factory.Services.CreateAsyncScope();
        var app = read.ServiceProvider.GetRequiredService<AppDbContext>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (await app.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::int AS "Value" FROM pg_locks
            WHERE locktype = 'advisory' AND NOT granted
                AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
            """).SingleAsync(timeout.Token) == 0)
            await Task.Delay(20, timeout.Token);
    }

    private async Task<Account> BootstrapAdministratorAsync()
    {
        var admin = await AccountAsync("bootstrap");
        var seeder = new IdempotentAdminRoleSeeder(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AdminBootstrapOptions { InitialAdminEmail = admin.Email }),
            _factory.Services.GetRequiredService<IHostEnvironment>(), NullLogger<IdempotentAdminRoleSeeder>.Instance);
        await seeder.StartAsync(Ct);
        (await EffectiveAdministratorsAsync()).ShouldBe([admin.UserId]);
        return admin;
    }

    private async Task<Account> AccountAsync(string label)
    {
        var email = Address(label);
        var session = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);
        return new Account(_factory.CreateClient(), await AdminAccountsKit.UserIdAsync(_factory, email, Ct), email, session);
    }

    private static string Address(string label) => $"lifecycle-{label}-{Guid.NewGuid():N}@example.se";
    private sealed record Account(HttpClient Client, Guid UserId, string Email, string SessionId);
    private Task<string> GrantAsync(Account account) =>
        ReauthTestHelpers.MintGrantAsync(_factory, account.Client, account.SessionId, account.Email, Ct);

    private static async Task<HttpStatusCode> MeAsync(Account account)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.SessionId);
        return (await account.Client.SendAsync(request, Ct)).StatusCode;
    }

    private static async Task ShouldBeLastAdministratorAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync(Ct));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("title").GetString().ShouldBe(AccountAccessErrors.LastAdministrator);
    }

    private async Task AddProviderLoginAsync(Guid userId)
    {
        await using var setup = _factory.Services.CreateAsyncScope();
        await using var access = await setup.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>()
            .BeginAsync([userId], false, Ct);
        var subject = ExternalSubject.TryCreate(Guid.NewGuid().ToString("N")).ShouldNotBeNull();
        (await setup.ServiceProvider.GetRequiredService<IExternalLoginWriter>()
            .LinkAsync(userId, ExternalProviderKey.Google, subject, Ct)).ShouldBe(ExternalLinkResult.Linked);
        await access.CommitAsync(Ct);
    }

    private async Task<List<Guid>> EffectiveAdministratorsAsync()
    {
        await using var read = _factory.Services.CreateAsyncScope();
        var users = await read.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .GetUsersInRoleAsync(Roles.Admin);
        var reader = read.ServiceProvider.GetRequiredService<IAccountAccessReader>();
        var effective = new List<Guid>();
        foreach (var user in users)
            if ((await reader.ReadAsync(user.Id, Ct))?.IsEffectiveAdmin == true)
                effective.Add(user.Id);
        return effective;
    }

    private async Task<PrimaryState> PrimaryAsync(Guid userId)
    {
        await using var read = _factory.Services.CreateAsyncScope();
        var identity = read.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        var user = await identity.Users.AsNoTracking().SingleAsync(row => row.Id == userId, Ct);
        var reader = read.ServiceProvider.GetRequiredService<IAccountAccessReader>();
        var account = (await reader.ReadAsync(userId, Ct)).ShouldNotBeNull();
        var app = read.ServiceProvider.GetRequiredService<AppDbContext>();
        return new PrimaryState(account, await reader.ReadEpochAsync(Ct), user.SecurityStamp, user.ConcurrencyStamp,
            await identity.UserLogins.CountAsync(row => row.UserId == userId, Ct),
            await app.AuditLogEntries.CountAsync(row => row.UserId == userId && row.EventType == "Account.Deleted", Ct),
            await app.AuditLogEntries.CountAsync(row => row.AggregateId == userId && row.EventType == "Admin.AccountSuspended", Ct));
    }

    private sealed record PrimaryState(AccountAccessSnapshot Access, long Epoch, string? SecurityStamp,
        string? ConcurrencyStamp, int ProviderLogins, int DeletionAudits, int SuspensionAudits);
}
