using JobbPilot.Application.Common.Security;
using JobbPilot.Domain.JobSeekers;

namespace JobbPilot.Infrastructure.Security;

/// <summary>
/// TD-13 — scoped impl av <see cref="ICurrentDataOwner"/>. En instans per
/// DI-scope (request/Hangfire-job). Sätts en gång av
/// <c>FieldEncryptionKeyPrefetchBehavior</c>; ingen static/ambient state.
/// </summary>
public sealed class CurrentDataOwner : ICurrentDataOwner
{
    public JobSeekerId? JobSeekerId { get; private set; }

    public void SetOwner(JobSeekerId jobSeekerId) => JobSeekerId = jobSeekerId;
}
