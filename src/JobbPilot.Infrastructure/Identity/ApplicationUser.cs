using Microsoft.AspNetCore.Identity;

namespace JobbPilot.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    // Public setters here follow IdentityUser<T> convention — ApplicationUser is
    // an Identity framework entity, not a domain aggregate. CLAUDE.md §2.2
    // (private setters) applies to domain aggregates in src/JobbPilot.Domain/.
    public AuthProvider Provider { get; set; } = AuthProvider.Local;
    public string? ProviderUserId { get; set; }
}
