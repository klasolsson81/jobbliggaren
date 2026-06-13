using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.Waitlist.Commands.RejectWaitlistEntry;
using Jobbliggaren.Domain.Invitations;
using Jobbliggaren.Domain.Waitlist;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Waitlist;

public class RejectWaitlistEntryCommandHandlerTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly Guid AdminId = Guid.NewGuid();

    private static (AppDbContext Db, WaitlistEntry Entry) SeedPendingEntry()
    {
        var db = TestAppDbContextFactory.Create();
        var entry = TestWaitlistFactory.CreatePending("vantar@example.com", Clock);
        db.WaitlistEntries.Add(entry);
        db.SaveChanges();
        return (db, entry);
    }

    private static ICurrentUser AdminUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(AdminId);
        return u;
    }

    [Fact]
    public async Task Handle_WithPendingEntry_RejectsAndDoesNotCreateInvitation()
    {
        var (db, entry) = SeedPendingEntry();
        var handler = new RejectWaitlistEntryCommandHandler(db, AdminUser(), Clock);

        var result = await handler.Handle(
            new RejectWaitlistEntryCommand(entry.Id.Value), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        db.WaitlistEntries.Single().Status.ShouldBe(WaitlistStatus.Rejected);
        db.Invitations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WithUnknownEntry_ReturnsNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = new RejectWaitlistEntryCommandHandler(db, AdminUser(), Clock);

        var result = await handler.Handle(
            new RejectWaitlistEntryCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.NotFound");
    }

    [Fact]
    public async Task Handle_WhenAlreadyApproved_Fails()
    {
        var (db, entry) = SeedPendingEntry();
        entry.Approve(AdminId, InvitationId.New(), Clock);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new RejectWaitlistEntryCommandHandler(db, AdminUser(), Clock);

        var result = await handler.Handle(
            new RejectWaitlistEntryCommand(entry.Id.Value), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.NotPending");
    }

    [Fact]
    public async Task Handle_WithoutAdminUser_Fails()
    {
        var (db, entry) = SeedPendingEntry();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);

        var handler = new RejectWaitlistEntryCommandHandler(db, anon, Clock);

        var result = await handler.Handle(
            new RejectWaitlistEntryCommand(entry.Id.Value), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.AdminUnknown");
    }
}
