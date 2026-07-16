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
): string {
  const grouped = formatNumber(format, magnitude.magnitude);
  return magnitude.saturated ? `${grouped}+` : grouped;
}
