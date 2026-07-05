/**
 * Vertical positioning for the /ansokningar detail drawer (design handoff §9):
 * the panel is anchored near the click point — its top edge sits ~`offset`px
 * ABOVE the pointer's viewport Y — never a fixed top position (on a long page a
 * fixed-top panel would leave the user staring at only the scrim). The result is
 * CLAMPED to the viewport: at least `gutter` from the top, and never so low that
 * less than `minVisible` of the panel remains before the bottom gutter.
 *
 * Pure and viewport-measurement-free (no reflow): the shell caps the panel height
 * with `maxHeight = viewportHeight - top - gutter` + internal scroll, so the panel
 * always fits without a measure pass.
 */
export interface DrawerPositionOptions {
  gutter?: number;
  offset?: number;
  minVisible?: number;
}

export function clampDrawerTop(
  clientY: number,
  viewportHeight: number,
  { gutter = 16, offset = 240, minVisible = 120 }: DrawerPositionOptions = {},
): number {
  const lowerBound = gutter;
  const upperBound = Math.max(gutter, viewportHeight - gutter - minVisible);
  const desired = clientY - offset;
  return Math.min(Math.max(desired, lowerBound), upperBound);
}
