using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Invitations.Commands.IssueInvitation;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Invitations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Invitations;

public class IssueInvitationCommandHandlerTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly Guid AdminId = Guid.NewGuid();

    private static IssueInvitationCommandHandler CreateHandler(
        IAppDbContext? db = null,
        IInvitationTokenGenerator? tokenGen = null,
        IEmailSender? emailSender = null,
        ICurrentUser? currentUser = null)
    {
        db ??= CreateDbWithInvitationsSet();
        tokenGen ??= CreateTokenGenerator();
        emailSender ??= Substitute.For<IEmailSender>();
        currentUser ??= CreateAdminUser();
        return new IssueInvitationCommandHandler(db, tokenGen, emailSender, currentUser, Clock);
    }

    private static IAppDbContext CreateDbWithInvitationsSet()
    {
        var db = Substitute.For<IAppDbContext>();
        db.Invitations.Returns(Substitute.For<DbSet<Invitation>>());
        return db;
    }

    private static IInvitationTokenGenerator CreateTokenGenerator()
    {
        var gen = Substitute.For<IInvitationTokenGenerator>();
        gen.Generate().Returns(new InvitationToken("plaintext-xyz", "hash-abc"));
        return gen;
    }

    private static ICurrentUser CreateAdminUser()
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(AdminId);
        return user;
    }

    [Fact]
    public async Task Handle_WithValidEmail_ReturnsIssuedDto()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            new IssueInvitationCommand("ny@example.com", null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBe("ny@example.com");
        result.Value.InvitationId.ShouldNotBe(Guid.Empty);
        result.Value.ExpiresAt.ShouldBe(Clock.UtcNow.AddDays(7));
    }

    [Fact]
    public async Task Handle_WithCustomValidForDays_UsesCustomValue()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            new IssueInvitationCommand("ny@example.com", 14), CancellationToken.None);

        result.Value.ExpiresAt.ShouldBe(Clock.UtcNow.AddDays(14));
    }

    [Fact]
    public async Task Handle_AddsInvitationToDb()
    {
        var db = CreateDbWithInvitationsSet();
        var handler = CreateHandler(db: db);

        await handler.Handle(
            new IssueInvitationCommand("ny@example.com", null), CancellationToken.None);

        db.Invitations.Received(1).Add(Arg.Any<Invitation>());
    }

    [Fact]
    public async Task Handle_SendsInvitationEmailWithPlaintextToken()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var handler = CreateHandler(emailSender: emailSender);

        await handler.Handle(
            new IssueInvitationCommand("ny@example.com", null), CancellationToken.None);

        await emailSender.Received(1).SendInvitationEmailAsync(
            "ny@example.com",
            "plaintext-xyz",
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutAdminUserId_Fails()
    {
        var anonymous = Substitute.For<ICurrentUser>();
        anonymous.UserId.Returns((Guid?)null);
        var handler = CreateHandler(currentUser: anonymous);

        var result = await handler.Handle(
            new IssueInvitationCommand("ny@example.com", null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.AdminUnknown");
    }

    [Fact]
    public async Task Handle_WithInvalidEmail_FailsBeforeDbWrite()
    {
        var db = CreateDbWithInvitationsSet();
        var handler = CreateHandler(db: db);

        var result = await handler.Handle(
            new IssueInvitationCommand("no-at-sign", null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        db.Invitations.DidNotReceive().Add(Arg.Any<Invitation>());
    }
}
