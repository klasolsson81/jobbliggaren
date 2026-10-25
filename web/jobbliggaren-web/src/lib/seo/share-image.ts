/**
 * The public job page's link-preview image (ADR 0165 Amendment 2026-10-25): one square of the brand mark, the same for
 * every ad. 400 px is the widest image LinkedIn still shows as a thumbnail ("less than 401 pixels wide") and is under the
 * 600 × 315 Facebook needs for its large card, so a shared ad unfurls as the compact card on both.
 */
export const SHARE_IMAGE = { path: "/delningsbild", width: 400, height: 400 } as const;
