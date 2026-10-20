using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>
/// Registers the provider reads (#1981): the allowance figure's source (ADR 0178), the delivery statistics and the
/// recent failures, each authenticated with the read application's own key (<see cref="ScalewayReadOptions"/>), never
/// the sending key. Called from <c>AddInfrastructure</c>, the API's entry point. The Worker never calls it, so it never
/// reads the provider. <see cref="ScalewayReadSelection"/> decides the branch once, from raw configuration.
/// </summary>
public static class EmailUsageSourceCollectionExtensions
{
    private const string ProjectIdKey = $"{ScalewayEmailOptions.SectionName}:{nameof(ScalewayEmailOptions.ProjectId)}";
    private const string RegionKey = $"{ScalewayEmailOptions.SectionName}:{nameof(ScalewayEmailOptions.Region)}";

    public static IServiceCollection AddEmailUsageSource(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailAllowanceOptions>()
            .Bind(configuration.GetSection(EmailAllowanceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        switch (ScalewayReadSelection.Select(configuration))
        {
            case ScalewayReadState.Configured:
                services.AddOptions<ScalewayReadOptions>()
                    .Bind(configuration.GetSection(ScalewayReadOptions.SectionName))
                    .PostConfigure(options => options.ProjectId = configuration[ProjectIdKey] ?? string.Empty)
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                services.AddScalewayUsageClient(configuration[RegionKey] ?? string.Empty);
                services.AddSingleton<ScalewayReadTransport>();
                services.AddSingleton<IProviderUsageAttemptSource, ScalewayUsageSource>();
                services.AddSingleton<IEmailProviderUsageSource, MemoizedEmailProviderUsageSource>();
                services.AddSingleton<IDeliveryStatisticsAttemptSource, ScalewayDeliveryStatisticsSource>();
                services.AddSingleton<IEmailDeliveryStatisticsSource, MemoizedEmailDeliveryStatisticsSource>();
                services.AddSingleton<IRecentFailuresAttemptSource, ScalewayRecentFailuresSource>();
                services.AddSingleton<IEmailRecentFailuresSource, MemoizedEmailRecentFailuresSource>();
                break;

            case ScalewayReadState.ReadKeyMissing:
                AddEveryReadPort(services, new ReadKeyMissingEmailProviderReads());
                services.AddHostedService<ReadKeyMissingNotice>();
                break;

            default:
                AddEveryReadPort(services, new NotConfiguredEmailProviderReads());
                break;
        }

        return services;
    }

    /// <summary>One instance behind every read port, for the branches where nothing is read.</summary>
    private static void AddEveryReadPort<TReads>(IServiceCollection services, TReads reads)
        where TReads : class, IEmailProviderUsageSource, IEmailDeliveryStatisticsSource, IEmailRecentFailuresSource
    {
        services.AddSingleton<IEmailProviderUsageSource>(reads);
        services.AddSingleton<IEmailDeliveryStatisticsSource>(reads);
        services.AddSingleton<IEmailRecentFailuresSource>(reads);
    }
}

/// <summary>Which provider reads a deployment gets.</summary>
internal enum ScalewayReadState
{
    /// <summary><c>Email:Provider</c> is not Scaleway: there is no provider to ask.</summary>
    NotConfigured,

    /// <summary>Scaleway sends, and no read key is configured.</summary>
    ReadKeyMissing,

    /// <summary>Scaleway sends, and a read key of its own is configured.</summary>
    Configured,
}

/// <summary>
/// The one reading of whether the provider reads may run, from raw configuration at registration (the
/// <c>AddEmailSender</c> idiom). A read key that is present but unusable fails the boot, naming the key and never a
/// value: an empty secret file must not read as "not configured", and a pasted sending key must not read as a read key.
/// </summary>
internal static class ScalewayReadSelection
{
    internal const string ReadKeyName = $"{ScalewayReadOptions.SectionName}:{nameof(ScalewayReadOptions.SecretKey)}";

    private const string SendKeyName = $"{ScalewayEmailOptions.SectionName}:{nameof(ScalewayEmailOptions.SecretKey)}";

    internal static ScalewayReadState Select(IConfiguration configuration)
    {
        if (!EmailProviderSelection.IsScaleway(EmailProviderSelection.ConfiguredProvider(configuration)))
        {
            return ScalewayReadState.NotConfigured;
        }

        var readKey = configuration[ReadKeyName];
        if (readKey is null)
        {
            return ScalewayReadState.ReadKeyMissing;
        }

        if (string.IsNullOrWhiteSpace(readKey))
        {
            throw new InvalidOperationException(
                $"{ReadKeyName} is set but blank. Remove it to run without the provider reads, or set the read application's key.");
        }

        if (string.Equals(readKey, configuration[SendKeyName], StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{ReadKeyName} equals {SendKeyName}. The provider reads need the read application's own key, never the sending key.");
        }

        return ScalewayReadState.Configured;
    }
}
