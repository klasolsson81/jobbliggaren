using System.Data;
using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Admin;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

[Collection("Api")]
public sealed class ProtectedAccountTransactionLossTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProtectedIdentityWrite_ShouldNeverReopenOrOverwriteNewState_WhenItsPhysicalTransactionIsLost(bool terminateBackend)
    {
        var originalAddress = $"lost-account-lock-{Guid.NewGuid():N}@example.se";
        var target = await AdminAccountsKit.OpenActiveAsync(factory, originalAddress, Ct);
        var administrator = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        await using var staleScope = factory.Services.CreateAsyncScope();
        var services = staleScope.ServiceProvider;
        var app = services.GetRequiredService<AppDbContext>();
        var identity = services.GetRequiredService<AppIdentityDbContext>();
        var coordinator = services.GetRequiredService<IAccountAccessCoordinator>();
        var reader = services.GetRequiredService<IAccountAccessReader>();
        var epoch = await reader.ReadEpochAsync(Ct);
        await using var held = await coordinator.BeginAsync([target], false, Ct);
        var admitted = (await reader.ReadAsync(target, Ct)).ShouldNotBeNull();
        admitted.CanAuthenticate.ShouldBeTrue();
        var originalProof = new AccountAccessProof(epoch, target, admitted.AccessRevision);
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var staleUser = (await users.FindByIdAsync(target.ToString())).ShouldNotBeNull();
        var connection = app.Database.GetDbConnection().ShouldBeOfType<NpgsqlConnection>();
        ReferenceEquals(connection, identity.Database.GetDbConnection()).ShouldBeTrue();
        ReferenceEquals(app.Database.CurrentTransaction.ShouldNotBeNull().GetDbTransaction(),
            identity.Database.CurrentTransaction.ShouldNotBeNull().GetDbTransaction()).ShouldBeTrue();
        connection.State.ShouldBe(ConnectionState.Open);
        var admittedBackend = connection.ProcessID;

        if (terminateBackend)
        {
            await using var control = new NpgsqlConnection(app.Database.GetConnectionString());
            await control.OpenAsync(Ct);
            control.ProcessID.ShouldNotBe(admittedBackend);
            await using var kill = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", control);
            kill.Parameters.AddWithValue("pid", admittedBackend);
            (await kill.ExecuteScalarAsync(Ct)).ShouldBe(true);
        }
        else
        {
            await connection.CloseAsync();
            connection.State.ShouldBe(ConnectionState.Closed);
        }

        // The released physical lock admits a real lifecycle write in another request before this stale row resumes.
        var grant = await ReauthTestHelpers.MintGrantAsync(factory, administrator.Client,
            administrator.SessionId, administrator.Email, Ct);
        var suspended = await administrator.Client.PostAsJsonAsync($"/api/v1/admin/accounts/{target}/suspend",
            new { reauthGrant = grant }, Ct).WaitAsync(TimeSpan.FromSeconds(30), Ct);
        suspended.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The full-row Identity writer is the same UserManager entry point the production address writer uses.
        staleUser.UserName = $"stale-write-{Guid.NewGuid():N}@example.se";
        staleUser.Email = staleUser.UserName;
        var writeFailure = await Record.ExceptionAsync(async () =>
        {
            (await users.UpdateAsync(staleUser)).Succeeded.ShouldBeTrue();
        });
        writeFailure.ShouldNotBeNull();
        (writeFailure is InvalidOperationException or NpgsqlException or DbUpdateException).ShouldBeTrue();
        await Should.ThrowAsync<InvalidOperationException>(() => held.CommitAsync(Ct));
        await Should.ThrowAsync<InvalidOperationException>(() => services.GetRequiredService<ISessionStore>()
            .CreateAsync(target, originalProof, SessionLifetime.Persistent, Ct));

        await using var fresh = factory.Services.CreateAsyncScope();
        var persisted = await fresh.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Users.AsNoTracking()
            .Where(user => user.Id == target)
            .Select(user => new { user.Email, user.UserName, user.IsSuspended, user.AccessRevision })
            .SingleAsync(Ct);
        persisted.Email.ShouldBe(originalAddress);
        persisted.UserName.ShouldBe(originalAddress);
        persisted.IsSuspended.ShouldBeTrue();
        persisted.AccessRevision.ShouldBe(admitted.AccessRevision + 1);
        (await fresh.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries
            .CountAsync(row => row.AggregateId == target && row.EventType == "Admin.AccountSuspended", Ct)).ShouldBe(1);
        (await fresh.ServiceProvider.GetRequiredService<ISessionStore>().CreateAsync(
            target, originalProof, SessionLifetime.Persistent, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task ProtectedIdentityWrite_ShouldCommitOnItsAdmittedPhysicalTransaction_WhenBackendRemainsAlive()
    {
        var email = $"live-account-lock-{Guid.NewGuid():N}@example.se";
        var target = await AdminAccountsKit.OpenActiveAsync(factory, email, Ct);
        await using (var write = factory.Services.CreateAsyncScope())
        {
            var coordinator = write.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>();
            await using var held = await coordinator.BeginAsync([target], false, Ct);
            var userManager = write.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await userManager.FindByIdAsync(target.ToString())).ShouldNotBeNull();
            (await userManager.UpdateAsync(user)).Succeeded.ShouldBeTrue();
            await held.CommitAsync(Ct);
        }

        await using var read = factory.Services.CreateAsyncScope();
        var state = (await read.ServiceProvider.GetRequiredService<IAccountAccessReader>().ReadAsync(target, Ct))
            .ShouldNotBeNull();
        state.CanAuthenticate.ShouldBeTrue();
        state.Email.ShouldBe(email);
        state.AccessRevision.ShouldBe(0);
    }
}
