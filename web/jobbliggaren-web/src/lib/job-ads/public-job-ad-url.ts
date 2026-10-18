import { absoluteUrl } from "@/lib/site-url";

/**
 * The one URL of a public job page: its canonical (ADR 0165 D4) and its `loc` in the job sitemap (ADR 0174), built in
 * one place so the two cannot differ. The id is the lower-case form the proxy redirects every other spelling to.
 */
export function publicJobAdUrl(id: string): string {
  return absoluteUrl(`/jobb/${id}`);
}
