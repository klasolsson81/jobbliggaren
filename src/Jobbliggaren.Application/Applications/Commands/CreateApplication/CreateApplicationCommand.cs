using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Applications.Commands.CreateApplication;

public sealed record CreateApplicationCommand(
    Guid? JobAdId,
    string? CoverLetter,
    ManualPostingInput? Manual = null)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>,
      IRequiresFieldEncryptionKey
{
    public string EventType => "Application.Created";
    public string AggregateType => "Application";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}

/// <summary>
/// Manuell jobbmetadata för ansökningar utan JobAd-koppling. Ingen Source —
/// manuell ansökan är implicit Source=Manual (read-vägen projicerar literal).
/// </summary>
public sealed record ManualPostingInput(
    string? Title,
    string? Company,
    string? Url,
    DateTimeOffset? ExpiresAt);
