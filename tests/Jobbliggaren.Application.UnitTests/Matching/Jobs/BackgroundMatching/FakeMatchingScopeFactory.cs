using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Application.UnitTests.Matching.Jobs.BackgroundMatching;

/// <summary>
/// A concrete fake for the DI scope chain of <c>BackgroundMatchingJob</c> (#751, a child scope per
/// user) and <c>TopMatchMailDispatcher</c>. Every scope hands out the SAME context and collaborators,
/// so a test seeds and asserts against one context. <see cref="ScopesCreated"/> and
/// <see cref="Resolutions{T}"/> make the scope structure observable (parity
/// <c>SyncPlatsbankenSnapshotJobTests.FakeScopeFactory</c>).
/// </summary>
internal sealed class FakeMatchingScopeFactory(
    Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
    IMatchProfileBuilder profileBuilder,
    IMatchScorer scorer,
    IUserAccountService userAccounts)
    : IServiceScopeFactory, IServiceScope, IServiceProvider
{
    private readonly Dictionary<Type, int> _resolutions = [];

    public int ScopesCreated { get; private set; }

    public int Resolutions<T>() => _resolutions.GetValueOrDefault(typeof(T));

    public IServiceScope CreateScope()
    {
        ScopesCreated++;
        return this;
    }

    public IServiceProvider ServiceProvider => this;

    public object? GetService(Type serviceType)
    {
        object? service =
            serviceType == typeof(IAppDbContext) ? db
            : serviceType == typeof(IMatchProfileBuilder) ? profileBuilder
            : serviceType == typeof(IMatchScorer) ? scorer
            : serviceType == typeof(IUserAccountService) ? userAccounts
            : null;
        if (service is not null)
            _resolutions[serviceType] = _resolutions.GetValueOrDefault(serviceType) + 1;
        return service;
    }

    // Shared instances: the test owns their lifetime, not the scope.
    public void Dispose()
    {
    }
}
