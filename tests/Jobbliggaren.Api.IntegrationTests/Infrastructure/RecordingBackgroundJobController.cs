using System.Collections.Concurrent;
using Jobbliggaren.Application.Admin.BackgroundJobs;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// #204 / TD-83 PR2 — deterministic recording fake for <see cref="IBackgroundJobController"/> in Api
/// integration. Registered last-wins in <see cref="ApiFactory"/> (parity with
/// <see cref="RecordingEmailSender"/>) so the integration host never
/// composes the real <c>HangfireBackgroundJobController</c>.
/// <para>
/// This lets the audit / auth / outcome-mapping tests exercise the full Mediator pipeline (validation,
/// authorization, AuditBehavior, UnitOfWork) WITHOUT a bootstrapped Hangfire schema — <see cref="ApiFactory"/>
/// does NOT bootstrap the hangfire schema (Api runs <c>PrepareSchemaIfNecessary=false</c>; the Worker
/// owns schema bootstrap). The real adapter is a thin Hangfire wrapper covered manually in dev.
/// </para>
/// <para>
/// <see cref="TriggerRecurringAsync"/> echoes the id (matching the real adapter's contract).
/// <see cref="RequeueAsync"/> returns the settable <see cref="NextRequeueOutcome"/> (default
/// <see cref="RequeueOutcome.Requeued"/>) so a test can drive the 200 / 404 / 409 paths. Append-only
/// recording of trigger calls; thread-safe.
/// </para>
/// </summary>
internal sealed class RecordingBackgroundJobController : IBackgroundJobController
{
    private readonly ConcurrentQueue<string> _triggered = new();

    /// <summary>Every recurring-job id passed to <see cref="TriggerRecurringAsync"/> since host start.</summary>
    public IReadOnlyList<string> Triggered => [.. _triggered];

    /// <summary>
    /// The outcome <see cref="RequeueAsync"/> returns next. Settable so a single test can drive the
    /// Requeued (200) / JobNotFound (404) / NotInFailedState (409) mapping. Defaults to Requeued.
    /// </summary>
    public RequeueOutcome NextRequeueOutcome { get; set; } = RequeueOutcome.Requeued;

    public Task<string> TriggerRecurringAsync(string recurringJobId, CancellationToken ct)
    {
        _triggered.Enqueue(recurringJobId);
        return Task.FromResult(recurringJobId);
    }

    public Task<RequeueOutcome> RequeueAsync(string jobId, CancellationToken ct) =>
        Task.FromResult(NextRequeueOutcome);
}
