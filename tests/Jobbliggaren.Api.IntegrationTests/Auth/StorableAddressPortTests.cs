using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1737 — which addresses the account service will STORE, through real Identity and Postgres (security-auditor
/// MA-1 and her test rows 1 and 4, 2026-09-21). Two rules meet at the same seam: Identity's user-name charset no
/// longer refuses an address the two email validators admit, and an address carrying a control, format, surrogate
/// or whitespace character is refused before it can be stored.
/// </summary>
[Collection("Api")]
public class StorableAddressPortTests(ApiFactory factory)
{
    private const string Password = "Correct-Horse-Battery-9";

    private readonly ApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Tag() => Guid.NewGuid().ToString("N");

    private async Task<T> WithAccountsAsync<T>(Func<IUserAccountService, UserManager<ApplicationUser>, Task<T>> body)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await body(
            scope.ServiceProvider.GetRequiredService<IUserAccountService>(),
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
    }

    private Task<int> RowsTaggedAsync(string tag) =>
        WithAccountsAsync((_, users) => users.Users.CountAsync(u => u.Email != null && u.Email.Contains(tag), Ct));

    [Theory]
    [InlineData("o'brien-{0}@example.se")]
    [InlineData("björn-{0}@example.se")]
    [InlineData("a!b#c-{0}@example.se")]
    public async Task An_address_the_email_validators_admit_can_be_registered(string pattern)
    {
        var email = pattern.Replace("{0}", Tag());

        var row = await WithAccountsAsync(async (accounts, users) =>
        {
            (await accounts.CreateUserAsync(email, Password, Ct)).IsSuccess.ShouldBeTrue();
            return (await users.FindByEmailAsync(email)).ShouldNotBeNull();
        });

        row.Email.ShouldBe(email);
        row.UserName.ShouldBe(email);
    }

    [Theory]
    [InlineData(" pad-{0}@example.se")]
    [InlineData("nul\u0000-{0}@example.se")]
    [InlineData("rlo\u202E-{0}@example.se")]
    [InlineData("crlf\r\n-{0}@example.se")]
    public async Task An_unstorable_address_is_refused_in_swedish_and_leaves_no_row(string pattern)
    {
        var tag = Tag();
        var email = pattern.Replace("{0}", tag);
        email.ShouldContain(tag, customMessage: "the no-row reading below is only as strong as this substitution");

        var result = await WithAccountsAsync((accounts, _) => accounts.CreateUserAsync(email, Password, Ct));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailNotStorable);
        result.Error.Message.ShouldBe(AuthErrorCodes.EmailNotStorableMessage);
        (await RowsTaggedAsync(tag)).ShouldBe(0);
    }

    [Fact]
    public async Task The_passwordless_creator_refuses_an_unstorable_address_and_leaves_no_row()
    {
        // The address reaching it is the PROVEN one out of the grant, and the login challenge's request path
        // asks no such question: this is the only place it is asked before a passwordless account exists.
        var tag = Tag();

        await using var scope = _factory.Services.CreateAsyncScope();
        var creator = scope.ServiceProvider.GetRequiredService<IPasswordlessAccountCreator>();
        var result = await creator.CreatePasswordlessUserAsync($" pad-{tag}@example.se", Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailNotStorable);
        (await RowsTaggedAsync(tag)).ShouldBe(0);
    }

    [Fact]
    public async Task A_padded_spelling_of_a_stored_address_cannot_become_a_second_account()
    {
        // Identity keeps " a@x" and "a@x" apart as two rows while SubjectFingerprint.Hex, which trims, gives both
        // one key: two accounts on one challenge index and one set of budgets.
        var tag = Tag();
        var stored = $"twin-{tag}@example.se";

        var padded = await WithAccountsAsync(async (accounts, _) =>
        {
            (await accounts.CreateUserAsync(stored, Password, Ct)).IsSuccess.ShouldBeTrue();
            return await accounts.CreateUserAsync(" " + stored, Password, Ct);
        });

        padded.Error.Code.ShouldBe(AuthErrorCodes.EmailNotStorable);
        (await RowsTaggedAsync(tag)).ShouldBe(1);
    }

    // The residual security-auditor accepted (ADR 0142, Amendment 2026-09-21 (2)): both spellings are made of
    // visible characters, so both are storable, and Identity's unique normalised user name then refuses the
    // second. No session and no data cross; the second spelling's holder can neither register nor log in.
    [Theory]
    [InlineData("\u017Fquat-{0}@example.se", "squat-{0}@example.se")]
    [InlineData("bjo\u0308rn-{0}@example.se", "bj\u00F6rn-{0}@example.se")]
    public async Task A_spelling_Identity_folds_onto_another_holds_it_out_when_registered_first(
        string first, string second)
    {
        var tag = Tag();

        var plain = await WithAccountsAsync(async (accounts, _) =>
        {
            (await accounts.CreateUserAsync(first.Replace("{0}", tag), Password, Ct)).IsSuccess.ShouldBeTrue();
            return await accounts.CreateUserAsync(second.Replace("{0}", tag), Password, Ct);
        });

        plain.Error.Code.ShouldBe(AuthErrorCodes.DuplicateAccount);
        (await RowsTaggedAsync(tag)).ShouldBe(1);
    }

    [Fact]
    public async Task A_change_to_an_unstorable_address_is_refused_when_the_address_is_checked()
    {
        var tag = Tag();
        var stored = $"ask-{tag}@example.se";

        var result = await WithAccountsAsync(async (accounts, _) =>
        {
            var userId = (await accounts.CreateUserAsync(stored, Password, Ct)).Value;
            return await accounts.CheckAddressIsFreeAsync(userId, " other-" + stored, Ct);
        });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailNotStorable);
    }

    [Fact]
    public async Task A_change_to_an_unstorable_address_is_refused_at_the_write()
    {
        var tag = Tag();
        var stored = $"write-{tag}@example.se";
        var padded = " other-" + stored;

        var (result, row) = await WithAccountsAsync(async (accounts, users) =>
        {
            var userId = (await accounts.CreateUserAsync(stored, Password, Ct)).Value;
            var swap = await accounts.SwapConfirmedAddressAsync(userId, padded, Ct);
            return (swap, (await users.FindByIdAsync(userId.ToString())).ShouldNotBeNull());
        });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailNotStorable);
        (await RowsTaggedAsync(tag)).ShouldBe(1);
        row.Email.ShouldBe(stored);
        row.UserName.ShouldBe(stored);
    }

    [Fact]
    public async Task A_change_to_a_non_ascii_address_takes_the_user_name_with_it()
    {
        var tag = Tag();
        var stored = $"move-{tag}@example.se";
        var next = $"björn-{tag}@example.se";

        var row = await WithAccountsAsync(async (accounts, users) =>
        {
            var userId = (await accounts.CreateUserAsync(stored, Password, Ct)).Value;
            (await accounts.CheckAddressIsFreeAsync(userId, next, Ct)).IsSuccess.ShouldBeTrue();
            (await accounts.SwapConfirmedAddressAsync(userId, next, Ct)).IsSuccess.ShouldBeTrue();
            return (await users.FindByIdAsync(userId.ToString())).ShouldNotBeNull();
        });

        row.Email.ShouldBe(next);
        row.UserName.ShouldBe(next);
    }
}
