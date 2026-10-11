import { test as base, expect, type Page } from "@playwright/test";
import { loginAs, seedTestUser } from "./helpers/auth";
import { loggedInTest } from "./helpers/session";

// ADR 0163 — the signed-in half of the nonce-CSP verification: the login flow, a client navigation
// inside the app shell, and the CV preview (a `blob:` PDF framed in a modal; ADR 0101 Amendment
// 2026-09-06). Needs the dev stack (API + database), so it is run by the stack-owner session; the
// public-page half is `csp-violations.spec.ts` and needs nothing.
//
// The CV half is skipped in CI: CI serves a production build over http, and `isSameOriginRequest`
// requires an https Origin in production, so `/api/cv/import` answers 403 there. It runs against
// `pnpm dev`, which is how the stack-owner runs it.

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";
const RUN_ID = Date.now();

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

/** A one-page PDF with real text and a correct xref table, so the importer's parser accepts it. */
function buildPdf(lines: readonly string[]): Buffer {
  const content = `BT /F1 12 Tf 50 780 Td 14 TL ${lines.map((l) => `(${l}) Tj T*`).join(" ")} ET`;
  const objects = [
    "<</Type/Catalog/Pages 2 0 R>>",
    "<</Type/Pages/Kids[3 0 R]/Count 1>>",
    "<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>",
    `<</Length ${content.length}>>\nstream\n${content}\nendstream`,
    "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
  ];
  let out = "%PDF-1.4\n";
  const offsets: number[] = [];
  objects.forEach((body, i) => {
    offsets.push(out.length);
    out += `${i + 1} 0 obj\n${body}\nendobj\n`;
  });
  const xref = out.length;
  out += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  for (const o of offsets) out += `${String(o).padStart(10, "0")} 00000 n \n`;
  out += `trailer\n<</Size ${objects.length + 1}/Root 1 0 R>>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(out, "latin1");
}

// The login flow has to be watched from its first request, so it is NOT the worker-scoped fixture
// below (which logs in before any page exists). Its own address: a second code for the same address
// inside the cooldown gets no mail (#1738).
base("login, then a client navigation inside the app shell, raise no CSP violation", async ({ page }) => {
  const runId = RUN_ID + 1;
  await seedTestUser(BACKEND_URL, runId);
  const seen = await watchViolations(page);

  await loginAs(page, runId);
  await page.waitForLoadState("networkidle");

  // A soft navigation keeps the document: the marker survives it only if hydration worked.
  await page.evaluate(() => {
    (window as unknown as { __softNav: boolean }).__softNav = true;
  });
  const link = page.locator('a[href="/ansokningar"]').first();
  await expect(link).toBeVisible();
  await link.click();
  await page.waitForURL("**/ansokningar");
  await page.waitForLoadState("networkidle");

  expect(await page.evaluate(() => (window as unknown as { __softNav?: boolean }).__softNav)).toBe(true);
  expect(seen.violations).toEqual([]);
  expect(seen.consoleCsp).toEqual([]);
});

const test = loggedInTest(RUN_ID);

test("the CV preview: a PDF in a blob: iframe renders in the modal with no CSP violation", async ({ page }) => {
  test.skip(!!process.env.CI, "the import route refuses an http Origin in a production build; run against pnpm dev");
  const seen = await watchViolations(page);

  // Imported from inside the page, so the request carries the browser's own Origin and the session
  // cookie, exactly as the import form's would.
  await page.goto("/cv");
  const imported = await page.evaluate(async (bytes) => {
    const body = new FormData();
    body.append("file", new Blob([new Uint8Array(bytes)], { type: "application/pdf" }), "csp-probe.pdf");
    const res = await fetch("/api/cv/import", { method: "POST", body });
    return { status: res.status, json: res.ok ? await res.json() : null };
  }, [...buildPdf(["Anna Svensson", "anna@example.test", "Utvecklare", "Erfarenhet: byggt webbtjanster"])]);
  expect(imported.status, "the import is accepted").toBeLessThan(300);
  const parsedId = (imported.json as { parsedResumeId: string }).parsedResumeId;

  await page.goto(`/cv/granska/${parsedId}`);
  await page.getByRole("button", { name: "Öppna CV-filen" }).click();

  const frame = page.getByTitle("Din uppladdade CV-fil");
  await expect(frame).toBeVisible({ timeout: 15_000 });
  // The mechanism ADR 0101 names: fetch -> blob -> iframe(blob:). A direct BFF src would be refused by
  // frame-ancestors, and a missing src means the state never reached "ready".
  await expect(frame).toHaveAttribute("src", /^blob:/);
  // Let the browser's PDF viewer load inside the frame before judging the page.
  await page.waitForTimeout(1500);

  expect(seen.violations).toEqual([]);
  expect(seen.consoleCsp).toEqual([]);
});
