using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Security;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Applications.Commands.CreateApplicationFromJobAd;

/// <summary>
/// F6 P5 Punkt 2 Del B — quick-create av Application från en existerande
/// JobAd-rad ("Har ansökt"-flödet från modal-footer). Separat command från
/// <see cref="JobbPilot.Application.Applications.Commands.CreateApplication.CreateApplicationCommand"/>
/// per CTO Val 3 (SRP — distinkt precondition: JobAd måste finnas + ej
/// arkiverad). Genererar audit-event "Application.CreatedFromJobAd" så
/// audit-trail skiljer manuella ansökningar från quick-create-flödet.
///
/// Implementerar <see cref="IRequiresFieldEncryptionKey"/> paritet
/// CreateApplicationCommand (ADR 0049 Mekanik-not 3 — pre-handler-prefetch).
/// </summary>
public sealed record CreateApplicationFromJobAdCommand(Guid JobAdId)
    : ICommand<Result<Guid>>, IAuthenticatedRequest,
      IAuditableCommand<Result<Guid>>, IRequiresFieldEncryptionKey
{
    public string EventType => "Application.CreatedFromJobAd";
    public string AggregateType => "Application";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
