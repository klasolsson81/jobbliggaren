using Jobbliggaren.Application.Companies.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Infrastructure.CompanyRegistry;

/// <summary>
/// #454 (ADR 0088 D3) — the deterministic dev/test provider behind the port while the real SCB
/// adapter is a follow-up (Klas phasing 2026-07-02). Registered ONLY in Development/Test (allow-list
/// gate in <c>AddCompanyRegistry</c>, mirror <c>ConsoleEmailSender</c>). The table is a handful of
/// REAL public legal-entity org.nrs (publicly registered companies — same public-data class as the
/// ad corpus) so dev E2E reads naturally, plus one personnummer-shaped entry so the D4
/// refuse-posture is E2E-exercisable: the handler must refuse BEFORE this provider is invoked — the
/// entry existing HERE proves a leak would be visible (the transmission-fail-closed test pins that
/// this class never receives it). Everything else → <see cref="CompanyRegistryLookup.NotFound"/>.
/// Never logs the org.nr.
/// </summary>
internal sealed class FakeCompanyRegistry : ICompanyRegistry
{
    // Deterministic fixture table. Keys are verbatim 10-digit org.nrs (no hyphen — VO form).
    // Concrete Dictionary type (CA1859) — private and never exposed past the class boundary.
    private static readonly Dictionary<string, string> Entries =
        new(StringComparer.Ordinal)
        {
            // Real public legal entities (Bolagsverket-registered; public register data).
            ["5560125790"] = "Volvo Aktiebolag",
            ["5560360793"] = "Ericsson AB",
            ["5565021846"] = "Spotify AB",
            // A legal entity with (almost certainly) zero ads in the local corpus — the 0-ad
            // story the feature exists for ("0 aktiva annonser just nu — bevaka ändå").
            ["5560001712"] = "AB Stockholmshem",
            // Personnummer-shaped (3rd digit < 2 ⇒ enskild-firma/personnummer space). The handler
            // REFUSES this upstream (D4); if it ever reaches this table the fail-closed tests catch
            // the transmission. A synthetic value (valid shape, not a real personnummer).
            ["1901012384"] = "Enskild firma (fixture)",
        };

    public ValueTask<CompanyRegistryLookup> LookupAsync(
        OrganizationNumber organizationNumber, CancellationToken cancellationToken)
    {
        var lookup = Entries.TryGetValue(organizationNumber.Value, out var name)
            ? CompanyRegistryLookup.Found(new CompanyRegistryEntry(organizationNumber.Value, name))
            : CompanyRegistryLookup.NotFound;
        return ValueTask.FromResult(lookup);
    }
}
