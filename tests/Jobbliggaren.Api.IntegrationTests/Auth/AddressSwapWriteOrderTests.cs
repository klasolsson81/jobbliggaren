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
using Npgsql;
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
    public async Task SetUserNameAsync_succeeds_again_on_the_name_a_half_failed_swap_left()
    {
        // The retry after a swap whose second write failed. The first call below is that swap's first write, so the
        // row a fresh request then loads holds the new user name and the old address.
        var ct = TestContext.Current.CancellationToken;
        var oldEmail = Address("retry");
        var newEmail = Address("retry-new");
        var userId = await CreateAccountAsync(oldEmail, ct);

        using (var first = _factory.Services.CreateScope())
        {
            var userManager = first.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await userManager.FindByIdAsync(userId.ToString())).ShouldNotBeNull();
            (await userManager.SetUserNameAsync(user, newEmail)).Succeeded.ShouldBeTrue();
        }

        using var retry = _factory.Services.CreateScope();
        var retryManager = retry.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var halfMoved = (await retryManager.FindByIdAsync(userId.ToString())).ShouldNotBeNull();
        halfMoved.UserName.ShouldBe(newEmail);
        halfMoved.Email.ShouldBe(oldEmail);

        (await retryManager.SetUserNameAsync(halfMoved, newEmail)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task The_user_name_index_refuses_a_second_row_on_one_name_and_the_refusal_is_recognised()
    {
        // What the race's loser meets: the validator's read found no one on the name, and the write hits the
        // index. The write here has the loser's shape, an UPDATE of an existing row's normalised user name, and
        // goes through the Identity context so that no validator reads first.
        var ct = TestContext.Current.CancellationToken;
        var held = Address("index-held");
        await CreateAccountAsync(held, ct);
        var otherId = await CreateAccountAsync(Address("index-other"), ct);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        var other = await db.Users.SingleAsync(u => u.Id == otherId, ct);
        other.NormalizedUserName = scope.ServiceProvider.GetRequiredService<ILookupNormalizer>().NormalizeName(held);

        var refused = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));

        scope.ServiceProvider.GetRequiredService<IDbExceptionInspector>()
            .IsUniqueConstraintViolation(refused).ShouldBeTrue();
        refused.InnerException.ShouldBeOfType<PostgresException>().ConstraintName.ShouldBe("UserNameIndex");
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
    public async Task Swaps_racing_to_one_address_end_with_one_winner_and_one_row_on_it()
    {
        // Pairs confirm at the same instant. Whichever way each pair interleaves — the validator's read refusing
        // the second, or the index refusing its write — exactly one wins, the other is answered 400 and not 500,
        // and the address ends on the winner's row alone.
        var ct = TestContext.Current.CancellationToken;
        const int pairs = 6;

        var races = new List<Race>();
        for (var i = 0; i < pairs; i++)
        {
            var contested = Address($"race{i}");
            var firstOld = Address($"race{i}-a");
            var secondOld = Address($"race{i}-b");
            var first = await CreateAccountAsync(firstOld, ct);
            var second = await CreateAccountAsync(secondOld, ct);
            races.Add(new Race(
                first, firstOld, await MailedTokenAsync(first, contested),
                second, secondOld, await MailedTokenAsync(second, contested),
                contested));
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

            var first = await ReadAsync(race.First);
            var second = await ReadAsync(race.Second);
            var (winner, loser, loserOld) = first.Email == race.Contested
                ? (first, second, race.SecondOld)
                : (second, first, race.FirstOld);

            winner.Email.ShouldBe(race.Contested);
            winner.UserName.ShouldBe(race.Contested);
            loser.Email.ShouldBe(loserOld);
        }
    }

    private sealed record Race(
        Guid First, string FirstOld, string FirstToken,
        Guid Second, string SecondOld, string SecondToken,
        string Contested);
}
