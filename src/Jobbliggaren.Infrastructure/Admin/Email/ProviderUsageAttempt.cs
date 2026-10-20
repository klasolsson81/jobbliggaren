using Jobbliggaren.Application.Admin.Email;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>
/// One read of the provider as the adapter saw it: the result the port returns, plus the provider's
/// <c>Retry-After</c> when it sent one, which only <see cref="ReadMemo{TRead}"/> needs and the port
/// does not carry.
/// </summary>
internal sealed record ProviderAttempt<TRead>(TRead Read, TimeSpan? RetryAfter = null);

/// <summary>The adapter behind the memo. Infrastructure-internal: Application sees only <see cref="IEmailProviderUsageSource"/>.</summary>
internal interface IProviderUsageAttemptSource
{
    /// <summary>Never throws for a refusal, a timeout or an unreadable body; those are reasons on the result.</summary>
    ValueTask<ProviderAttempt<EmailProviderUsageRead>> AttemptUsageAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="AttemptUsageAsync"/>
    ValueTask<ProviderAttempt<EmailProviderPlanRead>> AttemptPlanAsync(CancellationToken cancellationToken);
}
