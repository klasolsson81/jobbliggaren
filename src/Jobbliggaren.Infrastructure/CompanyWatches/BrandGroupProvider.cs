using Jobbliggaren.Application.CompanyWatches.Abstractions;

namespace Jobbliggaren.Infrastructure.CompanyWatches;

/// <summary>
/// #311 PR-5 (ADR 0087 D4) — the <see cref="IBrandGroupProvider"/> implementation: one immutable
/// catalogue loaded once from the embedded, versioned brand-group dataset. Registered as an INSTANCE
/// in DI (the composition root calls <see cref="BrandGroupLoader"/> eagerly), so a malformed asset
/// kills host build instead of the first request — the <c>CriterionReferenceProvider</c> /
/// <c>BranschgruppProvider</c> fail-loud precedent (<c>AddSingleton&lt;IPort, Impl&gt;()</c> is lazy
/// and would defer the crash).
/// </summary>
internal sealed class BrandGroupProvider(BrandGroupCatalog catalog) : IBrandGroupProvider
{
    public BrandGroupCatalog Catalog { get; } =
        catalog ?? throw new ArgumentNullException(nameof(catalog));
}
