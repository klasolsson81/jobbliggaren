using JobbPilot.Domain.Common;
using JobbPilot.Domain.Invitations.Events;

namespace JobbPilot.Domain.Invitations;

public sealed class Invitation : AggregateRoot<InvitationId>
{
    public string Email { get; private set; } = null!;
    public InvitationOrigin Origin { get; private set; } = null!;
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public InvitationStatus Status { get; private set; } = null!;
    public Guid IssuedByAdminId { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset? RedeemedAt { get; private set; }
    public Guid? RedeemedByUserId { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? RevokedByAdminId { get; private set; }

    // EF Core constructor
    private Invitation() { }

    private Invitation(
        InvitationId id,
        string email,
        InvitationOrigin origin,
        string tokenHash,
        DateTimeOffset expiresAt,
        Guid issuedByAdminId,
        DateTimeOffset now) : base(id)
    {
        Email = email;
        Origin = origin;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        IssuedByAdminId = issuedByAdminId;
        IssuedAt = now;
        Status = InvitationStatus.Pending;
    }

    public static Result<Invitation> Issue(
        string? email,
        InvitationOrigin origin,
        string? tokenHash,
        TimeSpan validFor,
        Guid issuedByAdminId,
        IDateTimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure<Invitation>(
                DomainError.Validation("Invitation.EmailRequired", "E-postadress krävs."));

        if (email.Length > 254)
            return Result.Failure<Invitation>(
                DomainError.Validation("Invitation.EmailTooLong", "E-postadress får vara max 254 tecken."));

        if (!email.Contains('@'))
            return Result.Failure<Invitation>(
                DomainError.Validation("Invitation.EmailInvalid", "E-postadressen är inte giltig."));

        if (origin is null)
            return Result.Failure<Invitation>(
                DomainError.Validation("Invitation.OriginRequired", "Origin krävs."));

        if (string.IsNullOrWhiteSpace(tokenHash))
            return Result.Failure<Invitation>(
                DomainError.Validation("Invitation.TokenHashRequired", "Token-hash krävs."));

        if (validFor <= TimeSpan.Zero)
            return Result.Failure<Invitation>(
                DomainError.Validation("Invitation.ValidForMustBePositive", "Giltighetstid måste vara positiv."));

        if (issuedByAdminId == Guid.Empty)
            return Result.Failure<Invitation>(
                DomainError.Validation("Invitation.IssuedByAdminIdRequired", "IssuedByAdminId krävs."));

        var now = clock.UtcNow;
        var id = InvitationId.New();
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var expiresAt = now.Add(validFor);
        var invitation = new Invitation(id, normalizedEmail, origin, tokenHash, expiresAt, issuedByAdminId, now);
        invitation.RaiseDomainEvent(new InvitationIssuedDomainEvent(
            id, normalizedEmail, origin, issuedByAdminId, expiresAt, now));
        return Result.Success(invitation);
    }

    public Result Redeem(Guid redeemedByUserId, IDateTimeProvider clock)
    {
        if (redeemedByUserId == Guid.Empty)
            return Result.Failure(
                DomainError.Validation("Invitation.RedeemedByUserIdRequired", "RedeemedByUserId krävs."));

        if (Status == InvitationStatus.Redeemed)
            return Result.Failure(
                DomainError.Conflict("Invitation.AlreadyRedeemed", "Inbjudan har redan lösts in."));

        if (Status == InvitationStatus.Revoked)
            return Result.Failure(
                DomainError.Conflict("Invitation.Revoked", "Inbjudan har återkallats."));

        if (Status == InvitationStatus.Expired)
            return Result.Failure(
                DomainError.Conflict("Invitation.Expired", "Inbjudan har gått ut."));

        var now = clock.UtcNow;
        if (now >= ExpiresAt)
            return Result.Failure(
                DomainError.Conflict("Invitation.Expired", "Inbjudan har gått ut."));

        Status = InvitationStatus.Redeemed;
        RedeemedAt = now;
        RedeemedByUserId = redeemedByUserId;
        RaiseDomainEvent(new InvitationRedeemedDomainEvent(Id, redeemedByUserId, now));
        return Result.Success();
    }

    public Result Revoke(Guid revokedByAdminId, IDateTimeProvider clock)
    {
        if (revokedByAdminId == Guid.Empty)
            return Result.Failure(
                DomainError.Validation("Invitation.RevokedByAdminIdRequired", "RevokedByAdminId krävs."));

        if (Status != InvitationStatus.Pending)
            return Result.Failure(
                DomainError.Conflict("Invitation.NotPending",
                    $"Kan inte återkalla inbjudan i status {Status.Name}."));

        Status = InvitationStatus.Revoked;
        RevokedAt = clock.UtcNow;
        RevokedByAdminId = revokedByAdminId;
        RaiseDomainEvent(new InvitationRevokedDomainEvent(Id, revokedByAdminId, clock.UtcNow));
        return Result.Success();
    }

    public Result MarkExpired(IDateTimeProvider clock)
    {
        // Idempotent: redan terminal-state → no-op
        if (Status != InvitationStatus.Pending)
            return Result.Success();

        if (clock.UtcNow < ExpiresAt)
            return Result.Failure(
                DomainError.Validation("Invitation.NotYetExpired",
                    "Inbjudan har inte gått ut än."));

        Status = InvitationStatus.Expired;
        RaiseDomainEvent(new InvitationExpiredDomainEvent(Id, clock.UtcNow));
        return Result.Success();
    }
}
