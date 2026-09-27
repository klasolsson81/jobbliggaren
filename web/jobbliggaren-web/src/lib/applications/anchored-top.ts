import type { CSSProperties } from "react";

/**
 * Vertical near-click anchoring for the /ansokningar action dialogs
 * ("Slutför och skicka" / "Logga uppföljning" — design §9: never a fixed top
 * position; on a long page a fixed-top surface leaves the user staring at only
 * the scrim). The surface's top edge sits ~`offset`px ABOVE the pointer's
 * viewport Y, CLAMPED to the viewport: at least `gutter` from the top, and
 * never so low that less than `minVisible` of the surface remains before the
 * bottom gutter.
 *
 * Pure and viewport-measurement-free (no reflow). Born as the PR 6 detail
 * drawer's positioning helper (`clampDrawerTop`); the drawer was retired
 * 2026-07-10 (ADR 0092 Livscykel-amendment) — the dialog anchoring survives it.
 */
export interface AnchoredTopOptions {
  gutter?: number;
  offset?: number;
  minVisible?: number;
}

export function clampAnchoredTop(
  clientY: number,
  viewportHeight: number,
  { gutter = 16, offset = 240, minVisible = 120 }: AnchoredTopOptions = {},
): number {
  const lowerBound = gutter;
  const upperBound = Math.max(gutter, viewportHeight - gutter - minVisible);
  const desired = clientY - offset;
  return Math.min(Math.max(desired, lowerBound), upperBound);
}

/**
 * The inline placement of an anchored dialog: its top edge at `top`, centred on X.
 * The dialog primitive centres with Tailwind's `-translate-x-1/2 -translate-y-1/2`,
 * which set the `translate` property, so this overrides `translate` itself. A
 * `transform` would compose with it instead and move the dialog twice (#1850).
 */
export function anchoredDialogStyle(
  top: number | null | undefined,
): CSSProperties | undefined {
  return top != null ? { top: `${top}px`, translate: "-50% 0" } : undefined;
}
