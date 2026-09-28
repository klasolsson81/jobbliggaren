using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
            scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>(),
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

    // ── #1745: GitHub's rows beside Google's ──
    // GitHub's identifier is the production adapter's (GitHubIdentities over a documented /user): the numeric id.

    private static async Task<ExternalSubject> GitHubSubjectAsync() =>
        (await GitHubIdentities.ReadAsync(
            GitHubApiShapes.User(Random.Shared.NextInt64(1_000_000, 1L << 53), "store-gh"),
            GitHubApiShapes.Emails.PrimaryVerified("store@example.se"))).Subject;

    [Fact]
    public async Task A_github_login_is_stored_under_githubs_key_and_found_by_it()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userId = await OpenAccountAsync(scope);
        var subject = await GitHubSubjectAsync();

        (await Store(scope).LinkAsync(userId, ExternalProviderKey.GitHub, subject, Ct)).ShouldBe(ExternalLinkResult.Linked);

        (await Store(scope).FindUserIdAsync(ExternalProviderKey.GitHub, subject, Ct)).ShouldBe(userId);
        var row = await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().UserLogins
            .AsNoTracking()
            .SingleAsync(l => l.UserId == userId, Ct);
        row.LoginProvider.ShouldBe("github");
        row.ProviderKey.ShouldBe(subject.Reveal());
        row.ProviderDisplayName.ShouldBeNull();
    }

    [Fact]
    public async Task A_github_login_is_not_found_under_another_providers_key_with_the_same_identifier()
    {
        // DECLARED: no Google sub is known to equal a GitHub id, though both are decimal strings. The row asserts only
        // that the lookup keys on the provider as well as the identifier, so such a collision could never cross over.
        await using var scope = factory.Services.CreateAsyncScope();
        var userId = await OpenAccountAsync(scope);
        var subject = await GitHubSubjectAsync();
        await Store(scope).LinkAsync(userId, ExternalProviderKey.GitHub, subject, Ct);

        (await Store(scope).FindUserIdAsync(ExternalProviderKey.Google, subject, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task One_account_holds_a_google_login_and_a_github_login_side_by_side()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userId = await OpenAccountAsync(scope);
        var google = NewSubject();
        var github = await GitHubSubjectAsync();

        (await Store(scope).LinkAsync(userId, ExternalProviderKey.Google, google, Ct)).ShouldBe(ExternalLinkResult.Linked);
        (await Store(scope).LinkAsync(userId, ExternalProviderKey.GitHub, github, Ct)).ShouldBe(ExternalLinkResult.Linked);

        (await Store(scope).FindUserIdAsync(ExternalProviderKey.Google, google, Ct)).ShouldBe(userId);
        (await Store(scope).FindUserIdAsync(ExternalProviderKey.GitHub, github, Ct)).ShouldBe(userId);
    }

    // ── #1746: LinkedIn's rows beside the others ──
    // LinkedIn's identifier is the production adapter's (LinkedInIdentities over a documented userinfo): a pairwise sub.

    private static async Task<ExternalSubject> LinkedInSubjectAsync() =>
        (await LinkedInIdentities.ReadAsync(
            LinkedInUserInfoShapes.Member(LinkedInUserInfoShapes.NewSub(), "store@example.se"))).Subject;

    [Fact]
    public async Task A_linkedin_login_is_stored_under_linkedins_key_and_found_by_it()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userId = await OpenAccountAsync(scope);
        var subject = await LinkedInSubjectAsync();

        (await Store(scope).LinkAsync(userId, ExternalProviderKey.LinkedIn, subject, Ct)).ShouldBe(ExternalLinkResult.Linked);

        (await Store(scope).FindUserIdAsync(ExternalProviderKey.LinkedIn, subject, Ct)).ShouldBe(userId);
        var row = await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().UserLogins
            .AsNoTracking()
            .SingleAsync(l => l.UserId == userId, Ct);
        row.LoginProvider.ShouldBe("linkedin");
        row.ProviderKey.ShouldBe(subject.Reveal());
        row.ProviderDisplayName.ShouldBeNull();
    }

    [Theory]
    [InlineData("google")]
    [InlineData("github")]
    public async Task A_linkedin_login_is_not_found_under_another_providers_key_with_the_same_identifier(string other)
    {
        // DECLARED: no other provider's identifier is known to equal a LinkedIn sub. The row asserts only that the
        // lookup keys on the provider as well as the identifier, so such a collision could never cross over.
        await using var scope = factory.Services.CreateAsyncScope();
        var userId = await OpenAccountAsync(scope);
        var subject = await LinkedInSubjectAsync();
        await Store(scope).LinkAsync(userId, ExternalProviderKey.LinkedIn, subject, Ct);
        ExternalProviderKey.TryParse(other, out var key).ShouldBeTrue();

        (await Store(scope).FindUserIdAsync(key, subject, Ct)).ShouldBeNull();
    }

    // ── A link another request makes past this store's read (code-reviewer Major 2, test-writer Minor 4) ──
    // The actor: a second callback for the same identifier, in another request, linking it through the same store.

    public enum RaceWindow
    {
        BeforeIdentitysOwnCheck,
        BeforeTheSave,
    }

    [Theory]
    [InlineData(RaceWindow.BeforeIdentitysOwnCheck, false, ExternalLinkResult.LinkedToAnotherUser)]
    [InlineData(RaceWindow.BeforeIdentitysOwnCheck, true, ExternalLinkResult.AlreadyLinkedToThisUser)]
    [InlineData(RaceWindow.BeforeTheSave, false, ExternalLinkResult.LinkedToAnotherUser)]
    [InlineData(RaceWindow.BeforeTheSave, true, ExternalLinkResult.AlreadyLinkedToThisUser)]
    public async Task A_link_another_request_makes_past_the_read_is_answered_by_who_holds_it(
        RaceWindow window, bool sameAccount, ExternalLinkResult expected)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userId = await OpenAccountAsync(scope);
        var winner = sameAccount ? userId : await OpenAccountAsync(scope);
        var subject = NewSubject();
        async Task TheOtherRequestLinksItAsync()
        {
            await using var other = factory.Services.CreateAsyncScope();
            (await Store(other).LinkAsync(winner, ExternalProviderKey.Google, subject, Ct)).ShouldBe(ExternalLinkResult.Linked);
        }

        var identity = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        var store = new IdentityExternalLoginStore(
            ActivatorUtilities.CreateInstance<RacingUserManager>(
                scope.ServiceProvider, new Race(window, TheOtherRequestLinksItAsync)),
            identity,
            scope.ServiceProvider.GetRequiredService<IDbExceptionInspector>());

        (await store.LinkAsync(userId, ExternalProviderKey.Google, subject, Ct)).ShouldBe(expected);

        await identity.SaveChangesAsync(Ct);
        (await Store(scope).FindUserIdAsync(ExternalProviderKey.Google, subject, Ct)).ShouldBe(winner);
        (await identity.UserLogins.AsNoTracking().CountAsync(l => l.ProviderKey == subject.Reveal(), Ct)).ShouldBe(1);
    }

    public sealed record Race(RaceWindow Window, Func<Task> TheOtherRequest);

    // Identity's own UserManager, letting the other request in at one point of AddLoginAsync.
    private sealed class RacingUserManager(
        Race race,
        IUserStore<ApplicationUser> store,
        IOptions<IdentityOptions> optionsAccessor,
        IPasswordHasher<ApplicationUser> hasher,
        IEnumerable<IUserValidator<ApplicationUser>> userValidators,
        IEnumerable<IPasswordValidator<ApplicationUser>> validators,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<UserManager<ApplicationUser>> logger)
        : UserManager<ApplicationUser>(
            store, optionsAccessor, hasher, userValidators, validators, keyNormalizer, errors, services, logger)
    {
        public override async Task<IdentityResult> AddLoginAsync(ApplicationUser user, UserLoginInfo login)
        {
            if (race.Window == RaceWindow.BeforeIdentitysOwnCheck)
                await race.TheOtherRequest();
            return await base.AddLoginAsync(user, login);
        }

        protected override async Task<IdentityResult> UpdateUserAsync(ApplicationUser user)
        {
            if (race.Window == RaceWindow.BeforeTheSave)
                await race.TheOtherRequest();
            return await base.UpdateUserAsync(user);
        }
    }
}
