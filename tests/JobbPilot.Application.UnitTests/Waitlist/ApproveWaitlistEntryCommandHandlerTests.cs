using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Application.Waitlist.Commands.ApproveWaitlistEntry;
using JobbPilot.Domain.Invitations;
using JobbPilot.Domain.Waitlist;
using JobbPilot.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Waitlist;

public class ApproveWaitlistEntryCommandHandlerTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly Guid AdminId = Guid.NewGuid();

    private static (AppDbContext Db, WaitlistEntry Entry) SeedPendingEntry(string email = "vantar@example.com")
    {
        var db = TestAppDbContextFactory.Create();
        var entry = TestWaitlistFactory.CreatePending(email, Clock);
        db.WaitlistEntries.Add(entry);
        db.SaveChanges();
        return (db, entry);
    }

    private static IInvitationTokenGenerator TokenGen()
    {
        var gen = Substitute.For<IInvitationTokenGenerator>();
        gen.Generate().Returns(new InvitationToken("plaintext-app", "hash-app"));
        return gen;
    }

    private static ICurrentUser AdminUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(AdminId);
        return u;
    }

    [Fact]
    public async Task Handle_WithPendingEntry_ApprovesAndCreatesInvitation()
    {
        var (db, entry) = SeedPendingEntry();
        var handler = new ApproveWaitlistEntryCommandHandler(
            db, TokenGen(), Substitute.For<IEmailSender>(), AdminUser(), Clock);

        var result = await handler.Handle(
            new ApproveWaitlistEntryCommand(entry.Id.Value, null), CancellationToken.None);

        // Simulera UoW-pipeline-behavior (gör SaveChanges efter handler i prod).
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBe("vantar@example.com");

        var saved = db.WaitlistEntries.Single();
        saved.Status.ShouldBe(WaitlistStatus.Approved);
        saved.ResultingInvitationId.ShouldNotBeNull();
        db.Invitations.Single().Origin.ShouldBe(InvitationOrigin.WaitlistApproved);
    }

    [Fact]
    public async Task Handle_SendsInvitationEmail()
    {
        var (db, entry) = SeedPendingEntry();
        var emailSender = Substitute.For<IEmailSender>();
        var handler = new ApproveWaitlistEntryCommandHandler(
            db, TokenGen(), emailSender, AdminUser(), Clock);

        await handler.Handle(
            new ApproveWaitlistEntryCommand(entry.Id.Value, null), CancellationToken.None);

        await emailSender.Received(1).SendInvitationEmailAsync(
            "vantar@example.com",
            "plaintext-app",
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithUnknownEntry_ReturnsNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = new ApproveWaitlistEntryCommandHandler(
            db, TokenGen(), Substitute.For<IEmailSender>(), AdminUser(), Clock);

        var result = await handler.Handle(
            new ApproveWaitlistEntryCommand(Guid.NewGuid(), null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.NotFound");
    }

    [Fact]
    public async Task Handle_WhenEntryAlreadyApproved_Fails()
    {
        var (db, entry) = SeedPendingEntry();
        entry.Approve(AdminId, InvitationId.New(), Clock);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ApproveWaitlistEntryCommandHandler(
            db, TokenGen(), Substitute.For<IEmailSender>(), AdminUser(), Clock);

        var result = await handler.Handle(
            new ApproveWaitlistEntryCommand(entry.Id.Value, null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.NotPending");
    }

    [Fact]
    public async Task Handle_WithoutAdminUser_Fails()
    {
        var (db, entry) = SeedPendingEntry();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);

        var handler = new ApproveWaitlistEntryCommandHandler(
            db, TokenGen(), Substitute.For<IEmailSender>(), anon, Clock);

        var result = await handler.Handle(
            new ApproveWaitlistEntryCommand(entry.Id.Value, null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.AdminUnknown");
    }
}
