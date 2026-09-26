using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1739 — the WIRING of <c>SwapConfirmedAddressAsync</c>: which Identity call runs before which, and what stops
/// the method. The unique index is on the normalised user name, while the e-mail index is not unique and
/// <c>RequireUniqueEmail</c> reads before it writes, so of two swaps racing to one address only the user-name
/// write can refuse the loser. The order is therefore the fix, and an order is only visible on a substituted
/// <see cref="UserManager{TUser}"/>; what the real one does at each step is pinned against Postgres in
/// <c>AddressSwapWriteOrderTests</c>. The credential was the code proven in the new inbox, so the swap verifies
/// no token of its own.
/// </summary>
public class UserAccountServiceAddressSwapTests
{
    private const string OldEmail = "gammal@example.se";
    private const string NewEmail = "ny@example.se";
    private const string FreshToken = "the-token-minted-after-the-user-name-write";

    private readonly UserManager<ApplicationUser> _userManager =
        Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
    private readonly IDbExceptionInspector _dbExceptionInspector = Substitute.For<IDbExceptionInspector>();
    private readonly RecordingLogger<UserAccountService> _logger = new();
    private readonly ApplicationUser _user = new()
    {
        Id = Guid.NewGuid(),
        Email = OldEmail,
        UserName = OldEmail,
    };
    private readonly UserAccountService _sut;

    public UserAccountServiceAddressSwapTests()
    {
        _sut = new UserAccountService(_userManager, _logger, _dbExceptionInspector);

        _userManager.FindByIdAsync(_user.Id.ToString()).Returns(_user);
        _userManager.SetUserNameAsync(_user, NewEmail).Returns(IdentityResult.Success);
        _userManager.GenerateChangeEmailTokenAsync(_user, NewEmail).Returns(FreshToken);
        _userManager.ChangeEmailAsync(_user, NewEmail, FreshToken).Returns(IdentityResult.Success);
    }

    private Task<Result> SwapAsync(string newEmail = NewEmail) =>
        _sut.SwapConfirmedAddressAsync(_user.Id, newEmail, TestContext.Current.CancellationToken);

    private async Task TheAddressIsNeverWritten() =>
        await _userManager.DidNotReceive().ChangeEmailAsync(
            Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<string>());

    [Fact]
    public async Task SwapConfirmedAddressAsync_ShouldWriteTheUserNameBeforeTheAddress()
    {
        var result = await SwapAsync();

        result.IsSuccess.ShouldBeTrue();
        Received.InOrder(() =>
        {
            _userManager.SetUserNameAsync(_user, NewEmail);
            _userManager.GenerateChangeEmailTokenAsync(_user, NewEmail);
            _userManager.ChangeEmailAsync(_user, NewEmail, FreshToken);
        });
    }

    [Fact]
    public async Task SwapConfirmedAddressAsync_ShouldVerifyNoTokenOfItsOwn()
    {
        await SwapAsync();

        await _userManager.DidNotReceiveWithAnyArgs().VerifyUserTokenAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task SwapConfirmedAddressAsync_ShouldGiveTheAddressWriteATokenMintedAfterTheUserNameWrite()
    {
        await SwapAsync();

        // The user-name write rotates the security stamp; the token minted after it is only the argument
        // ChangeEmailAsync requires.
        await _userManager.Received(1).ChangeEmailAsync(_user, NewEmail, FreshToken);
    }

    [Fact]
    public async Task SwapConfirmedAddressAsync_ShouldWriteNothing_WhenTheAddressIsNotStorable()
    {
        var result = await SwapAsync("ny\u200B@example.se");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailNotStorable);
        await _userManager.DidNotReceiveWithAnyArgs().FindByIdAsync(default!);
        await _userManager.DidNotReceiveWithAnyArgs().SetUserNameAsync(default!, default);
        await TheAddressIsNeverWritten();
    }

    [Fact]
    public async Task SwapConfirmedAddressAsync_ShouldWriteNothing_WhenTheAccountIsGone()
    {
        _userManager.FindByIdAsync(_user.Id.ToString()).Returns((ApplicationUser?)null);

        var result = await SwapAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        await _userManager.DidNotReceiveWithAnyArgs().SetUserNameAsync(default!, default);
        await TheAddressIsNeverWritten();
    }

    [Theory]
    [InlineData("DuplicateUserName")]
    [InlineData("DuplicateEmail")]
    public async Task SwapConfirmedAddressAsync_ShouldAnswerTaken_WhenTheValidatorRefusesTheUserNameAsADuplicate(string code)
    {
        _userManager.SetUserNameAsync(_user, NewEmail)
            .Returns(IdentityResult.Failed(new IdentityError { Code = code, Description = "taken" }));

        var result = await SwapAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailTaken);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        await TheAddressIsNeverWritten();
    }

    [Fact]
    public async Task SwapConfirmedAddressAsync_ShouldAnswerIncomplete_WhenTheUserNameIsRefusedForAnotherReason()
    {
        _userManager.SetUserNameAsync(_user, NewEmail)
            .Returns(IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure()));

        var result = await SwapAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailChangeIncomplete);
        await TheAddressIsNeverWritten();
    }

    [Fact]
    public async Task SwapConfirmedAddressAsync_ShouldAnswerTaken_WhenTheUniqueIndexRefusesTheUserName()
    {
        // The race itself: both swaps passed the validator's read, and the index refused this one's write. The
        // EF store lets that surface as a DbUpdateException; AddressSwapWriteOrderTests pins that the index
        // refuses such a write and that the inspector recognises the refusal.
        var refused = new DbUpdateException("unique violation");
        _userManager.SetUserNameAsync(_user, NewEmail).ThrowsAsync(refused);
        _dbExceptionInspector.IsUniqueConstraintViolation(refused).Returns(true);

        var result = await SwapAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailTaken);
        await TheAddressIsNeverWritten();
    }

    [Fact]
    public async Task SwapConfirmedAddressAsync_ShouldLetTheExceptionThrough_WhenTheSaveFailureIsNotAUniqueViolation()
    {
        var other = new DbUpdateException("not a unique violation");
        _userManager.SetUserNameAsync(_user, NewEmail).ThrowsAsync(other);
        _dbExceptionInspector.IsUniqueConstraintViolation(other).Returns(false);

        var thrown = await Should.ThrowAsync<DbUpdateException>(() => SwapAsync());

        thrown.ShouldBeSameAs(other);
    }

    [Fact]
    public async Task SwapConfirmedAddressAsync_ShouldRefuseLogAndLeaveTheUserNameWritten_WhenTheAddressWriteFails()
    {
        _userManager.ChangeEmailAsync(_user, NewEmail, FreshToken)
            .Returns(IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure()));

        var result = await SwapAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailChangeIncomplete);

        // The user name is written once, to the new address, and never written back: releasing it would hand the
        // contested name to the other swap while this row's change stands refused.
        await _userManager.Received(1).SetUserNameAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
        await _userManager.Received(1).SetUserNameAsync(_user, NewEmail);

        var entry = _logger.Records.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.EventId.Id.ShouldBe(4001);
        entry.Message.ShouldContain(_user.Id.ToString());
        entry.Message.ShouldNotContain(NewEmail);
        entry.Message.ShouldNotContain(OldEmail);
    }

    [Fact]
    public async Task SwapConfirmedAddressAsync_ShouldLogNothing_WhenTheSwapSucceeds()
    {
        await SwapAsync();

        _logger.Records.ShouldBeEmpty();
    }
}
