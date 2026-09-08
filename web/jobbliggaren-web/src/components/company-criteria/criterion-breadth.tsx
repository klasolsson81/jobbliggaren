import { useTranslations } from "next-intl";

// Parity `criterion-row.tsx` / `criteria-summary.tsx`: the middle dot joins the two axes and is a
// layout glyph, not copy. It stays inside the rendered string rather than becoming a separate node,
// exactly as the delivered catalogue row has it — a separator promoted to an element would need an
// aria treatment the catalogue never gave it, and the two rows must read alike.
const SEPARATOR = " · ";

interface CriterionBreadthProps {
  /** The criterion's SNI axis, LEAF codes as the wire carries them. */
  readonly sniCodes: ReadonlyArray<string>;
  /** The criterion's kommun axis. */
  readonly municipalityCodes: ReadonlyArray<string>;
}

/**
 * How WIDE a branschbevakning is: "1 bransch · 1 kommun". One knowledge piece, one component, read
 * by all four surfaces that show a criterion — `design-reviewer`'s bound form for her B5
 * (`docs/reviews/2026-09-08-1706-b5-design-reviewer.md`, B-1 to B-7), the sibling of the
 * `CriterionAdLines` extraction and for the same reason: four inline copies of one expression is
 * the drift shape that extraction exists to close.
 *
 * <p>The label states coverage; this line states extent. Why the extent is not in the label instead:
 * that report, B-1.</p>
 *
 * <p><b>The count is the RAW leaf count, and that is bound rather than incidental.</b> The repo
 * carries a SECOND count — `decomposeSelection(...).length`, what the edit dialog shows — which
 * collapses a fully selected node to one option (`criterion-options.ts`, `allSelected`). Under that
 * rule the narrow watch and the whole huvudgrupp would BOTH read "1 bransch" and this component
 * would answer nothing. A future harmonisation toward the picker's semantics must therefore not
 * carry these surfaces with it without re-measuring the collision (#1711 owns that divergence).</p>
 *
 * <p>Rendered unconditionally wherever a criterion is shown — including under a user-set label, and
 * including when the reference tree degraded and the heading falls back to the neutral noun. In
 * that degraded case this line is the row's only factual content.</p>
 *
 * <p>Presentational and locale-only: no `role`, no `aria-live`, nothing focusable. Extent is a
 * property of the watch, not a status about it, and on `/oversikt` the line shares its `<li>` with
 * the name so it is announced with it (WCAG 1.3.1).</p>
 */
export function CriterionBreadth({ sniCodes, municipalityCodes }: CriterionBreadthProps) {
  const t = useTranslations("pages.foretag.criteria");

  return (
    <p className="jp-criterion-breadth">
      {t("row.branschCount", { count: sniCodes.length })}
      {SEPARATOR}
      {t("row.kommunCount", { count: municipalityCodes.length })}
    </p>
  );
}
