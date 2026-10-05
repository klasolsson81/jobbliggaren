using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Auth.AccountEmailChanges;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #1975 — the address change's routes and premises, each made by the actor production uses: an administrator with a
/// session and a re-authentication grant minted the way production mints one, and a pending change written by the
/// production adapter.
/// </summary>
internal static class AccountEmailChangeKit
{
    public const string CompletePath = "/api/v1/auth/account-email-change/complete";

    public static string Path(Guid accountId) => $"/api/v1/admin/accounts/{accountId}/email-change";

    internal sealed record Admin(HttpClient Client, Guid UserId, string SessionId, string Email);

    public static async Task<Admin> AdminAsync(ApiFactory factory, string token, CancellationToken ct)
    {
        var (client, userId, sessionId) = await AdminAccountsKit.AdminAsync(factory, token, ct);
        return new Admin(client, userId, sessionId, AdminAccountsKit.Address(token, "admin"));
    }

    /// <summary>Asks for a change with a grant of the administrator's own, minted for this one request.</summary>
    public static async Task<HttpResponseMessage> RequestAsync(
        ApiFactory factory, Admin admin, Guid accountId, string newEmail, CancellationToken ct)
    {
        var grant = await ReauthTestHelpers.MintGrantAsync(factory, admin.Client, admin.SessionId, admin.Email, ct);
        return await admin.Client.PostAsJsonAsync(Path(accountId), new { newEmail, reauthGrant = grant }, ct);
    }

    public static Task<HttpResponseMessage> CompleteAsync(
        HttpClient client, string? currentEmail, string? newEmail, string? code, CancellationToken ct) =>
        client.PostAsJsonAsync(CompletePath, new { currentEmail, newEmail, code }, ct);

    /// <summary>
    /// A pending change whose delay has run. The actor is THE CLOCK (AGENTS.md §5 <c>Tests:</c>): the production adapter,
    /// over the host's own keyring and volatile connection, writes the change as it would have been written 73 hours
    /// ago, and both keys are then left the life the payload states, so the TTL and the payload agree as production
    /// keeps them. The host's own clock is never moved.
    /// </summary>
    public static async Task<AccountEmailChangePut.Written> ChangeStartedHoursAgoAsync(
        ApiFactory factory, Guid accountId, string newEmail, string currentEmail, CancellationToken ct, int hoursAgo = 73)
    {
        var now = factory.Services.GetRequiredService<IDateTimeProvider>().UtcNow;
        var then = new MutableFakeDateTimeProvider { UtcNow = now - TimeSpan.FromHours(hoursAgo) };
        var store = ActivatorUtilities.CreateInstance<RedisAccountEmailChangeStore>(factory.Services, then);

        var written = (await store.PutAsync(new NewAccountEmailChange(accountId, newEmail, currentEmail), ct))
            .ShouldBeOfType<AccountEmailChangePut.Written>();

        var left = written.ExpiresAt - now;
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        var db = redis.GetDatabase();
        var recordKey = RedisAccountEmailChangeStore.RecordKey(RedisAccountEmailChangeStore.RecordSegment(newEmail));
        foreach (var key in new[] { recordKey, RedisAccountEmailChangeStore.IndexKey(accountId) })
            (await db.KeyExpireAsync(key, TimeSpan.FromSeconds(Math.Ceiling(left.TotalSeconds)))).ShouldBeTrue();

        return written;
    }

    /// <summary>What the TTL does at the end of a change's life: the record is gone, by expiry and not by a deletion.</summary>
    public static async Task LetTheChangeExpireAsync(ApiFactory factory, string newEmail, CancellationToken ct)
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        var db = redis.GetDatabase();
        var recordKey = RedisAccountEmailChangeStore.RecordKey(RedisAccountEmailChangeStore.RecordSegment(newEmail));
        (await db.KeyExpireAsync(recordKey, TimeSpan.FromMilliseconds(1))).ShouldBeTrue();
        await Task.Delay(50, ct);
        (await db.KeyExistsAsync(recordKey)).ShouldBeFalse();
    }

    public static async Task<List<AuditLogEntry>> AuditRowsAsync(ApiFactory factory, Guid accountId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.AuditLogEntries.AsNoTracking()
            .Where(row => row.AggregateId == accountId && row.EventType.Contains("EmailChange"))
            .OrderBy(row => row.OccurredAt)
            .ToListAsync(ct);
    }

}
