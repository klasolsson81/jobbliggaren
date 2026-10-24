import { absoluteUrl } from "@/lib/site-url";

/**
 * The path of a public job page. The canonical below and the sign-in invitation's `next` (ADR 0183) are built from it,
 * so a visitor who signs in from the page comes back to the same ad. The id is the lower-case form the proxy redirects
 * every other spelling to.
 */
export function publicJobAdPath(id: string): `/jobb/${string}` {
  return `/jobb/${id}`;
}

/**
 * The one URL of a public job page: its canonical (ADR 0165 D4), its `loc` in the job sitemap (ADR 0174) and the link
 * the page's share control hands on (ADR 0183), built in one place so they cannot differ.
 */
export function publicJobAdUrl(id: string): string {
  return absoluteUrl(publicJobAdPath(id));
}
