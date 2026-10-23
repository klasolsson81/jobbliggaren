using Jobbliggaren.Application.Admin.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Infrastructure.Email.Counting;

/// <summary>
/// #1981 PR 3 (ADR 0180) — the Api's read of our own send counts. It chooses on the same provider switch the sender
/// does: only the Scaleway arm counts, so only there is there anything to read. The read key plays no part, because
/// these counts are ours and never the provider's.
/// </summary>
public static class EmailSendCountsSourceCollectionExtensions
{
    public static IServiceCollection AddEmailSendCountsSource(this IServiceCollection services, IConfiguration configuration)
    {
        if (EmailProviderSelection.IsScaleway(EmailProviderSelection.ConfiguredProvider(configuration)))
        {
            services.AddSingleton<IEmailSendCountsSource, SqlEmailSendCountsSource>();
        }
        else
        {
            services.AddSingleton<IEmailSendCountsSource>(new NotConfiguredEmailSendCounts());
        }

        return services;
    }
}
