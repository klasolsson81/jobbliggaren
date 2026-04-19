using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace JobbPilot.Application.Common;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<AssemblyMarker>();
        // Mediator + pipeline behaviors registreras i composition roots (Api/Worker)
        return services;
    }
}
