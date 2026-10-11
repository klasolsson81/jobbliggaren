/**
 * The query key the bounce's own hop carries, so a request that already came back is never bounced again. Its
 * value is a constant and names nothing.
 *
 * Its own module because the page that takes the marker off the address bar (`strip-bounce-marker.ts`, a client
 * island) must not import the bounce itself: that pulls `next/server` and the message catalogues into the browser.
 */
export const BOUNCE_MARKER = "vidare";
