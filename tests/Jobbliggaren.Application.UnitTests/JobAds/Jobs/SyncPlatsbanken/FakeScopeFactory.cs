using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Application.UnitTests.JobAds.Jobs.SyncPlatsbanken;

/// <summary>
/// Concrete fake for the DI-scope chain shared by the JobTech sync jobs' unit tests
/// (SnapshotJob and StreamJob both dispatch each item in its own child scope — #982 parity).
/// <c>scopeFactory.CreateAsyncScope()</c> is an extension that calls
/// <see cref="IServiceScopeFactory.CreateScope"/> and wraps the result in an
/// <c>AsyncServiceScope</c>. A small concrete fake is more readable than a deep NSubstitute chain
/// (CLAUDE.md §2.4) and lets a test count scope creations via <see cref="ScopesCreated"/>
/// (the per-item-scope regression assertion). The same <see cref="IMediator"/> instance is handed
/// out of every scope, so <c>mediator.Received()</c> assertions still hold.
/// </summary>
internal sealed class FakeScopeFactory(IMediator mediator)
    : IServiceScopeFactory, IServiceScope, IServiceProvider
{
    public int ScopesCreated { get; private set; }

    public IServiceScope CreateScope()
    {
        ScopesCreated++;
        return this;
    }

    public IServiceProvider ServiceProvider => this;

    public object? GetService(Type serviceType) =>
        serviceType == typeof(IMediator) ? mediator : null;

    public void Dispose() { }
}
