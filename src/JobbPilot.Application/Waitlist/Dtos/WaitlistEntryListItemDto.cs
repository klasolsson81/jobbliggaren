using JobbPilot.Domain.Waitlist;

namespace JobbPilot.Application.Waitlist.Dtos;

/// <summary>
/// Admin-listning av väntelisteposter. Innehåller Name + Motivation så admin
/// kan fatta approve/reject-beslut utan extra fetch. AcceptanceSnapshot
/// exponeras inte som default i listan — kan hämtas via separat detail-query
/// om behov uppstår (audit-export).
/// </summary>
public sealed record WaitlistEntryListItemDto(
    Guid WaitlistEntryId,
    string Email,
    string Name,
    string Motivation,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? RejectedAt,
    Guid? ResultingInvitationId,
    bool MarketingEmailAccepted)
{
    public static WaitlistEntryListItemDto From(WaitlistEntry w) =>
        new(w.Id.Value, w.Email, w.Name, w.Motivation, w.Status.Name, w.RequestedAt,
            w.ApprovedAt, w.RejectedAt, w.ResultingInvitationId?.Value,
            w.Acceptance.MarketingEmailAccepted);
}
