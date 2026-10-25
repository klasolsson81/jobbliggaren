/**
 * Where an anchored panel goes (#2212). On a wide and tall enough viewport it is anchored: 8px under
 * its trigger, left-aligned with it, and moved left when its right edge would come closer than 16px
 * to the viewport's. On a narrow or short one it is a sheet under the header, which CSS lays out.
 * Pure, so the policy is tested without layout; `usePanelPosition` reads the DOM and calls it.
 *
 * Anchored coordinates are the document's: `top` and `left` are for a panel whose containing block
 * is the initial containing block (`position: absolute`, no positioned ancestor).
 */

/** From the trigger's bottom edge to the panel's top. */
export const PANEL_GAP = 8;
/** The least distance between an anchored panel and either side of the viewport. */
export const VIEWPORT_GUTTER = 16;
/**
 * At or below this client width a panel is a sheet: above it a 580px panel always fits between the
 * gutters (641 - 32 = 609). It is also 1280px at 200% zoom.
 */
export const SHEET_MAX_WIDTH = 640;
/**
 * At or below this client height a panel is a sheet: a landscape phone, or 1366x768 at 200% zoom,
 * has no room under the pill for a panel's own minimum height.
 */
export const SHEET_MAX_HEIGHT = 500;

export interface TriggerBox {
  readonly left: number;
  readonly bottom: number;
}

/** `clientWidth` and `clientHeight` are the document element's, so a classic scrollbar is excluded. */
export interface Viewport {
  readonly clientWidth: number;
  readonly clientHeight: number;
  readonly scrollX: number;
  readonly scrollY: number;
}

export type PanelPlacement =
  | {
      readonly placement: "anchored";
      readonly top: number;
      readonly left: number;
      readonly width: number;
    }
  | { readonly placement: "sheet" };

export function placePanel(
  trigger: TriggerBox,
  viewport: Viewport,
  preferredWidth: number,
): PanelPlacement {
  const { clientWidth, clientHeight, scrollX, scrollY } = viewport;
  const top = trigger.bottom + PANEL_GAP + scrollY;

  // A document without layout reports a zero-size viewport: there is no edge to keep inside, and
  // nothing to size a sheet to.
  if (clientWidth <= 0 || clientHeight <= 0) {
    return { placement: "anchored", top, left: trigger.left + scrollX, width: preferredWidth };
  }

  if (clientWidth <= SHEET_MAX_WIDTH || clientHeight <= SHEET_MAX_HEIGHT) {
    return { placement: "sheet" };
  }

  const width = Math.min(preferredWidth, clientWidth - 2 * VIEWPORT_GUTTER);
  const left = Math.max(
    VIEWPORT_GUTTER,
    Math.min(trigger.left, clientWidth - VIEWPORT_GUTTER - width),
  );
  return { placement: "anchored", top, left: left + scrollX, width };
}
