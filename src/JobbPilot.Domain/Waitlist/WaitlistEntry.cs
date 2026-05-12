using JobbPilot.Domain.Common;
using JobbPilot.Domain.Invitations;
using JobbPilot.Domain.Waitlist.Events;

namespace JobbPilot.Domain.Waitlist;

public sealed class WaitlistEntry : AggregateRoot<WaitlistEntryId>
{
    public string Email { get; private set; } = null!;
    public DateTimeOffset RequestedAt { get; private set; }
    public WaitlistStatus Status { get; private set; } = null!;
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? ApprovedByAdminId { get; private set; }
    public InvitationId? ResultingInvitationId { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }
    public Guid? RejectedByAdminId { get; private set; }

    // EF Core constructor
    private WaitlistEntry() { }

    private WaitlistEntry(
        WaitlistEntryId id,
        string email,
        DateTimeOffset requestedAt) : base(id)
    {
        Email = email;
        RequestedAt = requestedAt;
        Status = WaitlistStatus.Pending;
    }

    public static Result<WaitlistEntry> Request(
        string? email,
        IDateTimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure<WaitlistEntry>(
                DomainError.Validation("WaitlistEntry.EmailRequired", "E-postadress krävs."));

        if (email.Length > 254)
            return Result.Failure<WaitlistEntry>(
                DomainError.Validation("WaitlistEntry.EmailTooLong", "E-postadress får vara max 254 tecken."));

        if (!email.Contains('@'))
            return Result.Failure<WaitlistEntry>(
                DomainError.Validation("WaitlistEntry.EmailInvalid", "E-postadressen är inte giltig."));

        var now = clock.UtcNow;
        var id = WaitlistEntryId.New();
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var entry = new WaitlistEntry(id, normalizedEmail, now);
        entry.RaiseDomainEvent(new WaitlistEntryRequestedDomainEvent(id, normalizedEmail, now));
        return Result.Success(entry);
    }

    public Result Approve(
        Guid approvedByAdminId,
        InvitationId resultingInvitationId,
        IDateTimeProvider clock)
    {
        if (approvedByAdminId == Guid.Empty)
            return Result.Failure(
                DomainError.Validation("WaitlistEntry.ApprovedByAdminIdRequired",
                    "ApprovedByAdminId krävs."));

        if (resultingInvitationId == default)
            return Result.Failure(
                DomainError.Validation("WaitlistEntry.ResultingInvitationIdRequired",
                    "ResultingInvitationId krävs."));

        if (Status != WaitlistStatus.Pending)
            return Result.Failure(
                DomainError.Conflict("WaitlistEntry.NotPending",
                    $"Kan inte godkänna väntelistepost i status {Status.Name}."));

        Status = WaitlistStatus.Approved;
        ApprovedAt = clock.UtcNow;
        ApprovedByAdminId = approvedByAdminId;
        ResultingInvitationId = resultingInvitationId;
        RaiseDomainEvent(new WaitlistEntryApprovedDomainEvent(
            Id, approvedByAdminId, resultingInvitationId, clock.UtcNow));
        return Result.Success();
    }

    public Result Reject(Guid rejectedByAdminId, IDateTimeProvider clock)
    {
        if (rejectedByAdminId == Guid.Empty)
            return Result.Failure(
                DomainError.Validation("WaitlistEntry.RejectedByAdminIdRequired",
                    "RejectedByAdminId krävs."));

        if (Status != WaitlistStatus.Pending)
            return Result.Failure(
                DomainError.Conflict("WaitlistEntry.NotPending",
                    $"Kan inte avvisa väntelistepost i status {Status.Name}."));

        Status = WaitlistStatus.Rejected;
        RejectedAt = clock.UtcNow;
        RejectedByAdminId = rejectedByAdminId;
        RaiseDomainEvent(new WaitlistEntryRejectedDomainEvent(Id, rejectedByAdminId, clock.UtcNow));
        return Result.Success();
    }
}
