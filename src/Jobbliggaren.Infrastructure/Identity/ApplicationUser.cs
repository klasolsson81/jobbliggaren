using Microsoft.AspNetCore.Identity;

namespace Jobbliggaren.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    // Public setters here follow IdentityUser<T> convention — ApplicationUser is
    // an Identity framework entity, not a domain aggregate. CLAUDE.md §2.2
    // (private setters) applies to domain aggregates in src/Jobbliggaren.Domain/.
    public AuthProvider Provider { get; set; } = AuthProvider.Local;
    public string? ProviderUserId { get; set; }

    /// <summary>
    /// When this Identity user row was created. DB-stamped via a <c>now()</c> store
    /// default (see <c>ApplicationUserConfiguration</c>) — <c>UserManager.CreateAsync</c>
    /// inserts without setting it, so registration needs no extra wiring. Consumed by the
    /// orphan-sweep grace window (#508 / ADR 0024 D6): registration commits the Identity
    /// user first and the JobSeeker later (two-boundary), so an Identity user younger than
    /// the grace window with no JobSeeker is presumed mid-registration and is never swept as
    /// an orphan. A test may set a non-default value before <c>CreateAsync</c> to control the
    /// age (the store default only fills the CLR sentinel <c>default(DateTimeOffset)</c>).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
