using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>
/// Registers the allowance figure's source (#1981, ADR 0178). Called from <c>AddInfrastructure</c>, the API's entry
/// point, and after <c>AddEmailSender</c>, which binds and validates the Scaleway credentials this source reuses. The
/// Worker calls neither, so it never reads the provider.
/// </summary>
public static class EmailUsageSourceCollectionExtensions
{
    public static IServiceCollection AddEmailUsageSource(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailAllowanceOptions>()
            .Bind(configuration.GetSection(EmailAllowanceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (EmailProviderSelection.IsScaleway(EmailProviderSelection.ConfiguredProvider(configuration)))
        {
            var region = configuration[$"{ScalewayEmailOptions.SectionName}:{nameof(ScalewayEmailOptions.Region)}"];
            services.AddScalewayUsageClient(region ?? string.Empty);
            services.AddSingleton<IProviderUsageAttemptSource, ScalewayUsageSource>();
            services.AddSingleton<IEmailProviderUsageSource, MemoizedEmailProviderUsageSource>();
        }
        else
        {
            services.AddSingleton<IEmailProviderUsageSource, NotConfiguredEmailProviderUsageSource>();
        }

        return services;
    }
}
