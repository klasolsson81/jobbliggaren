/**
 * ADR 0188 (#2265) — the cap of quick links in the /jobb hero, the junior link included: the backend's
 * `QuickLinkCap.MaxTotal`, pinned equal by `QuickLinkCapWireContractTests`. The web reads it only to decide whether to
 * offer an add; the API decides, and answers 409 past it.
 */
export const QUICK_LINK_MAX = 3;

/** The 409's problem title when the cap is reached (`QuickLinkCap.MaxTotalCode`, pinned by the same test). */
export const QUICK_LINK_FULL_CODE = "QuickLink.MaxTotal";

/** Whether one more quick link fits beside the junior link (counted while it is on) and the marked searches. */
export function hasQuickLinkRoom(juniorLinkOn: boolean, markedSearches: number): boolean {
  return markedSearches + (juniorLinkOn ? 1 : 0) < QUICK_LINK_MAX;
}
