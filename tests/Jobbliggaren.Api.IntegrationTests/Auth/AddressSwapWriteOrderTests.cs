using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1739 (PR 1) — what the address swap's write order RESTS on, measured against the real
/// <see cref="UserManager{TUser}"/> and Postgres. <c>ConfirmChangeEmailAsync</c> takes the user name before it
/// writes the address, because only the user-name index is unique. That order is pinned as wiring in
/// <c>UserAccountServiceAddressSwapTests</c>; this class pins the framework behaviour each step assumes, and
/// what the swap leaves behind for the one who came second.
/// </summary>
[Collection("Api")]
public class AddressSwapWriteOrderTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static string Address(string label) => $"swap-{label}-{Guid.NewGuid():N}@example.se";

    private Task<HttpResponseMessage> ConfirmAsync(Guid uid, string email, string token, CancellationToken ct) =>
        _client.PostAsJsonAsync("/api/v1/auth/confirm-email-change", new { uid, email, token }, ct);

    private async Task<Guid> CreateAccountAsync(string email, CancellationToken ct)
    {
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: ct);
        return (await ReadAsync(email))!.Id;
    }

    private async Task<ApplicationUser?> ReadAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }

    private async Task<ApplicationUser> ReadAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByIdAsync(userId.ToString());
        return user.ShouldNotBeNull();
    }

    // The token as the mailed link carries it: Identity's change-email token, Base64Url-encoded, the transform
    // of UserAccountService.GenerateChangeEmailTokenAsync.
    private async Task<string> MailedTokenAsync(Guid userId, string newEmail)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(userId.ToString())).ShouldNotBeNull();
        return Base64Url.EncodeToString(
            Encoding.UTF8.GetBytes(await userManager.GenerateChangeEmailTokenAsync(user, newEmail)));
    }

    private async Task<int> RowsOnAddressAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var normalized = scope.ServiceProvider.GetRequiredService<ILookupNormalizer>().NormalizeEmail(email);
        return await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>()
            .Users.AsNoTracking().CountAsync(u => u.NormalizedEmail == normalized);
    }

    [Fact]
    public async Task SetUserNameAsync_rotates_the_security_stamp_so_a_token_minted_before_it_is_dead()
    {
        var ct = TestContext.Current.CancellationToken;
        var newEmail = Address("stamp-new");
        var userId = await CreateAccountAsync(Address("stamp"), ct);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(userId.ToString())).ShouldNotBeNull();
        var stampBefore = user.SecurityStamp;
        var mintedBefore = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        var purpose = UserManager<ApplicationUser>.GetChangeEmailTokenPurpose(newEmail);
        var provider = userManager.Options.Tokens.ChangeEmailTokenProvider;
        (await userManager.VerifyUserTokenAsync(user, provider, purpose, mintedBefore)).ShouldBeTrue();

        (await userManager.SetUserNameAsync(user, newEmail)).Succeeded.ShouldBeTrue();

        // Why the mailed token is verified BEFORE the user-name write, and why the address write gets a token
        // minted after it.
        user.SecurityStamp.ShouldNotBe(stampBefore);
        (await userManager.VerifyUserTokenAsync(user, provider, purpose, mintedBefore)).ShouldBeFalse();
    }

    [Fact]
    public async Task SetUserNameAsync_to_the_name_the_user_already_holds_succeeds()
    {
        // The retry after a swap whose second write failed: the user name is already the new address, and the
        // validator must not count the user's own row as the duplicate.
        var ct = TestContext.Current.CancellationToken;
        var email = Address("self");
        var userId = await CreateAccountAsync(email, ct);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(userId.ToString())).ShouldNotBeNull();

        (await userManager.SetUserNameAsync(user, email)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task The_unique_index_is_on_the_user_name_and_its_refusal_is_recognised()
    {
        // The state the race's loser meets: the validator's read found nothing, and the write hits the index.
        // No path in src/ writes a second row on one user name; the row is added by hand to show that the INDEX
        // refuses it and that the refusal is the exception the swap treats as "the address is taken".
        var ct = TestContext.Current.CancellationToken;
        var email = Address("index");
        await CreateAccountAsync(email, ct);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        var normalizer = scope.ServiceProvider.GetRequiredService<ILookupNormalizer>();
        db.Users.Add(new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = normalizer.NormalizeName(email),
            Email = Address("index-other"),
            SecurityStamp = Guid.NewGuid().ToString(),
        });

        var refused = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));

        scope.ServiceProvider.GetRequiredService<IDbExceptionInspector>()
            .IsUniqueConstraintViolation(refused).ShouldBeTrue();
    }

    [Fact]
    public async Task The_second_swap_to_one_address_is_refused_and_keeps_both_its_columns()
    {
        var ct = TestContext.Current.CancellationToken;
        var firstOld = Address("first");
        var secondOld = Address("second");
        var contested = Address("contested");
        var first = await CreateAccountAsync(firstOld, ct);
        var second = await CreateAccountAsync(secondOld, ct);

        // Both hold a live mailed token for the SAME address before either confirms.
        var firstToken = await MailedTokenAsync(first, contested);
        var secondToken = await MailedTokenAsync(second, contested);

        (await ConfirmAsync(first, contested, firstToken, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ConfirmAsync(second, contested, secondToken, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var loser = await ReadAsync(second);
        loser.Email.ShouldBe(secondOld);
        loser.UserName.ShouldBe(secondOld);
        (await RowsOnAddressAsync(contested)).ShouldBe(1);
    }

    [Fact]
    public async Task A_mailed_token_cannot_be_used_twice()
    {
        var ct = TestContext.Current.CancellationToken;
        var oldEmail = Address("once");
        var newEmail = Address("once-new");
        var userId = await CreateAccountAsync(oldEmail, ct);
        var token = await MailedTokenAsync(userId, newEmail);

        (await ConfirmAsync(userId, newEmail, token, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ConfirmAsync(userId, newEmail, token, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var user = await ReadAsync(userId);
        user.Email.ShouldBe(newEmail);
        user.UserName.ShouldBe(newEmail);
    }

    [Fact]
    public async Task Swaps_racing_to_one_address_leave_one_row_on_it_and_no_row_half_moved()
    {
        // Pairs confirm at the same instant. Whichever way each pair interleaves — the validator's read refusing
        // the second, or the index refusing its write — exactly one wins, the other is answered 400 and not 500,
        // and no row ends with its user name and its address apart.
        var ct = TestContext.Current.CancellationToken;
        const int pairs = 6;

        var races = new List<(Guid First, Guid Second, string Contested, string FirstToken, string SecondToken)>();
        for (var i = 0; i < pairs; i++)
        {
            var contested = Address($"race{i}");
            var first = await CreateAccountAsync(Address($"race{i}-a"), ct);
            var second = await CreateAccountAsync(Address($"race{i}-b"), ct);
            races.Add((first, second, contested,
                await MailedTokenAsync(first, contested), await MailedTokenAsync(second, contested)));
        }

        var outcomes = await Task.WhenAll(races.Select(async race =>
        {
            var responses = await Task.WhenAll(
                ConfirmAsync(race.First, race.Contested, race.FirstToken, ct),
                ConfirmAsync(race.Second, race.Contested, race.SecondToken, ct));
            return responses.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray();
        }));

        foreach (var statuses in outcomes)
            statuses.ShouldBe([HttpStatusCode.NoContent, HttpStatusCode.BadRequest]);

        foreach (var race in races)
        {
            (await RowsOnAddressAsync(race.Contested)).ShouldBe(1);
            foreach (var userId in new[] { race.First, race.Second })
            {
                var user = await ReadAsync(userId);
                user.UserName.ShouldBe(user.Email);
            }
        }
    }
}
