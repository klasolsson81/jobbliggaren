using Microsoft.AspNetCore.Identity;

namespace Jobbliggaren.Infrastructure.Identity;

// Public setters here follow IdentityUser<T> convention — ApplicationUser is
// an Identity framework entity, not a domain aggregate. CLAUDE.md §2.2
// (private setters) applies to domain aggregates in src/Jobbliggaren.Domain/.
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public bool IsSuspended { get; private set; }

    public long AccessRevision { get; private set; }

    public long CredentialCutoff { get; private set; }

    internal void ChangeAccess(bool suspended, long epoch)
    {
        if (IsSuspended == suspended)
            throw new InvalidOperationException("The requested access state already holds.");
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(epoch, CredentialCutoff);

        AdvanceCredentials(epoch);
        IsSuspended = suspended;
    }

    internal void AdvanceCredentials(long epoch)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(epoch, CredentialCutoff);
        AccessRevision = checked(AccessRevision + 1);
        CredentialCutoff = epoch;
        SecurityStamp = Guid.NewGuid().ToString();
        ConcurrencyStamp = Guid.NewGuid().ToString();
    }

    /// <summary>
    /// When this Identity user row was created. DB-stamped via a <c>now()</c> store
    /// default (see <c>ApplicationUserConfiguration</c>) — <c>UserManager.CreateAsync</c>
    /// inserts without setting it, so registration needs no extra wiring. Consumed by the
    /// orphan-sweep grace window (#508 / ADR 0024 D6). Registration now commits Identity
    /// and profile atomically (ADR 0155); the grace remains for rows from the retired writer.
    /// A test may set a non-default value before <c>CreateAsync</c> to control the
    /// age (the store default only fills the CLR sentinel <c>default(DateTimeOffset)</c>).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
