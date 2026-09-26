using Jobbliggaren.Application.CompanyWatches.Abstractions;

namespace Jobbliggaren.Infrastructure.CompanyRegister.Reference;

/// <summary>
/// #560 PR-3 (CTO Fork G2) — the <see cref="ICriterionReferenceProvider"/> implementation: two
/// immutable catalogs loaded once from the embedded, versioned SCB datasets. Registered as an
/// INSTANCE in DI (the composition root calls <see cref="CriterionReferenceLoader"/> eagerly), so a
/// malformed asset kills host build instead of the first request — the <c>BranschgruppProvider</c>
/// fail-loud precedent (<c>AddSingleton&lt;IPort, Impl&gt;()</c> is lazy and would defer the crash).
/// </summary>
internal sealed class CriterionReferenceProvider : ICriterionReferenceProvider
{
    public CriterionReferenceProvider(
        SniReferenceCatalog sni,
        KommunReferenceCatalog kommuner,
        SniAliasCatalog aliases)
    {
        Sni = sni ?? throw new ArgumentNullException(nameof(sni));
        Kommuner = kommuner ?? throw new ArgumentNullException(nameof(kommuner));
        Aliases = aliases ?? throw new ArgumentNullException(nameof(aliases));

        // The two cross-dataset checks the alias loader cannot make on its own — it sees one
        // stream. Both fail LOUD here, at host build, for the same reason every other check in this
        // pair does: an alias pointing at a code the catalog does not have would surface a picker
        // row for a concept that cannot be selected, which is the vacuous-filter failure mode.
        if (!string.Equals(aliases.SniVersion, sni.Version, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Alias-assetet är byggt mot SNI '{aliases.SniVersion}' men katalogen är '{sni.Version}'. "
                + "Kör om tools/sni-aliases/generate.mjs mot den nya katalogen.");

        var known = sni.Sections.Select(static s => s.Code)
            .Concat(sni.Divisions.Select(static d => d.Code))
            .Concat(sni.Leaves.Select(static l => l.Code))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var alias in aliases.Aliases)
        {
            if (!known.Contains(alias.Code))
                throw new InvalidOperationException(
                    $"Alias för '{alias.Code}' ({alias.Source}) pekar på en kod som inte finns i SNI {sni.Version}.");
        }
    }

    public SniReferenceCatalog Sni { get; }

    public KommunReferenceCatalog Kommuner { get; }

    public SniAliasCatalog Aliases { get; }
}
