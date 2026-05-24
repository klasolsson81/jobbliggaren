using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Application.Waitlist;
using JobbPilot.Application.Waitlist.Commands.RequestWaitlistEntry;
using JobbPilot.Domain.Waitlist;
using JobbPilot.Domain.Waitlist.Events;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Waitlist;

public class RequestWaitlistEntryCommandHandlerTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    private const string ValidName = "Anna Testperson";
    private const string ValidMotivation =
        "Jag vill testa JobbPilot för att hantera mina ansökningar.";

    private static IOptions<PrivacyPolicyOptions> Options(string version = "1.0") =>
        Microsoft.Extensions.Options.Options.Create(
            new PrivacyPolicyOptions { CurrentVersion = version });

    private static RequestWaitlistEntryCommand ValidCommand(
        string email = "ny@example.com",
        string? name = null,
        string? motivation = null,
        bool marketing = false) =>
        new(
            Email: email,
            Name: name ?? ValidName,
            Motivation: motivation ?? ValidMotivation,
            MarketingEmailAccepted: marketing);

    private static RequestWaitlistEntryCommandHandler Handler(
        AppDbContext db,
        IEmailSender? emailSender = null,
        string privacyPolicyVersion = "1.0") =>
        new(db, emailSender ?? Substitute.For<IEmailSender>(), Clock, Options(privacyPolicyVersion));

    [Fact]
    public async Task Handle_WithNewEmail_CreatesPendingEntryWithAllFields()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = Handler(db);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);
        // Simulera UoW-pipeline-behavior (gör SaveChanges efter handler i prod).
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBe("ny@example.com");
        result.Value.WaitlistEntryId.ShouldNotBe(Guid.Empty);

        var saved = db.WaitlistEntries.Single();
        saved.Name.ShouldBe(ValidName);
        saved.Motivation.ShouldBe(ValidMotivation);
        saved.Acceptance.MarketingEmailAccepted.ShouldBeFalse();
        saved.Acceptance.PrivacyPolicyVersion.ShouldBe("1.0");
        saved.Acceptance.AcceptedAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public async Task Handle_SendsConfirmationEmail()
    {
        var db = TestAppDbContextFactory.Create();
        var emailSender = Substitute.For<IEmailSender>();
        var handler = Handler(db, emailSender);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        await emailSender.Received(1).SendWaitlistConfirmationAsync(
            "ny@example.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StampsPrivacyPolicyVersionFromOptions_NotFromClient()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = Handler(db, privacyPolicyVersion: "2.5-beta");

        await handler.Handle(ValidCommand(), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        db.WaitlistEntries.Single().Acceptance.PrivacyPolicyVersion.ShouldBe("2.5-beta");
    }

    [Fact]
    public async Task Handle_WithMarketingAcceptanceTrue_PersistsTrue()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = Handler(db);

        await handler.Handle(ValidCommand(marketing: true), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        db.WaitlistEntries.Single().Acceptance.MarketingEmailAccepted.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WithDuplicatePendingEmail_RefreshesExistingAndDoesNotResendEmail()
    {
        var db = TestAppDbContextFactory.Create();
        var existing = TestWaitlistFactory.CreatePending(
            "klas@example.com", Clock, name: "Gamla namnet", motivation: "Gammal motivering som är längre än 10 tecken.");
        db.WaitlistEntries.Add(existing);
        await db.SaveChangesAsync(CancellationToken.None);
        var originalRequestedAt = existing.RequestedAt;

        var emailSender = Substitute.For<IEmailSender>();
        var handler = Handler(db, emailSender);

        var result = await handler.Handle(
            ValidCommand(
                email: "klas@example.com",
                name: "Nytt namn",
                motivation: "En helt ny motivering med uppdaterat innehåll.",
                marketing: true),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WaitlistEntryId.ShouldBe(existing.Id.Value);

        await db.SaveChangesAsync(CancellationToken.None);
        var refreshed = db.WaitlistEntries.Single();
        refreshed.Name.ShouldBe("Nytt namn");
        refreshed.Motivation.ShouldBe("En helt ny motivering med uppdaterat innehåll.");
        refreshed.Acceptance.MarketingEmailAccepted.ShouldBeTrue();
        refreshed.RequestedAt.ShouldBe(originalRequestedAt);

        // Ingen ny email vid refresh — bekräftelse skickades vid första anmälan.
        await emailSender.DidNotReceive().SendWaitlistConfirmationAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());

        refreshed.DomainEvents.ShouldContain(e => e is WaitlistEntryRefreshedDomainEvent);
    }

    [Fact]
    public async Task Handle_NormalizesEmailCaseAndTrim()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = Handler(db);

        var result = await handler.Handle(
            ValidCommand(email: "  Klas@Example.COM  "), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBe("klas@example.com");
    }

    [Fact]
    public async Task Handle_WithInvalidEmail_Fails()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = Handler(db);

        var result = await handler.Handle(
            ValidCommand(email: "no-at-sign"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.EmailInvalid");
    }

    [Fact]
    public async Task Handle_WithTooShortMotivation_Fails()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = Handler(db);

        var result = await handler.Handle(
            ValidCommand(motivation: "kort"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.MotivationInvalidLength");
    }
}
