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
 * `code` and `state`; unstyled; the same target in the refresh and the link. Nothing from the query
 * is reflected, and the target is escaped for the attribute it is written into.
 */

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
