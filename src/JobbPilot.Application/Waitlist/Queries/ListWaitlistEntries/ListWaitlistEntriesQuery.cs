using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Waitlist.Dtos;
using Mediator;

namespace JobbPilot.Application.Waitlist.Queries.ListWaitlistEntries;

/// <summary>
/// Admin-listning av waitlist-poster. Filter via <paramref name="Status"/>
/// (Pending|Approved|Rejected). Returnerar äldsta först — Pending som
/// väntat längst har högst prioritet vid manuell approval.
/// </summary>
public sealed record ListWaitlistEntriesQuery(string? Status)
    : IQuery<IReadOnlyList<WaitlistEntryListItemDto>>, IAdminRequest;
