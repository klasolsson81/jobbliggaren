import { BOUNCE_MARKER } from "./bounce-marker";

/**
 * The address without the cookie bounce's marker, or null when it carries none (ADR 0165).
 *
 * An anonymous visitor who arrives from another site passes through the continuation document and lands on
 * `/jobb/<id>?vidare=1`. That address is what they copy and share. Opened by a signed-in person from another
 * site, the marker makes the bounce stand down (it exists so a hop never repeats), so the link would show them
 * the public page. Taking the marker off the address bar once the page is up keeps the shared link clean.
 * Everything else in the address is kept as it is.
 */
export function stripBounceMarker(href: string): string | null {
  const url = new URL(href);
  if (!url.searchParams.has(BOUNCE_MARKER)) return null;
  url.searchParams.delete(BOUNCE_MARKER);
  return `${url.pathname}${url.search}${url.hash}`;
}
