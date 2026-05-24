using JobbPilot.Domain.Invitations;
using JobbPilot.Domain.UnitTests.JobAds;
using JobbPilot.Domain.Waitlist;
using JobbPilot.Domain.Waitlist.Events;
using Shouldly;

namespace JobbPilot.Domain.UnitTests.Waitlist;

public class WaitlistEntryTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly Guid AdminId = Guid.NewGuid();
    private const string ValidEmail = "klasskamrat@example.com";
    private const string ValidName = "Klas Olsson";
    private const string ValidMotivation = "Jag vill testa JobbPilot för att hantera mina ansökningar.";

    private static AcceptanceSnapshot ValidAcceptance(
        bool marketing = false,
        string privacyPolicyVersion = "1.0") =>
        new(marketing, Clock.UtcNow, privacyPolicyVersion);

    [Fact]
    public void Request_WithValidInput_CreatesPendingEntry()
    {
        var acceptance = ValidAcceptance();

        var result = WaitlistEntry.Request(ValidEmail, ValidName, ValidMotivation, acceptance, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBe(ValidEmail);
        result.Value.Name.ShouldBe(ValidName);
        result.Value.Motivation.ShouldBe(ValidMotivation);
        result.Value.Acceptance.ShouldBe(acceptance);
        result.Value.Status.ShouldBe(WaitlistStatus.Pending);
        result.Value.RequestedAt.ShouldBe(Clock.UtcNow);
        result.Value.ApprovedAt.ShouldBeNull();
        result.Value.RejectedAt.ShouldBeNull();
        result.Value.ResultingInvitationId.ShouldBeNull();
    }

    [Fact]
    public void Request_RaisesRequestedEvent_WithEmailOnly()
    {
        var result = WaitlistEntry.Request(
            ValidEmail, ValidName, ValidMotivation, ValidAcceptance(), Clock);

        var evt = result.Value.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<WaitlistEntryRequestedDomainEvent>();
        evt.WaitlistEntryId.ShouldBe(result.Value.Id);
        evt.Email.ShouldBe(ValidEmail);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void Request_NormalizesEmailLowercaseAndTrim()
    {
        var result = WaitlistEntry.Request(
            "  Klas@Example.COM  ", ValidName, ValidMotivation, ValidAcceptance(), Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBe("klas@example.com");
    }

    [Fact]
    public void Request_TrimsNameAndMotivation()
    {
        var result = WaitlistEntry.Request(
            ValidEmail,
            "  Klas Olsson  ",
            "  Jag vill testa JobbPilot för att hantera mina ansökningar.  ",
            ValidAcceptance(),
            Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Klas Olsson");
        result.Value.Motivation.ShouldBe("Jag vill testa JobbPilot för att hantera mina ansökningar.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Request_WithBlankEmail_Fails(string? email)
    {
        var result = WaitlistEntry.Request(email, ValidName, ValidMotivation, ValidAcceptance(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.EmailRequired");
    }

    [Fact]
    public void Request_WithEmailWithoutAt_Fails()
    {
        var result = WaitlistEntry.Request("no-at-sign", ValidName, ValidMotivation, ValidAcceptance(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.EmailInvalid");
    }

    [Fact]
    public void Request_WithTooLongEmail_Fails()
    {
        var tooLong = new string('a', 250) + "@x.se";

        var result = WaitlistEntry.Request(tooLong, ValidName, ValidMotivation, ValidAcceptance(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.EmailTooLong");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Request_WithBlankName_Fails(string? name)
    {
        var result = WaitlistEntry.Request(ValidEmail, name, ValidMotivation, ValidAcceptance(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.NameRequired");
    }

    [Fact]
    public void Request_WithTooLongName_Fails()
    {
        var tooLong = new string('A', 101);

        var result = WaitlistEntry.Request(ValidEmail, tooLong, ValidMotivation, ValidAcceptance(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.NameInvalidLength");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Request_WithBlankMotivation_Fails(string? motivation)
    {
        var result = WaitlistEntry.Request(ValidEmail, ValidName, motivation, ValidAcceptance(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.MotivationRequired");
    }

    [Fact]
    public void Request_WithTooShortMotivation_Fails()
    {
        var result = WaitlistEntry.Request(ValidEmail, ValidName, "kort", ValidAcceptance(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.MotivationInvalidLength");
    }

    [Fact]
    public void Request_WithTooLongMotivation_Fails()
    {
        var tooLong = new string('a', 1001);

        var result = WaitlistEntry.Request(ValidEmail, ValidName, tooLong, ValidAcceptance(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.MotivationInvalidLength");
    }

    [Fact]
    public void Request_WithoutMarketingAcceptance_Succeeds()
    {
        var acceptance = ValidAcceptance(marketing: false);

        var result = WaitlistEntry.Request(ValidEmail, ValidName, ValidMotivation, acceptance, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Acceptance.MarketingEmailAccepted.ShouldBeFalse();
    }

    [Fact]
    public void Request_WithMarketingAcceptance_Succeeds()
    {
        var acceptance = ValidAcceptance(marketing: true);

        var result = WaitlistEntry.Request(ValidEmail, ValidName, ValidMotivation, acceptance, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Acceptance.MarketingEmailAccepted.ShouldBeTrue();
    }

    [Fact]
    public void Request_WithBlankPrivacyPolicyVersion_Fails()
    {
        var acceptance = ValidAcceptance(privacyPolicyVersion: "");

        var result = WaitlistEntry.Request(ValidEmail, ValidName, ValidMotivation, acceptance, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.PrivacyPolicyVersionRequired");
    }

    [Fact]
    public void RefreshRequest_OnPending_UpdatesFieldsAndPreservesRequestedAt()
    {
        var entry = WaitlistEntry.Request(
            ValidEmail, ValidName, ValidMotivation, ValidAcceptance(), Clock).Value;
        var originalRequestedAt = entry.RequestedAt;
        entry.ClearDomainEvents();

        var newAcceptance = ValidAcceptance(marketing: true, privacyPolicyVersion: "2.0");
        var result = entry.RefreshRequest(
            "Klas Olsson Uppdaterad",
            "En uppdaterad motivering med ny information.",
            newAcceptance,
            Clock);

        result.IsSuccess.ShouldBeTrue();
        entry.Name.ShouldBe("Klas Olsson Uppdaterad");
        entry.Motivation.ShouldBe("En uppdaterad motivering med ny information.");
        entry.Acceptance.ShouldBe(newAcceptance);
        entry.RequestedAt.ShouldBe(originalRequestedAt);
        entry.Status.ShouldBe(WaitlistStatus.Pending);

        var evt = entry.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<WaitlistEntryRefreshedDomainEvent>();
        evt.WaitlistEntryId.ShouldBe(entry.Id);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void RefreshRequest_OnApproved_Fails()
    {
        var entry = WaitlistEntry.Request(
            ValidEmail, ValidName, ValidMotivation, ValidAcceptance(), Clock).Value;
        entry.Approve(AdminId, InvitationId.New(), Clock);

        var result = entry.RefreshRequest(
            "Nytt namn", "En uppdaterad motivering med ny information.", ValidAcceptance(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.NotPending");
    }

    [Fact]
    public void RefreshRequest_OnRejected_Fails()
    {
        var entry = WaitlistEntry.Request(
            ValidEmail, ValidName, ValidMotivation, ValidAcceptance(), Clock).Value;
        entry.Reject(AdminId, Clock);

        var result = entry.RefreshRequest(
            "Nytt namn", "En uppdaterad motivering med ny information.", ValidAcceptance(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.NotPending");
    }

    [Fact]
    public void RefreshRequest_WithInvalidMotivation_Fails()
    {
        var entry = WaitlistEntry.Request(
            ValidEmail, ValidName, ValidMotivation, ValidAcceptance(), Clock).Value;

        var result = entry.RefreshRequest(ValidName, "kort", ValidAcceptance(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.MotivationInvalidLength");
    }

    [Fact]
    public void Approve_WhenPending_FlipsToApprovedAndLinksInvitation()
    {
        var entry = WaitlistEntry.Request(
            ValidEmail, ValidName, ValidMotivation, ValidAcceptance(), Clock).Value;
        entry.ClearDomainEvents();
        var invitationId = InvitationId.New();

        var result = entry.Approve(AdminId, invitationId, Clock);

        result.IsSuccess.ShouldBeTrue();
        entry.Status.ShouldBe(WaitlistStatus.Approved);
        entry.ApprovedAt.ShouldBe(Clock.UtcNow);
        entry.ApprovedByAdminId.ShouldBe(AdminId);
        entry.ResultingInvitationId.ShouldBe(invitationId);
        var evt = entry.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<WaitlistEntryApprovedDomainEvent>();
        evt.ApprovedByAdminId.ShouldBe(AdminId);
        evt.ResultingInvitationId.ShouldBe(invitationId);
    }

    [Fact]
    public void Approve_TwiceFails()
    {
        var entry = WaitlistEntry.Request(
            ValidEmail, ValidName, ValidMotivation, ValidAcceptance(), Clock).Value;
        entry.Approve(AdminId, InvitationId.New(), Clock);

        var second = entry.Approve(AdminId, InvitationId.New(), Clock);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("WaitlistEntry.NotPending");
    }

    [Fact]
    public void Approve_WithEmptyAdminId_Fails()
    {
        var entry = WaitlistEntry.Request(
            ValidEmail, ValidName, ValidMotivation, ValidAcceptance(), Clock).Value;

        var result = entry.Approve(Guid.Empty, InvitationId.New(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.ApprovedByAdminIdRequired");
    }

    [Fact]
    public void Approve_WithDefaultInvitationId_Fails()
    {
        var entry = WaitlistEntry.Request(
            ValidEmail, ValidName, ValidMotivation, ValidAcceptance(), Clock).Value;

        var result = entry.Approve(AdminId, default, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.ResultingInvitationIdRequired");
    }

    [Fact]
    public void Approve_AfterReject_Fails()
    {
        var entry = WaitlistEntry.Request(
            ValidEmail, ValidName, ValidMotivation, ValidAcceptance(), Clock).Value;
        entry.Reject(AdminId, Clock);

        var result = entry.Approve(AdminId, InvitationId.New(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.NotPending");
    }

    [Fact]
    public void Reject_WhenPending_FlipsToRejected()
    {
        var entry = WaitlistEntry.Request(
            ValidEmail, ValidName, ValidMotivation, ValidAcceptance(), Clock).Value;
        entry.ClearDomainEvents();

        var result = entry.Reject(AdminId, Clock);

        result.IsSuccess.ShouldBeTrue();
        entry.Status.ShouldBe(WaitlistStatus.Rejected);
        entry.RejectedAt.ShouldBe(Clock.UtcNow);
        entry.RejectedByAdminId.ShouldBe(AdminId);
        entry.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<WaitlistEntryRejectedDomainEvent>();
    }

    [Fact]
    public void Reject_ErasesPII_BehallsAuditTrail()
    {
        // GDPR Art. 5(1)(c) + Art. 17: PII raderas vid Reject, audit-bevis bevaras.
        // CTO-dom 2026-05-24 Fynd 2 Approach A.
        var acceptance = ValidAcceptance();
        var entry = WaitlistEntry.Request(
            ValidEmail, ValidName, ValidMotivation, acceptance, Clock).Value;
        var originalAcceptedAt = entry.Acceptance.AcceptedAt;

        entry.Reject(AdminId, Clock);

        // PII raderad:
        entry.Name.ShouldBe(WaitlistEntry.ErasedNameSentinel);
        entry.Motivation.ShouldBe(WaitlistEntry.ErasedMotivationSentinel);
        entry.Acceptance.MarketingEmailAccepted.ShouldBeFalse();
        entry.Acceptance.PrivacyPolicyVersion.ShouldBe(WaitlistEntry.ErasedAcceptanceVersion);

        // Audit-bevis bevarat:
        entry.Email.ShouldBe(ValidEmail);
        entry.RejectedAt.ShouldBe(Clock.UtcNow);
        entry.RejectedByAdminId.ShouldBe(AdminId);
        entry.Acceptance.AcceptedAt.ShouldBe(originalAcceptedAt);
    }

    [Fact]
    public void Reject_TwiceFails()
    {
        var entry = WaitlistEntry.Request(
            ValidEmail, ValidName, ValidMotivation, ValidAcceptance(), Clock).Value;
        entry.Reject(AdminId, Clock);

        var second = entry.Reject(AdminId, Clock);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("WaitlistEntry.NotPending");
    }

    [Fact]
    public void Reject_AfterApprove_Fails()
    {
        var entry = WaitlistEntry.Request(
            ValidEmail, ValidName, ValidMotivation, ValidAcceptance(), Clock).Value;
        entry.Approve(AdminId, InvitationId.New(), Clock);

        var result = entry.Reject(AdminId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.NotPending");
    }

    [Fact]
    public void Reject_WithEmptyAdminId_Fails()
    {
        var entry = WaitlistEntry.Request(
            ValidEmail, ValidName, ValidMotivation, ValidAcceptance(), Clock).Value;

        var result = entry.Reject(Guid.Empty, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.RejectedByAdminIdRequired");
    }
}
