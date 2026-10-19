using Jobbliggaren.Application.Admin.Email;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>
/// The source when <c>Email:Provider</c> is not Scaleway: dev, CI and the test hosts. There is no provider to ask,
/// so the answer is a state and never a failure, and nothing is read.
/// </summary>
internal sealed class NotConfiguredEmailProviderUsageSource : IEmailProviderUsageSource
{
    private static readonly EmailProviderUsageRead NoUsage = new EmailProviderUsageRead.NotConfigured();
    private static readonly EmailProviderPlanRead NoPlan = new EmailProviderPlanRead.NotConfigured();

    public ValueTask<EmailProviderUsageRead> ReadUsageAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(NoUsage);

    public ValueTask<EmailProviderPlanRead> ReadPlanAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(NoPlan);
}
