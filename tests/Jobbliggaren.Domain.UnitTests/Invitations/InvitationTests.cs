using Jobbliggaren.Domain.Invitations;
using Jobbliggaren.Domain.Invitations.Events;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Invitations;

public class InvitationTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly Guid AdminId = Guid.NewGuid();
    private const string ValidEmail = "klasskamrat@example.com";
    // Lågentropi-platshållare som inte triggar gitleaks. Domänkravet är bara
    // "icke-tom string" — riktig HMAC-SHA256-hashing sker i Application-lagret.
    private const string ValidTokenHash = "fake-token-hash-for-domain-tests";
    private static readonly TimeSpan ValidFor = TimeSpan.FromDays(7);

    [Fact]
    public void Issue_WithValidData_CreatesPendingInvitation()
    {
        var result = Invitation.Issue(
            ValidEmail, InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBe(ValidEmail);
        result.Value.Origin.ShouldBe(InvitationOrigin.DirectInvite);
        result.Value.TokenHash.ShouldBe(ValidTokenHash);
        result.Value.Status.ShouldBe(InvitationStatus.Pending);
        result.Value.IssuedByAdminId.ShouldBe(AdminId);
        result.Value.IssuedAt.ShouldBe(Clock.UtcNow);
        result.Value.ExpiresAt.ShouldBe(Clock.UtcNow.Add(ValidFor));
        result.Value.RedeemedAt.ShouldBeNull();
        result.Value.RevokedAt.ShouldBeNull();
    }

    [Fact]
    public void Issue_RaisesInvitationIssuedEvent()
    {
        var result = Invitation.Issue(
            ValidEmail, InvitationOrigin.WaitlistApproved, ValidTokenHash, ValidFor, AdminId, Clock);

        var evt = result.Value.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<InvitationIssuedDomainEvent>();
        evt.InvitationId.ShouldBe(result.Value.Id);
        evt.Email.ShouldBe(ValidEmail);
        evt.Origin.ShouldBe(InvitationOrigin.WaitlistApproved);
        evt.IssuedByAdminId.ShouldBe(AdminId);
        evt.ExpiresAt.ShouldBe(Clock.UtcNow.Add(ValidFor));
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void Issue_NormalizesEmailLowercaseAndTrim()
    {
        var result = Invitation.Issue(
            "  Klas@Example.COM  ", InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBe("klas@example.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Issue_WithBlankEmail_Fails(string? email)
    {
        var result = Invitation.Issue(
            email, InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.EmailRequired");
    }

    [Fact]
    public void Issue_WithEmailWithoutAt_Fails()
    {
        var result = Invitation.Issue(
            "no-at-sign", InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.EmailInvalid");
    }

    [Fact]
    public void Issue_WithTooLongEmail_Fails()
    {
        var tooLong = new string('a', 250) + "@x.se";

        var result = Invitation.Issue(
            tooLong, InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.EmailTooLong");
    }

    [Fact]
    public void Issue_WithBlankTokenHash_Fails()
    {
        var result = Invitation.Issue(
            ValidEmail, InvitationOrigin.DirectInvite, "", ValidFor, AdminId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.TokenHashRequired");
    }

    [Fact]
    public void Issue_WithNonPositiveValidFor_Fails()
    {
        var result = Invitation.Issue(
            ValidEmail, InvitationOrigin.DirectInvite, ValidTokenHash, TimeSpan.Zero, AdminId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.ValidForMustBePositive");
    }

    [Fact]
    public void Issue_WithEmptyAdminId_Fails()
    {
        var result = Invitation.Issue(
            ValidEmail, InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, Guid.Empty, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.IssuedByAdminIdRequired");
    }

    [Fact]
    public void Redeem_WhenPendingAndNotExpired_FlipsToRedeemed()
    {
        var invitation = Invitation.Issue(
            ValidEmail, InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock).Value;
        invitation.ClearDomainEvents();
        var userId = Guid.NewGuid();

        var result = invitation.Redeem(userId, Clock);

        result.IsSuccess.ShouldBeTrue();
        invitation.Status.ShouldBe(InvitationStatus.Redeemed);
        invitation.RedeemedAt.ShouldBe(Clock.UtcNow);
        invitation.RedeemedByUserId.ShouldBe(userId);
        var evt = invitation.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<InvitationRedeemedDomainEvent>();
        evt.RedeemedByUserId.ShouldBe(userId);
    }

    [Fact]
    public void Redeem_TwiceFailsWithAlreadyRedeemed()
    {
        var invitation = Invitation.Issue(
            ValidEmail, InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock).Value;
        invitation.Redeem(Guid.NewGuid(), Clock);

        var second = invitation.Redeem(Guid.NewGuid(), Clock);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("Invitation.AlreadyRedeemed");
    }

    [Fact]
    public void Redeem_WithEmptyUserId_Fails()
    {
        var invitation = Invitation.Issue(
            ValidEmail, InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock).Value;

        var result = invitation.Redeem(Guid.Empty, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.RedeemedByUserIdRequired");
    }

    [Fact]
    public void Redeem_WhenExpired_Fails()
    {
        var invitation = Invitation.Issue(
            ValidEmail, InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock).Value;
        var future = FakeDateTimeProvider.At(Clock.UtcNow.Add(ValidFor).AddSeconds(1));

        var result = invitation.Redeem(Guid.NewGuid(), future);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.Expired");
        invitation.Status.ShouldBe(InvitationStatus.Pending);
    }

    [Fact]
    public void Redeem_WhenRevoked_Fails()
    {
        var invitation = Invitation.Issue(
            ValidEmail, InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock).Value;
        invitation.Revoke(AdminId, Clock);

        var result = invitation.Redeem(Guid.NewGuid(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.Revoked");
    }

    [Fact]
    public void Revoke_WhenPending_FlipsToRevoked()
    {
        var invitation = Invitation.Issue(
            ValidEmail, InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock).Value;
        invitation.ClearDomainEvents();

        var result = invitation.Revoke(AdminId, Clock);

        result.IsSuccess.ShouldBeTrue();
        invitation.Status.ShouldBe(InvitationStatus.Revoked);
        invitation.RevokedAt.ShouldBe(Clock.UtcNow);
        invitation.RevokedByAdminId.ShouldBe(AdminId);
        invitation.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<InvitationRevokedDomainEvent>();
    }

    [Fact]
    public void Revoke_WhenRedeemed_Fails()
    {
        var invitation = Invitation.Issue(
            ValidEmail, InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock).Value;
        invitation.Redeem(Guid.NewGuid(), Clock);

        var result = invitation.Revoke(AdminId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.NotPending");
    }

    [Fact]
    public void Revoke_WithEmptyAdminId_Fails()
    {
        var invitation = Invitation.Issue(
            ValidEmail, InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock).Value;

        var result = invitation.Revoke(Guid.Empty, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.RevokedByAdminIdRequired");
    }

    [Fact]
    public void MarkExpired_AfterExpiresAt_FlipsToExpired()
    {
        var invitation = Invitation.Issue(
            ValidEmail, InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock).Value;
        invitation.ClearDomainEvents();
        var future = FakeDateTimeProvider.At(Clock.UtcNow.Add(ValidFor).AddSeconds(1));

        var result = invitation.MarkExpired(future);

        result.IsSuccess.ShouldBeTrue();
        invitation.Status.ShouldBe(InvitationStatus.Expired);
        invitation.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<InvitationExpiredDomainEvent>();
    }

    [Fact]
    public void MarkExpired_BeforeExpiresAt_Fails()
    {
        var invitation = Invitation.Issue(
            ValidEmail, InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock).Value;

        var result = invitation.MarkExpired(Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Invitation.NotYetExpired");
        invitation.Status.ShouldBe(InvitationStatus.Pending);
    }

    [Fact]
    public void MarkExpired_WhenAlreadyTerminal_IsIdempotentNoOp()
    {
        var invitation = Invitation.Issue(
            ValidEmail, InvitationOrigin.DirectInvite, ValidTokenHash, ValidFor, AdminId, Clock).Value;
        invitation.Redeem(Guid.NewGuid(), Clock);
        invitation.ClearDomainEvents();
        var future = FakeDateTimeProvider.At(Clock.UtcNow.Add(ValidFor).AddSeconds(1));

        var result = invitation.MarkExpired(future);

        result.IsSuccess.ShouldBeTrue();
        invitation.Status.ShouldBe(InvitationStatus.Redeemed);
        invitation.DomainEvents.ShouldBeEmpty();
    }
}
