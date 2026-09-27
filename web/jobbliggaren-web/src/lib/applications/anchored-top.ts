import type { CSSProperties } from "react";

const ANCHOR_GUTTER = 16;
const ANCHOR_OFFSET = 170;
const ANCHOR_MIN_VISIBLE = 240;

/**
 * Vertical near-click anchoring for the /ansokningar action dialogs
 * ("Slutför och skicka" / "Logga uppföljning" — design §9: never a fixed top
 * position; on a long page a fixed-top surface leaves the user staring at only
 * the scrim). The surface's top edge sits `ANCHOR_OFFSET`px ABOVE the pointer's
 * viewport Y, CLAMPED to the viewport: at least `ANCHOR_GUTTER` from the top,
 * and never so low that less than `ANCHOR_MIN_VISIBLE` remains before the
 * bottom gutter.
 *
 * Pure and viewport-measurement-free (no reflow). Born as the PR 6 detail
 * drawer's positioning helper (`clampDrawerTop`); the drawer was retired
 * 2026-07-10 (ADR 0092 Livscykel-amendment) — the dialog anchoring survives it.
 */
export function clampAnchoredTop(clientY: number, viewportHeight: number): number {
  const upperBound = Math.max(
    ANCHOR_GUTTER,
    viewportHeight - ANCHOR_GUTTER - ANCHOR_MIN_VISIBLE,
  );
  return Math.min(Math.max(clientY - ANCHOR_OFFSET, ANCHOR_GUTTER), upperBound);
}

/**
 * The inline placement of an anchored dialog: its top edge at `top`, centred on X.
 * The dialog primitive centres with Tailwind's `-translate-x-1/2 -translate-y-1/2`,
 * which set the `translate` property, so this overrides `translate` itself. A
 * `transform` would compose with it instead and move the dialog twice (#1850).
 * On Y the dialog moves up by whatever of it would pass the bottom gutter (`100%`
 * in `translate` is its own height), so it opens whole wherever it fits; only a
 * dialog taller than the room between the gutters scrolls inside.
 */
export function anchoredDialogStyle(
  top: number | null | undefined,
): CSSProperties | undefined {
  return top != null
    ? {
        top: `${top}px`,
        translate: `-50% min(0px, calc(100dvh - ${ANCHOR_GUTTER}px - ${top}px - 100%))`,
        maxHeight: `calc(100dvh - ${2 * ANCHOR_GUTTER}px)`,
        overflowY: "auto",
      }
    : undefined;
}
