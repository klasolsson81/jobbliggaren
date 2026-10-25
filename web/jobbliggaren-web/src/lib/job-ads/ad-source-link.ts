/**
 * The links out of the public job page (ADR 0160 D4: `url` is public, http(s) only).
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

function parseHttpUrl(url: string): URL | null {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return null;
  }
  return parsed.protocol === "https:" || parsed.protocol === "http:" ? parsed : null;
}

export function adSourceLink(url: string): AdSourceLink | null {
  const parsed = parseHttpUrl(url);
  if (parsed === null) return null;

  const host = parsed.hostname.toLowerCase();
  const isAf = host === ARBETSFORMEDLINGEN_HOST || host.endsWith(`.${ARBETSFORMEDLINGEN_HOST}`);
  return { href: parsed.href, label: isAf ? "openAdAf" : "openAd" };
}

/**
 * The employer's application page on the public job page, as the backend's gate admitted it (#2214 PR 1b). It ends
 * up in an `href` too, so it passes the same http(s) check; anything else, or no value, gives no "Ansök".
 */
export function applicationLinkHref(url: string | null | undefined): string | null {
  if (url === null || url === undefined) return null;
  return parseHttpUrl(url)?.href ?? null;
}
