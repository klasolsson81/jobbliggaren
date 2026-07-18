using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Application.UnitTests.Matching.Jobs.BackgroundMatching;

/// <summary>
/// Konkret fake för DI-scope-kedjan (#751 — child scope per user). Jobbet anropar
/// <c>scopeFactory.CreateAsyncScope()</c>, som via <see cref="IServiceScopeFactory.CreateScope"/>
/// wrappar resultatet i en <c>AsyncServiceScope</c>. Varje scope returnerar SAMMA
/// AppDbContext/collaborators — testerna seedar och asserterar mot EN context, precis som före
/// refactorn, så alla befintliga testkroppar är orörda (konstruktions-only-migration).
/// Delad av <see cref="BackgroundMatchingJobTests"/> och
/// <see cref="BackgroundMatchingJobTopDirectTests"/> (CTO-ruling 2026-07-18: en delad fil för två
/// syskon-konsumenter, ej promotad till TestSupport). Antal skapade scopes räknas via
/// <see cref="ScopesCreated"/> (regressions-assertion — paritet
/// <c>SyncPlatsbankenSnapshotJobTests.FakeScopeFactory</c>). "En liten konkret fake är mer läsbar
/// än en djup NSubstitute-kedja" (CLAUDE.md §2.4).
/// </summary>
internal sealed class FakeMatchingScopeFactory(
    Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
    IMatchProfileBuilder profileBuilder,
    IMatchScorer scorer,
    IUserAccountService userAccounts)
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
        serviceType == typeof(IAppDbContext) ? db
        : serviceType == typeof(IMatchProfileBuilder) ? profileBuilder
        : serviceType == typeof(IMatchScorer) ? scorer
        : serviceType == typeof(IUserAccountService) ? userAccounts
        : null;

    // Delade instanser — livslängden ägs av testet (db disposas där), inte av scopen.
    public void Dispose()
    {
    }
}
