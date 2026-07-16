namespace Jobbliggaren.Application.CompanyWatches.Abstractions;

/// <summary>
/// #560 PR-3 (CTO Fork G2, 2026-07-16) — the SCB reference data a criteria-based company watch is
/// authored against: the SNI 2025 industry hierarchy and the current län/kommun list. ONE knowledge
/// authority for BOTH consumers — the Application existence-validator (an unknown-but-well-formed
/// code is a Validation failure, never stored) and the FE picker tree (served via
/// <c>GetCriterionReferenceQuery</c>) — so the picker can never offer a code the validator rejects,
/// and an SCB update touches exactly one dataset.
///
/// <para>
/// <b>Why existence validation at all (the A1 bind):</b> the Domain spec
/// (<c>CompanyWatchCriteriaSpec</c>) enforces FORMAT only (5-digit / 4-digit — framework-free, DIP).
/// A well-formed code that does not exist in SCB's universe ("99999") would be stored and then
/// <b>silently match nothing forever</b> in the register — this product's cardinal sin (the
/// vacuous-criterion failure the whole criteria wave exists to fight). Existence is knowledge about
/// an external taxonomy, so it lives behind this port (Application defines, Infrastructure loads the
/// embedded versioned dataset).
/// </para>
///
/// <para>
/// Implementations are immutable and loaded once at host build (INSTANCE-registered — a lazy
/// type-registration would defer a malformed-asset crash to the first request;
/// <c>BranschgruppProvider</c> precedent).
/// </para>
/// </summary>
public interface ICriterionReferenceProvider
{
    SniReferenceCatalog Sni { get; }

    KommunReferenceCatalog Kommuner { get; }
}
