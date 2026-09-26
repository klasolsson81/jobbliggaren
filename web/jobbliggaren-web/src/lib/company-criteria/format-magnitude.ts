import { formatNumber, type JpFormatter } from "@/lib/i18n/format";

/**
 * #560 PR-3 — render the honest magnitude of a criterion. The number is locale-grouped (sv: "10 000"
 * with a non-breaking space) and, when the count SATURATED at the product ceiling, suffixed with "+"
 * so the copy reads "10 000+" — never a bare ceiling number the register cannot stand behind (#859:
 * a rendered magnitude must be true). Shared by the picker's live preview and the browse headline so
 * the previewed number and the saved number render identically.
 */
export function formatMagnitude(
  format: JpFormatter,
  magnitude: { readonly magnitude: number; readonly saturated: boolean },
): string;
/**
 * #1681 part 2 (ADR 0139) — the criterion's AD magnitude may have no number at all (the watch is too
 * broad to materialise, or has not been materialised for its current predicate yet). That overload
 * returns `null` rather than a formatted zero, so a caller cannot render "0" for a count nobody took.
 * The company-side magnitude keeps the non-null overload above and is unaffected.
 */
export function formatMagnitude(
  format: JpFormatter,
  magnitude: { readonly magnitude: number | null; readonly saturated: boolean },
): string | null;
export function formatMagnitude(
  format: JpFormatter,
  magnitude: { readonly magnitude: number | null; readonly saturated: boolean },
): string | null {
  if (magnitude.magnitude === null) return null;
  const grouped = formatNumber(format, magnitude.magnitude);
  return magnitude.saturated ? `${grouped}+` : grouped;
}
