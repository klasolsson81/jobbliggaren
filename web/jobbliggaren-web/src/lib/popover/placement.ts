/**
 * Where an anchored panel goes (#2212): 8px under its trigger, left-aligned with it, and moved left
 * when its right edge would come closer than 16px to the viewport's. Pure, so the policy is tested
 * without layout; `usePanelPosition` reads the DOM and calls it.
 *
 * Coordinates are the document's: `top` and `left` are for a panel whose containing block is the
 * initial containing block (`position: absolute`, no positioned ancestor).
 */

/** From the trigger's bottom edge to the panel's top. */
export const PANEL_GAP = 8;
/** The least distance between the panel and either side of the viewport. */
export const VIEWPORT_GUTTER = 16;

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

export interface PanelPlacement {
  readonly top: number;
  readonly left: number;
  readonly width: number;
}

export function placePanel(
  trigger: TriggerBox,
  viewport: Viewport,
  preferredWidth: number,
): PanelPlacement {
  const { clientWidth, clientHeight, scrollX, scrollY } = viewport;
  const top = trigger.bottom + PANEL_GAP + scrollY;

  // A document without layout reports a zero-size viewport: there is no edge to keep inside.
  if (clientWidth <= 0 || clientHeight <= 0) {
    return { top, left: trigger.left + scrollX, width: preferredWidth };
  }

  const width = Math.min(preferredWidth, clientWidth - 2 * VIEWPORT_GUTTER);
  const left = Math.max(
    VIEWPORT_GUTTER,
    Math.min(trigger.left, clientWidth - VIEWPORT_GUTTER - width),
  );
  return { top, left: left + scrollX, width };
}
