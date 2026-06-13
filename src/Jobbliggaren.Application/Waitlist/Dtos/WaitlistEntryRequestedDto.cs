using Jobbliggaren.Domain.Waitlist;

namespace Jobbliggaren.Application.Waitlist.Dtos;

public sealed record WaitlistEntryRequestedDto(Guid WaitlistEntryId, string Email)
{
    public static WaitlistEntryRequestedDto From(WaitlistEntry entry) =>
        new(entry.Id.Value, entry.Email);
}
