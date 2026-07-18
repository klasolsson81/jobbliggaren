namespace Jobbliggaren.Application.CompanyWatches.Abstractions;

/// <summary>
/// #311 PR-5 (ADR 0087 D4) — the curated brand-group catalogue behind a port: the follow handler
/// resolves a slug to a group (unknown → <c>DomainError.NotFound</c>), and the scan / list / status
/// read paths expand a group to its member org.nrs. ONE knowledge authority (Application defines,
/// Infrastructure loads the embedded versioned dataset), so an org.nr can only belong to a group
/// because a maintainer put it there in a reviewed PR — never by name inference (the Volvo×20 trap).
///
/// <para>
/// Implementations are immutable and loaded once at host build (INSTANCE-registered — a lazy
/// type-registration would defer a malformed-asset crash to the first request;
/// <c>CriterionReferenceProvider</c> / <c>BranschgruppProvider</c> precedent).
/// </para>
/// </summary>
public interface IBrandGroupProvider
{
    BrandGroupCatalog Catalog { get; }
}
