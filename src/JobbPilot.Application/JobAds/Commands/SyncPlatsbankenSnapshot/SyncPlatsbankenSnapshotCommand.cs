using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.JobAds.Commands.SyncPlatsbankenSnapshot;

/// <summary>
/// Admin-triggerad synkron snapshot-import från Platsbanken via JobTech-API.
/// P8b leverans (ADR 0032 §9). Sync-flöde i P8c sker via Hangfire — denna
/// command är primärt avsedd för smoke-test efter deploy.
/// </summary>
public sealed record SyncPlatsbankenSnapshotCommand
    : ICommand<Result<SyncPlatsbankenSnapshotResult>>, IAdminRequest;
