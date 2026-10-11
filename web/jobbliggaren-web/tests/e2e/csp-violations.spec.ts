import { test, expect, type Page } from "@playwright/test";

// ADR 0163 — the nonce CSP must break nothing the browser can see. `securitypolicyviolation` is what a
// blocked script or frame looks like from the page, and the console says the same in prose; both are
// collected here for EVERY frame of the page, `blob:` frames included (`exposeBinding` is installed
// in each, which `addInitScript` alone would not report back from).
//
// Backend-free by construction: public pages only. The signed-in and CV-preview halves live in
// `csp-violations-signed-in.spec.ts`, which needs the dev stack.

interface Violation {
  readonly frame: string;
  readonly directive: string;
  readonly blocked: string;
}

async function watchViolations(page: Page): Promise<{ violations: Violation[]; consoleCsp: string[] }> {
  const violations: Violation[] = [];
  const consoleCsp: string[] = [];
  await page.exposeBinding("__reportCsp", (_source, v: Violation) => {
    violations.push(v);
  });
  await page.addInitScript(() => {
    document.addEventListener(
      "securitypolicyviolation",
      (e) =>
        void (
          window as unknown as { __reportCsp: (v: Violation) => void }
        ).__reportCsp({
          frame: location.protocol,
          directive: e.violatedDirective,
          blocked: e.blockedURI.slice(0, 40),
        }),
      true
    );
  });
  page.on("console", (m) => {
    if (/Content Security Policy|Refused to (execute|load|apply|frame)/i.test(m.text())) {
      consoleCsp.push(m.text().slice(0, 200));
    }
  });
  return { violations, consoleCsp };
}

test.describe("public pages and client navigation raise no CSP violation", () => {
  for (const path of ["/", "/logga-in", "/vanliga-fragor", "/integritet"]) {
    test(`a full load of ${path}`, async ({ page }) => {
      const seen = await watchViolations(page);

      await page.goto(path);
      await page.waitForLoadState("networkidle");

      expect(seen.violations).toEqual([]);
      expect(seen.consoleCsp).toEqual([]);
    });
  }

  test("a client-side navigation (a Next <Link>) hydrates and navigates with no violation", async ({ page }) => {
    const seen = await watchViolations(page);

    await page.goto("/");
    await page.waitForLoadState("networkidle");
    // A soft navigation keeps the document, so a marker on `window` survives it. If hydration were
    // blocked the click would be a plain browser navigation and the marker would be gone.
    await page.evaluate(() => {
      (window as unknown as { __softNav: boolean }).__softNav = true;
    });
    const link = page.locator('footer a[href="/vanliga-fragor"]').first();
    await expect(link).toBeVisible();
    await link.click();
    await page.waitForURL("**/vanliga-fragor");
    await page.waitForLoadState("networkidle");

    expect(await page.evaluate(() => (window as unknown as { __softNav?: boolean }).__softNav)).toBe(true);
    expect(seen.violations).toEqual([]);
    expect(seen.consoleCsp).toEqual([]);
  });
});

// The blob: document inherits the creator's origin AND its CSP. This is the control behind ADR 0163's
// claim that the nonce hardens the CV preview: measured in Chromium 2026-10-11, an inline script in an
// HTML blob RAN under 'unsafe-inline' and is BLOCKED under the nonce policy, while a PDF blob loads in
// the browser's viewer without a single violation. The real preview is exercised in the signed-in spec.
//
// DECLARED UNREACHABLE (AGENTS.md §5 Tests): no path in src/ produces an HTML blob — the CV preview only
// accepts the allowlisted PDF/DOCX types and fixes the blob's type itself (cv-preview.tsx, ADR 0163 D6).
// The HTML blob here is a control the test builds on purpose, and it asserts only what the browser does
// with the policy a blob inherits, never what production does.
test("a blob: document built from bytes inherits the policy: an inline script in an HTML blob does not run, a PDF blob raises nothing", async ({
  page,
}) => {
  const seen = await watchViolations(page);
  await page.goto("/logga-in");
  await page.waitForLoadState("networkidle");

  await page.evaluate(() => {
    const w = window as unknown as { __ran: string[] };
    w.__ran = [];
    window.addEventListener("message", (e) => w.__ran.push(String(e.data)));

    const pdf = new Blob(["%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 100]>>endobj\ntrailer<</Root 1 0 R/Size 4>>\n%%EOF"], {
      type: "application/pdf",
    });
    const pdfFrame = document.createElement("iframe");
    pdfFrame.id = "pdf-control";
    pdfFrame.src = URL.createObjectURL(pdf);
    document.body.append(pdfFrame);

    // Built from pieces so this file never contains a literal closing script tag.
    const html = new Blob(["<scr" + "ipt>parent.postMessage('inline-script-ran','*')</scr" + "ipt><body>x"], {
      type: "text/html",
    });
    const htmlFrame = document.createElement("iframe");
    htmlFrame.id = "html-control";
    htmlFrame.src = URL.createObjectURL(html);
    document.body.append(htmlFrame);
  });

  // The HTML blob's violation is the signal that its document finished loading under the policy.
  await expect.poll(() => seen.violations.length, { timeout: 10_000 }).toBeGreaterThan(0);
  await page.waitForTimeout(500);

  expect(await page.evaluate(() => (window as unknown as { __ran: string[] }).__ran)).toEqual([]);
  // Exactly the blocked inline script, reported from the blob: frame — nothing from the PDF.
  expect(seen.violations).toHaveLength(1);
  expect(seen.violations[0]).toMatchObject({ frame: "blob:", directive: expect.stringMatching(/^script-src/) });
});
