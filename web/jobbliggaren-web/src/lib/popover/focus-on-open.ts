/**
 * What takes focus when a popover opens (#2212). A sheet covers the pill that opened it, so focus
 * always moves into a sheet. A search field takes focus where a keyboard is likely; with a coarse
 * pointer the dialog itself takes it instead, so opening does not raise a soft keyboard. An anchored
 * popover without a field leaves focus on its pill (ADR 0055).
 */
export type OpenFocus = "field" | "root" | "none";

export function focusOnOpen(
  placement: "anchored" | "sheet",
  coarsePointer: boolean,
  hasField: boolean,
): OpenFocus {
  if (hasField) return coarsePointer ? "root" : "field";
  return placement === "sheet" ? "root" : "none";
}

/** The pointer query the house already uses for touch-only rules. */
export const COARSE_POINTER_QUERY = "(pointer: coarse)";

/** False where there is no `matchMedia` (a server render, jsdom): a fine pointer is the default. */
export function isCoarsePointer(): boolean {
  return (
    typeof window !== "undefined" &&
    typeof window.matchMedia === "function" &&
    window.matchMedia(COARSE_POINTER_QUERY).matches
  );
}
