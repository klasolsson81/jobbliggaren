using Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackSummary;
using Jobbliggaren.Application.Feedback;
using Jobbliggaren.Infrastructure.Admin.Feedback;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Infrastructure.Feedback;

internal static class FeedbackServiceCollectionExtensions
{
    /// <summary>
    /// #1979. Registered from <c>AddPersistence</c>, so the Api (submit, prompt state, admin) and the
    /// Worker (the notice dispatch) read the same switch and recipient. Deliberately no ValidateOnStart:
    /// a missing recipient keeps feedback closed and visible on the admin page, rather than stopping the
    /// only production host.
    /// </summary>
    public static IServiceCollection AddFeedback(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FeedbackOptions>().Bind(configuration.GetSection(FeedbackOptions.SectionName));
        services.AddScoped<FeedbackGate>();
        services.AddScoped<IFeedbackRatingSummaryReader, SqlFeedbackRatingSummaryReader>();
        return services;
    }
}
