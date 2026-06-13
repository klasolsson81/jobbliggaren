using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Waitlist.Commands.RejectWaitlistEntry;

/// <summary>
/// Admin avvisar en pending waitlist-post. Inga email skickas till avvisade
/// (medveten silent-rejection per civic-utility-design — användaren får inte
/// negativ feedback om ett internt admin-val).
/// </summary>
public sealed record RejectWaitlistEntryCommand(Guid WaitlistEntryId)
    : ICommand<Result>, IAdminRequest;
