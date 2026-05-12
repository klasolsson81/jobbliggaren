using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Application.Waitlist.Commands.RequestWaitlistEntry;
using JobbPilot.Domain.Waitlist;
using JobbPilot.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Waitlist;

public class RequestWaitlistEntryCommandHandlerTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    [Fact]
    public async Task Handle_WithNewEmail_CreatesPendingEntry()
    {
        var db = TestAppDbContextFactory.Create();
        var emailSender = Substitute.For<IEmailSender>();
        var handler = new RequestWaitlistEntryCommandHandler(db, emailSender, Clock);

        var result = await handler.Handle(
            new RequestWaitlistEntryCommand("ny@example.com"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBe("ny@example.com");
        result.Value.WaitlistEntryId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Handle_SendsConfirmationEmail()
    {
        var db = TestAppDbContextFactory.Create();
        var emailSender = Substitute.For<IEmailSender>();
        var handler = new RequestWaitlistEntryCommandHandler(db, emailSender, Clock);

        await handler.Handle(
            new RequestWaitlistEntryCommand("ny@example.com"), CancellationToken.None);

        await emailSender.Received(1).SendWaitlistConfirmationAsync(
            "ny@example.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithDuplicatePendingEmail_ReturnsExistingIdempotent()
    {
        var db = TestAppDbContextFactory.Create();
        var existing = WaitlistEntry.Request("klas@example.com", Clock).Value;
        db.WaitlistEntries.Add(existing);
        await db.SaveChangesAsync(CancellationToken.None);

        var emailSender = Substitute.For<IEmailSender>();
        var handler = new RequestWaitlistEntryCommandHandler(db, emailSender, Clock);

        var result = await handler.Handle(
            new RequestWaitlistEntryCommand("klas@example.com"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WaitlistEntryId.ShouldBe(existing.Id.Value);
        // Ingen ny email vid duplicate.
        await emailSender.DidNotReceive().SendWaitlistConfirmationAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NormalizesEmailCaseAndTrim()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = new RequestWaitlistEntryCommandHandler(
            db, Substitute.For<IEmailSender>(), Clock);

        var result = await handler.Handle(
            new RequestWaitlistEntryCommand("  Klas@Example.COM  "), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBe("klas@example.com");
    }

    [Fact]
    public async Task Handle_WithInvalidEmail_Fails()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = new RequestWaitlistEntryCommandHandler(
            db, Substitute.For<IEmailSender>(), Clock);

        var result = await handler.Handle(
            new RequestWaitlistEntryCommand("no-at-sign"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.EmailInvalid");
    }
}
