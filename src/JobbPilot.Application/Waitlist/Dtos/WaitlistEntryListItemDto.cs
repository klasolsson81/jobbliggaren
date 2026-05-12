using JobbPilot.Domain.Waitlist;

namespace JobbPilot.Application.Waitlist.Dtos;

public sealed record WaitlistEntryListItemDto(
    Guid WaitlistEntryId,
    string Email,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? RejectedAt,
    Guid? ResultingInvitationId)
{
    public static WaitlistEntryListItemDto From(WaitlistEntry w) =>
        new(w.Id.Value, w.Email, w.Status.Name, w.RequestedAt,
            w.ApprovedAt, w.RejectedAt, w.ResultingInvitationId?.Value);
}
