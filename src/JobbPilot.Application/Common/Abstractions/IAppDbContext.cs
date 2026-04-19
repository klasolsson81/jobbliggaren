using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Common.Abstractions;

/// <summary>
/// Application-side abstraction över EF Core DbContext. Exponerar DbSet&lt;T&gt;
/// per aggregate root. Medveten kompromiss per ADR 0009 — repository-pattern
/// ovanpå EF Core är ett anti-pattern. DbSet&lt;T&gt; är ett accepterat bridge-interface.
/// </summary>
public interface IAppDbContext
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
