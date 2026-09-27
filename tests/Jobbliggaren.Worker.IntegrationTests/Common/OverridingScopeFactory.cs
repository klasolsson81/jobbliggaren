using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Worker.IntegrationTests.Common;

/// <summary>
/// Wraps the fixture's REAL root <see cref="IServiceScopeFactory"/>: each child scope resolves
/// everything from a real DI scope EXCEPT the service types in <c>overrides</c>, which resolve to the
/// test's own instances. Lets a job run its production per-user child-scope path against real
/// Postgres while chosen collaborators are controlled.
/// </summary>
internal sealed class OverridingScopeFactory(
    IServiceScopeFactory inner,
    IReadOnlyDictionary<Type, object> overrides) : IServiceScopeFactory
{
    public IServiceScope CreateScope() => new OverridingScope(inner.CreateScope(), overrides);

    private sealed class OverridingScope(
        IServiceScope innerScope,
        IReadOnlyDictionary<Type, object> overrides) : IServiceScope, IServiceProvider, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType) =>
            overrides.TryGetValue(serviceType, out var instance)
                ? instance
                : innerScope.ServiceProvider.GetService(serviceType);

        public void Dispose() => innerScope.Dispose();

        public async ValueTask DisposeAsync()
        {
            if (innerScope is IAsyncDisposable ad)
                await ad.DisposeAsync();
            else
                innerScope.Dispose();
        }
    }
}
