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

    [Fact]
    public void Request_WithValidEmail_CreatesPendingEntry()
    {
        var result = WaitlistEntry.Request(ValidEmail, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBe(ValidEmail);
        result.Value.Status.ShouldBe(WaitlistStatus.Pending);
        result.Value.RequestedAt.ShouldBe(Clock.UtcNow);
        result.Value.ApprovedAt.ShouldBeNull();
        result.Value.RejectedAt.ShouldBeNull();
        result.Value.ResultingInvitationId.ShouldBeNull();
    }

    [Fact]
    public void Request_RaisesRequestedEvent()
    {
        var result = WaitlistEntry.Request(ValidEmail, Clock);

        var evt = result.Value.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<WaitlistEntryRequestedDomainEvent>();
        evt.WaitlistEntryId.ShouldBe(result.Value.Id);
        evt.Email.ShouldBe(ValidEmail);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void Request_NormalizesEmailLowercaseAndTrim()
    {
        var result = WaitlistEntry.Request("  Klas@Example.COM  ", Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBe("klas@example.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Request_WithBlankEmail_Fails(string? email)
    {
        var result = WaitlistEntry.Request(email, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.EmailRequired");
    }

    [Fact]
    public void Request_WithEmailWithoutAt_Fails()
    {
        var result = WaitlistEntry.Request("no-at-sign", Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.EmailInvalid");
    }

    [Fact]
    public void Request_WithTooLongEmail_Fails()
    {
        var tooLong = new string('a', 250) + "@x.se";

        var result = WaitlistEntry.Request(tooLong, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.EmailTooLong");
    }

    [Fact]
    public void Approve_WhenPending_FlipsToApprovedAndLinksInvitation()
    {
        var entry = WaitlistEntry.Request(ValidEmail, Clock).Value;
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
        var entry = WaitlistEntry.Request(ValidEmail, Clock).Value;
        entry.Approve(AdminId, InvitationId.New(), Clock);

        var second = entry.Approve(AdminId, InvitationId.New(), Clock);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("WaitlistEntry.NotPending");
    }

    [Fact]
    public void Approve_WithEmptyAdminId_Fails()
    {
        var entry = WaitlistEntry.Request(ValidEmail, Clock).Value;

        var result = entry.Approve(Guid.Empty, InvitationId.New(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.ApprovedByAdminIdRequired");
    }

    [Fact]
    public void Approve_WithDefaultInvitationId_Fails()
    {
        var entry = WaitlistEntry.Request(ValidEmail, Clock).Value;

        var result = entry.Approve(AdminId, default, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.ResultingInvitationIdRequired");
    }

    [Fact]
    public void Approve_AfterReject_Fails()
    {
        var entry = WaitlistEntry.Request(ValidEmail, Clock).Value;
        entry.Reject(AdminId, Clock);

        var result = entry.Approve(AdminId, InvitationId.New(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.NotPending");
    }

    [Fact]
    public void Reject_WhenPending_FlipsToRejected()
    {
        var entry = WaitlistEntry.Request(ValidEmail, Clock).Value;
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
    public void Reject_TwiceFails()
    {
        var entry = WaitlistEntry.Request(ValidEmail, Clock).Value;
        entry.Reject(AdminId, Clock);

        var second = entry.Reject(AdminId, Clock);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("WaitlistEntry.NotPending");
    }

    [Fact]
    public void Reject_AfterApprove_Fails()
    {
        var entry = WaitlistEntry.Request(ValidEmail, Clock).Value;
        entry.Approve(AdminId, InvitationId.New(), Clock);

        var result = entry.Reject(AdminId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.NotPending");
    }

    [Fact]
    public void Reject_WithEmptyAdminId_Fails()
    {
        var entry = WaitlistEntry.Request(ValidEmail, Clock).Value;

        var result = entry.Reject(Guid.Empty, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WaitlistEntry.RejectedByAdminIdRequired");
    }
}
