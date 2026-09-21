using System.Buffers.Text;
using System.Text;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1739 (PR 1) — the WIRING of <c>ConfirmChangeEmailAsync</c>: which Identity call runs before which, and
/// what stops the method. The unique index is on the normalised user name, while the e-mail index is not
/// unique and <c>RequireUniqueEmail</c> reads before it writes, so of two swaps racing to one address only
/// the user-name write can refuse the loser. The order is therefore the fix, and an order is only visible
/// on a substituted <see cref="UserManager{TUser}"/>; what the real one does at each step is pinned against
/// Postgres in <c>AddressSwapWriteOrderTests</c>.
/// </summary>
public class UserAccountServiceAddressSwapTests
{
    private const string NewEmail = "ny@example.se";
    private const string MailedToken = "the-mailed-token";
    private const string FreshToken = "the-token-minted-after-the-user-name-write";

    private static readonly string UrlSafeMailedToken =
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(MailedToken));

    private readonly UserManager<ApplicationUser> _userManager =
        Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
    private readonly IDbExceptionInspector _dbExceptions = Substitute.For<IDbExceptionInspector>();
    private readonly RecordingLogger<UserAccountService> _logger = new();
    private readonly ApplicationUser _user = new()
    {
        Id = Guid.NewGuid(),
        Email = "gammal@example.se",
        UserName = "gammal@example.se",
    };
    private readonly UserAccountService _sut;

    public UserAccountServiceAddressSwapTests()
    {
        _sut = new UserAccountService(
            _userManager,
            Substitute.For<ILoginTimingEqualizer>(),
            Options.Create(new AuthOptions()),
            _logger,
            _dbExceptions);

        _userManager.FindByIdAsync(_user.Id.ToString()).Returns(_user);
        _userManager.VerifyUserTokenAsync(_user, Arg.Any<string>(), Arg.Any<string>(), MailedToken).Returns(true);
        _userManager.SetUserNameAsync(_user, NewEmail).Returns(IdentityResult.Success);
        _userManager.GenerateChangeEmailTokenAsync(_user, NewEmail).Returns(FreshToken);
        _userManager.ChangeEmailAsync(_user, NewEmail, FreshToken).Returns(IdentityResult.Success);
    }

    private Task<Domain.Common.Result> ConfirmAsync() =>
        _sut.ConfirmChangeEmailAsync(_user.Id, NewEmail, UrlSafeMailedToken, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ConfirmChangeEmailAsync_TakesTheUserNameBeforeItWritesTheAddress()
    {
        var result = await ConfirmAsync();

        result.IsSuccess.ShouldBeTrue();
        Received.InOrder(() =>
        {
            _userManager.VerifyUserTokenAsync(_user, Arg.Any<string>(), Arg.Any<string>(), MailedToken);
            _userManager.SetUserNameAsync(_user, NewEmail);
            _userManager.GenerateChangeEmailTokenAsync(_user, NewEmail);
            _userManager.ChangeEmailAsync(_user, NewEmail, FreshToken);
        });
    }

    [Fact]
    public async Task ConfirmChangeEmailAsync_VerifiesTheMailedTokenForThisAddress()
    {
        await ConfirmAsync();

        // The purpose carries the address, so a token mailed for one address cannot confirm another.
        await _userManager.Received(1).VerifyUserTokenAsync(
            _user,
            _userManager.Options.Tokens.ChangeEmailTokenProvider,
            UserManager<ApplicationUser>.GetChangeEmailTokenPurpose(NewEmail),
            MailedToken);
    }

    [Fact]
    public async Task ConfirmChangeEmailAsync_WritesNothing_WhenTheMailedTokenDoesNotVerify()
    {
        _userManager.VerifyUserTokenAsync(_user, Arg.Any<string>(), Arg.Any<string>(), MailedToken).Returns(false);

        var result = await ConfirmAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidEmailChangeToken");
        await _userManager.DidNotReceive().SetUserNameAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
        await _userManager.DidNotReceive().GenerateChangeEmailTokenAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
        await _userManager.DidNotReceive().ChangeEmailAsync(
            Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ConfirmChangeEmailAsync_NeverPassesTheMailedTokenToTheAddressWrite()
    {
        await ConfirmAsync();

        // The user-name write rotates the security stamp, so the mailed token is dead by then. It authorises;
        // the token minted after that write is only the argument ChangeEmailAsync requires.
        await _userManager.DidNotReceive().ChangeEmailAsync(_user, NewEmail, MailedToken);
    }

    [Fact]
    public async Task ConfirmChangeEmailAsync_RefusesAndLeavesTheAddress_WhenTheUserNameIsTaken()
    {
        // What UserValidator answers when another row already holds the name, in IdentityErrorDescriber's own words.
        _userManager.SetUserNameAsync(_user, NewEmail)
            .Returns(IdentityResult.Failed(new IdentityErrorDescriber().DuplicateUserName(NewEmail)));

        var result = await ConfirmAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidEmailChangeToken");
        await _userManager.DidNotReceive().ChangeEmailAsync(
            Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ConfirmChangeEmailAsync_RefusesAndLeavesTheAddress_WhenTheUniqueIndexRefusesTheUserName()
    {
        // The race itself: both swaps passed the validator's read, and the index refused this one's write. The
        // EF store lets that surface as a DbUpdateException; AddressSwapWriteOrderTests pins that the index
        // produces it and that the inspector recognises it.
        var refused = new DbUpdateException("unique violation");
        _userManager.SetUserNameAsync(_user, NewEmail).ThrowsAsync(refused);
        _dbExceptions.IsUniqueConstraintViolation(refused).Returns(true);

        var result = await ConfirmAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidEmailChangeToken");
        await _userManager.DidNotReceive().ChangeEmailAsync(
            Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ConfirmChangeEmailAsync_LetsAnyOtherSaveFailureThrough()
    {
        var other = new DbUpdateException("not a unique violation");
        _userManager.SetUserNameAsync(_user, NewEmail).ThrowsAsync(other);
        _dbExceptions.IsUniqueConstraintViolation(other).Returns(false);

        var thrown = await Should.ThrowAsync<DbUpdateException>(ConfirmAsync);

        thrown.ShouldBeSameAs(other);
    }

    [Fact]
    public async Task ConfirmChangeEmailAsync_RefusesAndSaysSo_WhenTheAddressWriteFailsAfterTheUserNameWrite()
    {
        _userManager.ChangeEmailAsync(_user, NewEmail, FreshToken)
            .Returns(IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure()));

        var result = await ConfirmAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidEmailChangeToken");
        var entry = _logger.Records.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.EventId.Id.ShouldBe(4001);
        entry.Message.ShouldContain(_user.Id.ToString());
        entry.Message.ShouldNotContain(NewEmail);
    }

    [Fact]
    public async Task ConfirmChangeEmailAsync_LogsNothing_WhenTheSwapSucceeds()
    {
        await ConfirmAsync();

        _logger.Records.ShouldBeEmpty();
    }
}
