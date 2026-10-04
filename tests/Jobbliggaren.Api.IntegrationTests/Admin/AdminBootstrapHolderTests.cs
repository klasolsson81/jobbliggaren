using System.Net;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #2001 — the admin bootstrap grants the role only while the role has no holder. security-auditor's probe of
/// 2026-10-04, inverted: an admin who leaves the configured address does not hand the role to the next account
/// that proves that address's inbox.
/// </summary>
[Collection("Api")]
public sealed class AdminBootstrapHolderTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_bootstrap_never_grants_the_configured_address_s_next_holder_while_the_role_has_one()
    {
        var token = NewToken();
        var bootstrap = Address(token, "bootstrap");
        var parked = await ParkEveryHolderAsync();
        Guid? first = null;
        Guid? second = null;
        try
        {
            // 1. The role has no holder, as on a fresh install: the bootstrap's own premise.
            var firstSession = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, bootstrap, ct: Ct);
            first = await UserIdAsync(factory, bootstrap, Ct);
            await RunTheSeederAsync(bootstrap, new CapturingLoggerProvider());
            (await IsAdminAsync(first.Value)).ShouldBeTrue();

            // 2. That admin moves to another address through the self-service change-email flow.
            (await ReauthTestHelpers.MoveTheAddressAsync(
                factory, factory.CreateClient(), firstSession, bootstrap, Address(token, "moved"), Ct))
                .StatusCode.ShouldBe(HttpStatusCode.OK);

            // 3. A second account moves onto the freed address through the same flow, proving its inbox.
            var secondAddress = Address(token, "second");
            var secondSession = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, secondAddress, ct: Ct);
            second = await UserIdAsync(factory, secondAddress, Ct);
            (await ReauthTestHelpers.MoveTheAddressAsync(
                factory, factory.CreateClient(), secondSession, secondAddress, bootstrap, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.OK);

            // 4. The next start grants the second account nothing, and names it by its id alone.
            var logs = new CapturingLoggerProvider();
            await RunTheSeederAsync(bootstrap, logs);

            (await IsAdminAsync(second.Value)).ShouldBeFalse();
            (await IsAdminAsync(first.Value)).ShouldBeTrue();
            var warning = logs.Logs.Single(log => log.EventId.Id == 6);
            warning.Level.ShouldBe(LogLevel.Warning);
            warning.Message.ShouldContain(second.Value.ToString());
            logs.Logs.Where(log => log.AllText.Contains(bootstrap, StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty();
        }
        finally
        {
            foreach (var account in new[] { first, second })
            {
                if (account is { } id && await IsAdminAsync(id))
                    await DemoteAsync(factory, id);
            }
            foreach (var holder in parked)
                await PromoteAsync(factory, holder);
        }
    }

    /// <summary>Takes the role from every account that holds it; the caller gives each one back.</summary>
    private async Task<IReadOnlyList<Guid>> ParkEveryHolderAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var holders = await users.GetUsersInRoleAsync(Roles.Admin);
        foreach (var holder in holders)
            (await users.RemoveFromRoleAsync(holder, Roles.Admin)).Succeeded.ShouldBeTrue();
        return holders.Select(holder => holder.Id).ToList();
    }

    private async Task RunTheSeederAsync(string configured, CapturingLoggerProvider logs)
    {
        using var loggers = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var seeder = new IdempotentAdminRoleSeeder(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AdminBootstrapOptions { InitialAdminEmail = configured }),
            factory.Services.GetRequiredService<IHostEnvironment>(),
            loggers.CreateLogger<IdempotentAdminRoleSeeder>());
        await seeder.StartAsync(Ct);
    }

    private async Task<bool> IsAdminAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(userId.ToString())).ShouldNotBeNull();
        return await users.IsInRoleAsync(user, Roles.Admin);
    }
}
