using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Auth.AccountEmailChanges;
using Microsoft.AspNetCore.DataProtection;
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
    /// Admin for an account that is not the bootstrap holder. The actor is
    /// <see cref="Jobbliggaren.Infrastructure.Identity.IdempotentAdminRoleSeeder"/> before #2006, which is retired, and
    /// <see cref="AdminBootstrapHolderTests.The_bootstrap_never_grants_the_configured_address_s_next_holder_while_the_role_has_one"/> pins that today's seeder does not produce it (AGENTS.md §5 <c>Tests:</c>).
    /// </summary>
    public static Task GrantAdminAsTheRetiredSeederDidAsync(ApiFactory factory, Guid userId) =>
        AdminAccountsKit.PromoteAsync(factory, userId);

    /// <summary>
    /// The clock passes over an actual HTTP request: preserve its mailed code, nonce and original access proof,
    /// shift only the committed witness and the record's instants, and leave both keys their remaining lifetime.
    /// </summary>
    public sealed record ElapsedChange(LoginCode Code, DateTimeOffset CompletableFrom, DateTimeOffset ExpiresAt, Guid RequestId);

    public static async Task<ElapsedChange> ChangeStartedHoursAgoAsync(
        ApiFactory factory, Guid accountId, string newEmail, string currentEmail, CancellationToken ct, int hoursAgo = 73)
    {
        var administrator = await AdminAsync(factory, AdminAccountsKit.NewToken(), ct);
        var requested = await RequestAsync(factory, administrator, accountId, newEmail, ct);
        requested.StatusCode.ShouldBe(HttpStatusCode.Accepted, await requested.Content.ReadAsStringAsync(ct));
        return await AgeStartedChangeAsync(factory, accountId, newEmail, currentEmail, ct, hoursAgo);
    }

    public static async Task<ElapsedChange> AgeStartedChangeAsync(
        ApiFactory factory, Guid accountId, string newEmail, string currentEmail, CancellationToken ct, int hoursAgo = 73)
    {
        var now = factory.Services.GetRequiredService<IDateTimeProvider>().UtcNow;
        var mailed = factory.Emails.LoginChallenges.Last(mail => mail.ToEmail == newEmail).Content
            .ShouldBeOfType<LoginChallengeEmail.AccountEmailChangeCode>();
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        var db = redis.GetDatabase();
        var recordKey = RedisAccountEmailChangeStore.RecordKey(RedisAccountEmailChangeStore.RecordSegment(newEmail));
        var protectedPayload = await db.HashGetAsync(recordKey, "p");
        protectedPayload.IsNull.ShouldBeFalse();
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(RedisAccountEmailChangeStore.ProtectorPurpose);
        var original = JsonSerializer.Deserialize<RedisAccountEmailChangeStore.ChangePayload>(
            protector.Unprotect((byte[])protectedPayload!)).ShouldNotBeNull();
        original.Code.ShouldBe(mailed.Code.Reveal());
        original.UserId.ShouldBe(accountId);
        original.CurrentFingerprint.ShouldBe(Jobbliggaren.Infrastructure.Auth.SubjectFingerprint.Hex(currentEmail));
        var requestId = original.RequestId.ShouldNotBeNull();
        var delta = TimeSpan.FromHours(hoursAgo);
        var milliseconds = checked((long)delta.TotalMilliseconds);
        var elapsed = original with { NotBefore = original.NotBefore - milliseconds, ExpiresAt = original.ExpiresAt - milliseconds };
        (elapsed with { NotBefore = original.NotBefore, ExpiresAt = original.ExpiresAt }).ShouldBe(original);
        await db.HashSetAsync(recordKey,
        [
            new HashEntry("p", protector.Protect(JsonSerializer.SerializeToUtf8Bytes(elapsed))),
            new HashEntry("n", elapsed.NotBefore),
            new HashEntry("x", elapsed.ExpiresAt),
        ]);
        var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(elapsed.ExpiresAt);
        var issuedAt = expiresAt - AccountEmailChangePolicy.Ttl;
        var completableFrom = DateTimeOffset.FromUnixTimeMilliseconds(elapsed.NotBefore);
        completableFrom.ShouldBe(issuedAt + AccountEmailChangePolicy.Delay);
        var left = expiresAt - now;
        foreach (var key in new[] { recordKey, RedisAccountEmailChangeStore.IndexKey(accountId) })
            (await db.KeyExpireAsync(key, TimeSpan.FromSeconds(Math.Ceiling(left.TotalSeconds)))).ShouldBeTrue();

        await using var scope = factory.Services.CreateAsyncScope();
        var app = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var witness = (await app.AuditLogEntries.AsNoTracking().Where(row => row.AggregateId == accountId
            && row.EventType == RequestAccountEmailChangeCommand.RequestedEventType).OrderByDescending(row => row.OccurredAt)
            .ToListAsync(ct)).First(row => JsonDocument.Parse(row.Payload!).RootElement.GetProperty("requestId").GetGuid() == requestId);
        (await app.AuditLogEntries.Where(row => row.Id == witness.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.OccurredAt, witness.OccurredAt - delta), ct)).ShouldBe(1);
        await using var access = await scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>()
            .BeginAsync([accountId], false, ct);
        (await scope.ServiceProvider.GetRequiredService<IAccountEmailChangeRequests>()
            .HasCommittedRequestAsync(accountId, requestId, issuedAt, expiresAt, ct)).ShouldBeTrue();
        await access.CommitAsync(ct);
        return new ElapsedChange(mailed.Code, completableFrom, expiresAt, requestId);
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
