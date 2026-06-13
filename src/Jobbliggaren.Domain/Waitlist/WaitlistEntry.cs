using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Invitations;
using Jobbliggaren.Domain.Waitlist.Events;

namespace Jobbliggaren.Domain.Waitlist;

public sealed class WaitlistEntry : AggregateRoot<WaitlistEntryId>
{
    public const int NameMinLength = 1;
    public const int NameMaxLength = 100;
    public const int MotivationMinLength = 10;
    public const int MotivationMaxLength = 1000;

    public string Email { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Motivation { get; private set; } = null!;
    public AcceptanceSnapshot Acceptance { get; private set; } = null!;
    public DateTimeOffset RequestedAt { get; private set; }
    public WaitlistStatus Status { get; private set; } = null!;
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? ApprovedByAdminId { get; private set; }
    public InvitationId? ResultingInvitationId { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }
    public Guid? RejectedByAdminId { get; private set; }

    /// <summary>Sentinel-värde för Name efter <see cref="Reject"/>. Se XML-doc på metoden.</summary>
    public const string ErasedNameSentinel = "(raderad)";

    /// <summary>Sentinel-värde för Motivation efter <see cref="Reject"/>.</summary>
    public const string ErasedMotivationSentinel = "(raderad)";

    /// <summary>PrivacyPolicyVersion-marker som signalerar att AcceptanceSnapshot är erased.</summary>
    public const string ErasedAcceptanceVersion = "erased";

    // EF Core constructor
    private WaitlistEntry() { }

    private WaitlistEntry(
        WaitlistEntryId id,
        string email,
        string name,
        string motivation,
        AcceptanceSnapshot acceptance,
        DateTimeOffset requestedAt) : base(id)
    {
        Email = email;
        Name = name;
        Motivation = motivation;
        Acceptance = acceptance;
        RequestedAt = requestedAt;
        Status = WaitlistStatus.Pending;
    }

    public static Result<WaitlistEntry> Request(
        string? email,
        string? name,
        string? motivation,
        AcceptanceSnapshot acceptance,
        IDateTimeProvider clock)
    {
        var validation = ValidateInput(email, name, motivation, acceptance);
        if (validation.IsFailure)
            return Result.Failure<WaitlistEntry>(validation.Error);

        var now = clock.UtcNow;
        var id = WaitlistEntryId.New();
        var entry = new WaitlistEntry(
            id,
            NormalizeEmail(email!),
            name!.Trim(),
            motivation!.Trim(),
            acceptance,
            now);
        entry.RaiseDomainEvent(new WaitlistEntryRequestedDomainEvent(id, entry.Email, now));
        return Result.Success(entry);
    }

    /// <summary>
    /// Uppdaterar en pending entry vid re-signup. GDPR Art. 7(3): nyaste
    /// acceptance gäller. <see cref="RequestedAt"/> bevaras (FIFO-position
    /// i kön behålls). Raisar <see cref="WaitlistEntryRefreshedDomainEvent"/>
    /// som audit-trail-signal.
    /// </summary>
    public Result RefreshRequest(
        string? name,
        string? motivation,
        AcceptanceSnapshot acceptance,
        IDateTimeProvider clock)
    {
        if (Status != WaitlistStatus.Pending)
            return Result.Failure(
                DomainError.Conflict("WaitlistEntry.NotPending",
                    $"Kan inte uppdatera väntelistepost i status {Status.Name}."));

        var validation = ValidateInput(Email, name, motivation, acceptance);
        if (validation.IsFailure)
            return Result.Failure(validation.Error);

        Name = name!.Trim();
        Motivation = motivation!.Trim();
        Acceptance = acceptance;
        RaiseDomainEvent(new WaitlistEntryRefreshedDomainEvent(Id, clock.UtcNow));
        return Result.Success();
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

        // GDPR Art. 5(1)(c) data-minimization + Art. 17 right-to-erasure:
        // rättslig grund för Name + Motivation + Marketing-consent upphör
        // när admin avvisar. Bevarat: Email (audit-bevis "vem fick avslag"),
        // RejectedAt, RejectedByAdminId, AcceptedAt (audit "när skickade de in").
        // CTO-dom 2026-05-24 Fynd 2 Approach A.
        Name = ErasedNameSentinel;
        Motivation = ErasedMotivationSentinel;
        Acceptance = new AcceptanceSnapshot(
            MarketingEmailAccepted: false,
            AcceptedAt: Acceptance.AcceptedAt,
            PrivacyPolicyVersion: ErasedAcceptanceVersion);

        RaiseDomainEvent(new WaitlistEntryRejectedDomainEvent(Id, rejectedByAdminId, clock.UtcNow));
        return Result.Success();
    }

    private static Result ValidateInput(
        string? email,
        string? name,
        string? motivation,
        AcceptanceSnapshot acceptance)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure(
                DomainError.Validation("WaitlistEntry.EmailRequired", "E-postadress krävs."));

        if (email.Length > 254)
            return Result.Failure(
                DomainError.Validation("WaitlistEntry.EmailTooLong",
                    "E-postadress får vara max 254 tecken."));

        if (!email.Contains('@'))
            return Result.Failure(
                DomainError.Validation("WaitlistEntry.EmailInvalid",
                    "E-postadressen är inte giltig."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(
                DomainError.Validation("WaitlistEntry.NameRequired", "Namn krävs."));

        var trimmedName = name.Trim();
        if (trimmedName.Length < NameMinLength || trimmedName.Length > NameMaxLength)
            return Result.Failure(
                DomainError.Validation("WaitlistEntry.NameInvalidLength",
                    $"Namn ska vara {NameMinLength}–{NameMaxLength} tecken."));

        if (string.IsNullOrWhiteSpace(motivation))
            return Result.Failure(
                DomainError.Validation("WaitlistEntry.MotivationRequired",
                    "Motivering krävs."));

        var trimmedMotivation = motivation.Trim();
        if (trimmedMotivation.Length < MotivationMinLength ||
            trimmedMotivation.Length > MotivationMaxLength)
            return Result.Failure(
                DomainError.Validation("WaitlistEntry.MotivationInvalidLength",
                    $"Motivering ska vara {MotivationMinLength}–{MotivationMaxLength} tecken."));

        if (string.IsNullOrWhiteSpace(acceptance.PrivacyPolicyVersion))
            return Result.Failure(
                DomainError.Validation("WaitlistEntry.PrivacyPolicyVersionRequired",
                    "Privacy policy-version krävs."));

        return Result.Success();
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
