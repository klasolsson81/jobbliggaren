using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Invitations.Commands.RedeemInvitation;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Invitations;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Invitations;

public class RedeemInvitationCommandHandlerTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private const string ValidPassword = "S3kret!pass";

    private static (AppDbContext Db, Invitation Inv, string Plaintext, string Hash) SeedPendingInvitation()
    {
        var db = TestAppDbContextFactory.Create();
        const string plaintext = "plaintext-redeem-token";
        const string hash = "hash-redeem-abc";
        var inv = Invitation.Issue(
            "inbjuden@example.com",
            InvitationOrigin.DirectInvite,
            hash,
            TimeSpan.FromDays(7),
            Guid.NewGuid(),
            Clock).Value;
        db.Invitations.Add(inv);
        db.SaveChanges();
        return (db, inv, plaintext, hash);
    }

    private static IInvitationTokenGenerator TokenGenWithHash(string plaintext, string hash)
    {
        var gen = Substitute.For<IInvitationTokenGenerator>();
        gen.Hash(plaintext).Returns(hash);
        return gen;
    }

    [Fact]
    public async Task Handle_WithValidToken_RedeemsAndCreatesSession()
    {
        var (db, _, plaintext, hash) = SeedPendingInvitation();
        var newUserId = Guid.NewGuid();
        var userService = Substitute.For<IUserAccountService>();
        userService.CreateUserAsync("inbjuden@example.com", ValidPassword, Arg.Any<CancellationToken>())
            .Returns(Result.Success(newUserId));
        var sessionStore = Substitute.For<ISessionStore>();
        var sessionId = SessionId.Generate();
        sessionStore.CreateAsync(newUserId, Arg.Any<CancellationToken>())
            .Returns(new Session(sessionId, newUserId, Clock.UtcNow, Clock.UtcNow.AddDays(14)));

        var handler = new RedeemInvitationCommandHandler(
            db,
            TokenGenWithHash(plaintext, hash),
            userService,
            sessionStore,
            Substitute.For<IAuthAuditLogger>(),
            Clock);

        var result = await handler.Handle(
            new RedeemInvitationCommand(plaintext, ValidPassword, "Klas Olsson"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SessionId.ShouldBe(sessionId.Reveal());
    }

    [Fact]
    public async Task Handle_UsesInvitationEmailNotCommandEmail()
    {
        var (db, _, plaintext, hash) = SeedPendingInvitation();
        var userService = Substitute.For<IUserAccountService>();
        userService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Guid.NewGuid()));
        var sessionStore = Substitute.For<ISessionStore>();
        sessionStore.CreateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Session(SessionId.Generate(), Guid.NewGuid(), Clock.UtcNow, Clock.UtcNow.AddDays(14)));

        var handler = new RedeemInvitationCommandHandler(
            db,
            TokenGenWithHash(plaintext, hash),
            userService,
            sessionStore,
            Substitute.For<IAuthAuditLogger>(),
            Clock);

        await handler.Handle(
            new RedeemInvitationCommand(plaintext, ValidPassword, "Klas"), CancellationToken.None);

        // Verifierar att email kommer från Invitation, inte från command body.
        await userService.Received(1).CreateUserAsync(
            "inbjuden@example.com", ValidPassword, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithUnknownToken_ReturnsNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        var gen = Substitute.For<IInvitationTokenGenerator>();
        gen.Hash(Arg.Any<string>()).Returns("unknown-hash");

        var handler = new RedeemInvitationCommandHandler(
            db, gen,
            Substitute.For<IUserAccountService>(),
            Substitute.For<ISessionStore>(),
            Substitute.For<IAuthAuditLogger>(),
            Clock);

        var result = await handler.Handle(
            new RedeemInvitationCommand("any", ValidPassword, "Klas"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.NotFound");
        // #203: a missing invitation is a NotFound (→404), not a Validation (→400).
        result.Error.Kind.ShouldBe(Jobbliggaren.Domain.Common.ErrorKind.NotFound);
    }

    [Fact]
    public async Task Handle_WhenCreateUserFails_RollsBackWithoutRedeem()
    {
        var (db, inv, plaintext, hash) = SeedPendingInvitation();
        var userService = Substitute.For<IUserAccountService>();
        userService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(
                DomainError.Validation("Auth.DuplicateEmail", "Email används redan.")));

        var handler = new RedeemInvitationCommandHandler(
            db,
            TokenGenWithHash(plaintext, hash),
            userService,
            Substitute.For<ISessionStore>(),
            Substitute.For<IAuthAuditLogger>(),
            Clock);

        var result = await handler.Handle(
            new RedeemInvitationCommand(plaintext, ValidPassword, "Klas"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        inv.Status.ShouldBe(InvitationStatus.Pending);
    }
}
