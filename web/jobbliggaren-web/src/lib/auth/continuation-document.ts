/**
 * The page an external login's callback answers with, whatever the outcome (ADR 0142 D8, #1744).
 *
 * A 200 document rather than a redirect: the callback arrives as a cross-site navigation, and a
 * `Strict` cookie set on a 3xx inside that chain is not sent on the next hop. From this document the
 * next hop starts on our own origin, so the session and flow cookies set beside it are sent.
 *
 * Its form is decided in the 6a form round (design-reviewer Blocker 2, security-auditor m-1,
 * senior-cto-advisor F13.4): `charset` first, then `referrer`, before any element that fetches or
 * navigates; no subresource but a `data:` icon, so nothing is requested while the URL carries
 * `code` and `state`; the same target in the refresh and the link. Nothing from the query is
 * reflected, and the target is escaped for the attribute it is written into.
 *
 * One constant style (DESIGN.md §11.6, #1746) paints the app's canvas and holds the content
 * invisible for two seconds, so a working refresh shows only the background; a browser that blocks
 * the refresh shows the link once the hold ends.
 */

/**
 * The document's only style. Each colour is the light value of the token DESIGN.md §11.6 names, and
 * the font is the system tail of `--jp-font-sans`. It fetches nothing and interpolates nothing. The
 * content is hidden only inside the animation, so a style that is refused leaves the page visible.
 */
export const CONTINUATION_STYLE = [
  ':root{color-scheme:light;background:#F4F6FA;color:#0C1A2E;font:16px/1.55 -apple-system,BlinkMacSystemFont,"Segoe UI",system-ui,sans-serif}',
  "body{margin:0}",
  "main{max-width:384px;margin:0 auto;padding:48px 24px;animation:jbl-hold 2s step-end}",
  "@keyframes jbl-hold{from,to{visibility:hidden}}",
  "h1{margin:0 0 16px;font-size:32px;font-weight:700;color:#133F73}",
  "p{margin:0}",
  "a{display:inline-flex;align-items:center;min-height:44px;color:#15603F;text-underline-offset:2px}",
  "a:focus-visible{outline:2px solid #15603F;outline-offset:2px;border-radius:4px}",
].join("\n");

export type ContinuationDocument = {
  /** The locale the strings were resolved in. */
  readonly lang: string;
  readonly title: string;
  readonly heading: string;
  readonly continueLabel: string;
  /** A same-site path. */
  readonly target: string;
};

const ESCAPES: Readonly<Record<string, string>> = {
  "&": "&amp;",
  "<": "&lt;",
  ">": "&gt;",
  '"': "&quot;",
  "'": "&#39;",
};

export function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (ch) => ESCAPES[ch] ?? ch);
}

export function buildContinuationDocument(doc: ContinuationDocument): string {
  const target = escapeHtml(doc.target);
  return [
    "<!DOCTYPE html>",
    `<html lang="${escapeHtml(doc.lang)}">`,
    "<head>",
    '<meta charset="utf-8">',
    '<meta name="referrer" content="no-referrer">',
    '<meta name="viewport" content="width=device-width, initial-scale=1">',
    `<meta http-equiv="refresh" content="0;url=${target}">`,
    '<link rel="icon" href="data:,">',
    `<title>${escapeHtml(doc.title)}</title>`,
    `<style>${CONTINUATION_STYLE}</style>`,
    "</head>",
    "<body>",
    "<main>",
    `<h1>${escapeHtml(doc.heading)}</h1>`,
    `<p><a href="${target}" rel="noreferrer">${escapeHtml(doc.continueLabel)}</a></p>`,
    "</main>",
    "</body>",
    "</html>",
    "",
  ].join("\n");
}
