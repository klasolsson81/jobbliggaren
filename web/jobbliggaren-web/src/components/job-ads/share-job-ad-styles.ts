/**
 * One look for every item on the share row (ADR 0183, Amendment 2026-10-25): the house's secondary button at 44 px,
 * filling its cell so the row stands in columns of one width. Its own module because the server-rendered links and the
 * client island both use it. `.jp-btn` is unlayered, so nothing here may set a property that rule sets (DESIGN.md §6).
 */
export const SHARE_ITEM = "jp-btn jp-btn--secondary w-full";

/** The row: wrapping on a narrow screen; from 640 px the three platforms on one line and the other three under them. */
export const SHARE_LIST = "flex flex-wrap gap-2 sm:grid sm:grid-cols-[repeat(3,max-content)]";

/** Lucide's size for an icon inline with text (DESIGN.md §7). */
export const SHARE_ICON_SIZE = 16;
