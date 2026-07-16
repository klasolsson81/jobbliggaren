using Jobbliggaren.Application.CompanyWatches.Abstractions;

namespace Jobbliggaren.Infrastructure.CompanyRegister.Reference;

/// <summary>
/// #560 PR-3 (CTO Fork G2) — the <see cref="ICriterionReferenceProvider"/> implementation: two
/// immutable catalogs loaded once from the embedded, versioned SCB datasets. Registered as an
/// INSTANCE in DI (the composition root calls <see cref="CriterionReferenceLoader"/> eagerly), so a
/// malformed asset kills host build instead of the first request — the <c>BranschgruppProvider</c>
/// fail-loud precedent (<c>AddSingleton&lt;IPort, Impl&gt;()</c> is lazy and would defer the crash).
/// </summary>
internal sealed class CriterionReferenceProvider(
    SniReferenceCatalog sni,
    KommunReferenceCatalog kommuner) : ICriterionReferenceProvider
{
    public SniReferenceCatalog Sni { get; } = sni ?? throw new ArgumentNullException(nameof(sni));

    public KommunReferenceCatalog Kommuner { get; } =
        kommuner ?? throw new ArgumentNullException(nameof(kommuner));
}
