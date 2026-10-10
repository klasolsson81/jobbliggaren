/**
 * What a request's Fetch Metadata says about how it was made. Runtime-agnostic, so the proxy and route
 * handlers read the same rule.
 *
 * Next's adapter strips `next-router-prefetch` before the proxy runs, so in the proxy only the standard headers
 * can identify a prefetch.
 */

/** A speculative fetch: Next's router, Speculation Rules (`Sec-Purpose: prefetch`), or the legacy `Purpose`. */
export function isPrefetch(headers: Headers): boolean {
  return (
    headers.get("next-router-prefetch") !== null ||
    (headers.get("sec-purpose") ?? "").includes("prefetch") ||
    headers.get("purpose") === "prefetch"
  );
}

/**
 * Anything but a top-level navigation: an image, a frame or a fetch. A client that sends neither header is not
 * called embedded.
 */
export function isEmbedded(headers: Headers): boolean {
  const mode = headers.get("sec-fetch-mode");
  const dest = headers.get("sec-fetch-dest");
  return (mode !== null && mode !== "navigate") || (dest !== null && dest !== "document");
}

/**
 * A top-level document navigation that another site started, such as a link clicked in webmail. All three headers
 * must be present and say so, so a client that sends none of them is never taken for one.
 */
export function isCrossSiteDocumentNavigation(headers: Headers): boolean {
  return (
    headers.get("sec-fetch-site") === "cross-site" &&
    headers.get("sec-fetch-mode") === "navigate" &&
    headers.get("sec-fetch-dest") === "document" &&
    !isPrefetch(headers)
  );
}
