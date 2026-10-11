# Verifying the nonce CSP (ADR 0163)

How to check that the page policy blocks nothing the app needs, locally and on production. The automated half is
`web/jobbliggaren-web/tests/e2e/{security-headers,csp-violations,csp-violations-signed-in}.spec.ts` and `src/proxy.test.ts`; this
runbook is the part a person does with a real browser, including the two engines the automated suite does not cover (Firefox and
Safari on iOS).

## What a violation looks like

A blocked script or frame writes a console error (`Refused to execute inline script because it violates the following Content
Security Policy directive: "script-src 'self' 'nonce-…' 'strict-dynamic'"`) **and** fires a `securitypolicyviolation` event.
Open DevTools **before** loading the page, tick *Preserve log*, and filter the console on `Content Security Policy`. To see them
as one line each, paste this in the console first (it only lives until the next full load, so use it for soft navigations):

```js
document.addEventListener("securitypolicyviolation", (e) => console.warn("CSPV", e.violatedDirective, e.blockedURI), true);
```

A page that looks fine but whose buttons do nothing is the other symptom: the bootstrap script was blocked and nothing hydrated.

## 1. Automated, no backend needed (any session)

```bash
cd web/jobbliggaren-web
pnpm build
node_modules/.bin/next start -p 3100 &        # or any free port
PLAYWRIGHT_BASE_URL=http://localhost:3100 node_modules/.bin/playwright test tests/e2e/security-headers.spec.ts tests/e2e/csp-violations.spec.ts
```

Pass criteria: all green. Against `pnpm dev` the two `/logga-in/lank` cache assertions fail by design (Next dev rewrites `Cache-Control`).

## 2. Locally with the stack (stack-owner session only: shared Postgres, ADR 0154 / CLAUDE.md §6.5)

With the dev API and database up and `pnpm dev` on :3000:

```bash
PLAYWRIGHT_BASE_URL=http://localhost:3000 node_modules/.bin/playwright test tests/e2e/csp-violations-signed-in.spec.ts
```

It logs in from a cold page, navigates inside the app shell with a soft navigation, imports a generated PDF through `/api/cv/import`,
opens the preview on `/cv/granska/<id>` and asserts the iframe is a `blob:` URL with no `securitypolicyviolation` and no CSP console line.
By hand, in Chrome with DevTools open:

| Flow | Do | Pass |
|---|---|---|
| Login | `/logga-in` → address → *Fortsätt* → code → *Bekräfta koden* | lands on `/oversikt`, console has no CSP line |
| Login by link | open a login link from the dev mail sink | the page opens; no CSP line |
| Client navigation | click through the app-shell nav (`/ansokningar`, `/jobb`, `/foretag`) without reloading | each page renders, URL changes, no full reload (the tab does not flash), no CSP line |
| Theme | dark mode is **off** (`DARK_MODE_ENABLED = false`); there is no toggle. Confirm the page stays light with `localStorage["jp-theme"] = "dark"` set and reloaded | stays light; no CSP line. Re-test with the toggle if dark mode is ever re-enabled: `ThemeScript` must then carry the nonce |
| CV preview | `/cv/granska/<id>` → *Öppna CV-filen* | the PDF paints in the modal; *Ladda ner* downloads |

## 3. Production, after the release

The box deploys on merge (ADR 0154). Anonymous checks only (CLAUDE.md §9.2: no signed-in request to the box; a signed-in check is Klas's own in his own browser).

```bash
curl -sSI https://jobbliggaren.se/ | grep -i -E "^(content-security-policy|strict-transport-security|cross-origin|via|link):"
```

Pass criteria:

- exactly **one** `content-security-policy`, with `script-src 'self' 'nonce-…' 'strict-dynamic'` and **no** `'unsafe-inline'` in `script-src`;
- exactly one `strict-transport-security: max-age=31536000` (unchanged);
- one `cross-origin-opener-policy: same-origin` and one `cross-origin-resource-policy: same-origin`;
- **no** `via:` line;
- the nonce differs between two requests: run the command twice and compare.

Then, in your own browser with DevTools open on `https://jobbliggaren.se`: the flows in the table above (login with each provider, client navigation, the
CV preview). Finally the scanners, which are Klas's to run (they are external requests): <https://developer.mozilla.org/en-US/observatory> and
<https://securityheaders.com/?q=jobbliggaren.se&followRedirects=on>. The grade is **not** claimed by the ADR.

## 4. The CV preview in Safari on iOS and in Firefox

These two engines render a PDF in a `blob:` iframe with their own viewers, and the automated suite only measures Chromium (ADR 0163 D5 records
this as unmeasured). Do it by hand once after the release, on a CV with an uploaded PDF original.

**Firefox (desktop)** — open `/cv/granska/<id>` → *Öppna CV-filen*; DevTools → Console (and Network).

- Pass: the PDF.js viewer paints the document inside the modal; no `Content Security Policy` line; *Ladda ner* saves `original.pdf`.
- Fail: a blank frame with a CSP line naming `frame-src`, `script-src` or `object-src`. Record the exact line; do not loosen the policy to make it
  go away (DPIA #659 §11 lapse trigger 4 concerns `frame-ancestors`, and `script-src` widening is this ADR's lapse trigger 4).

**Safari on iOS** — iOS has no console on the phone. Either attach it to a Mac (iPhone: *Settings → Safari → Advanced → Web Inspector*; Mac Safari:
*Develop → \[the iPhone\] → the tab*) and read the console as above, or, without a Mac, judge by behaviour.

- Pass: the modal opens, the first page of the PDF paints in the frame (iOS Safari shows a PDF in an iframe as a scrollable page image; that is the platform,
  not this change), and *Ladda ner* hands the file to the share sheet.
- A blank frame is a failure only if the same CV also paints on the desktop; compare before concluding it is the CSP. Without a Mac a blank frame
  cannot be attributed to the CSP from the phone alone.

Report the result on [#2126](https://github.com/klasolsson81/jobbliggaren/issues/2126).
