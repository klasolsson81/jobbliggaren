using JobbPilot.Domain.Waitlist;

namespace JobbPilot.Application.Waitlist.Dtos;

public sealed record WaitlistEntryRequestedDto(Guid WaitlistEntryId, string Email)
{
    public static WaitlistEntryRequestedDto From(WaitlistEntry entry) =>
        new(entry.Id.Value, entry.Email);
}
