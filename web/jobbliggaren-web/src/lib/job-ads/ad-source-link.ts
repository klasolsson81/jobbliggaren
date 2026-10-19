/**
 * The link out of the public job page (ADR 0160 D4: `url` is public, http(s) only).
 *
 * The importer picks the ad's address as `webpage_url`, then `source_links`
 * (`PlatsbankenJobSource.FirstNonMailtoUrl`), so it is not always an Arbetsförmedlingen page. The label says
 * "hos Arbetsförmedlingen" only when the host does, and is the plain "Öppna annonsen" otherwise. A value that is
 * not an http(s) URL gets no link at all: it ends up in an `href`, and `javascript:` there is script.
 */
export type AdSourceLink = {
  readonly href: string;
  readonly label: "openAdAf" | "openAd";
};

const ARBETSFORMEDLINGEN_HOST = "arbetsformedlingen.se";

export function adSourceLink(url: string): AdSourceLink | null {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return null;
  }
  if (parsed.protocol !== "https:" && parsed.protocol !== "http:") return null;

  const host = parsed.hostname.toLowerCase();
  const isAf = host === ARBETSFORMEDLINGEN_HOST || host.endsWith(`.${ARBETSFORMEDLINGEN_HOST}`);
  return { href: parsed.href, label: isAf ? "openAdAf" : "openAd" };
}
