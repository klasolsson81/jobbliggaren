using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1744 (ADR 0142 D8) — external logins in Identity's own <c>AspNetUserLogins</c>, against the real Identity
/// database: a link is found by the provider's identifier, linking again is idempotent, a login another account
/// holds is reported as such and never moved, and no display name is stored. Accounts are opened by the production
/// creator, as <c>complete</c> opens them.
/// </summary>
[Collection("Api")]
public class IdentityExternalLoginStoreTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ExternalSubject NewSubject() =>
        ExternalSubject.TryCreate(Guid.NewGuid().ToString("N"))!.Value;

    private static async Task<Guid> OpenAccountAsync(AsyncServiceScope scope)
    {
        var created = await scope.ServiceProvider.GetRequiredService<IPasswordlessAccountCreator>()
            .CreatePasswordlessUserAsync($"extern-{Guid.NewGuid():N}@example.se", Ct);
        return created.Value;
    }

    private static IdentityExternalLoginStore Store(AsyncServiceScope scope) =>
        new(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            scope.ServiceProvider.GetRequiredService<IDbExceptionInspector>());

    [Fact]
    public async Task A_linked_login_is_found_by_the_providers_identifier()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userId = await OpenAccountAsync(scope);
        var subject = NewSubject();

        (await Store(scope).LinkAsync(userId, ExternalProviderKey.Google, subject, Ct)).ShouldBe(ExternalLinkResult.Linked);
        (await Store(scope).FindUserIdAsync(ExternalProviderKey.Google, subject, Ct)).ShouldBe(userId);
    }

    [Fact]
    public async Task An_identifier_nobody_linked_is_found_on_no_account()
    {
        await using var scope = factory.Services.CreateAsyncScope();

        (await Store(scope).FindUserIdAsync(ExternalProviderKey.Google, NewSubject(), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Linking_the_same_login_to_the_same_account_again_is_idempotent()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userId = await OpenAccountAsync(scope);
        var subject = NewSubject();
        await Store(scope).LinkAsync(userId, ExternalProviderKey.Google, subject, Ct);

        (await Store(scope).LinkAsync(userId, ExternalProviderKey.Google, subject, Ct))
            .ShouldBe(ExternalLinkResult.AlreadyLinkedToThisUser);
    }

    [Fact]
    public async Task A_login_another_account_holds_is_reported_and_never_moved()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var holder = await OpenAccountAsync(scope);
        var other = await OpenAccountAsync(scope);
        var subject = NewSubject();
        await Store(scope).LinkAsync(holder, ExternalProviderKey.Google, subject, Ct);

        (await Store(scope).LinkAsync(other, ExternalProviderKey.Google, subject, Ct))
            .ShouldBe(ExternalLinkResult.LinkedToAnotherUser);
        (await Store(scope).FindUserIdAsync(ExternalProviderKey.Google, subject, Ct)).ShouldBe(holder);
    }

    [Fact]
    public async Task The_stored_row_names_the_provider_and_holds_no_display_name()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userId = await OpenAccountAsync(scope);
        var subject = NewSubject();
        await Store(scope).LinkAsync(userId, ExternalProviderKey.Google, subject, Ct);

        var row = await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().UserLogins
            .AsNoTracking()
            .SingleAsync(l => l.UserId == userId, Ct);

        row.LoginProvider.ShouldBe("google");
        row.ProviderKey.ShouldBe(subject.Reveal());
        row.ProviderDisplayName.ShouldBeNull();
    }
}
