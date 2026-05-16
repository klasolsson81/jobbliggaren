/**
 * Frontend visual verification — temp screenshot-loop.
 *
 * Kör Playwright headless mot en dev-server och tar screenshots i tre
 * viewports (1280 / 1920 / 3440) × light/dark. Bilderna sparas UTANFÖR repot
 * i c:/tmp/jobbpilot-visual/<tidsstämpel>/ och är self-cleaning: vid varje
 * körning raderas ALLA tidigare körningars mappar FÖRST (cleanup får inte
 * vara ett kom-ihåg-steg — se docs/runbooks/frontend-visual-verification.md).
 *
 * Två lägen:
 *
 *  1. Publikt (default) — `pnpm visual-verify` mot lokal `pnpm dev`.
 *     Capturerar enbart publika sidor. Oförändrat beteende.
 *
 *  2. Auth-läge (opt-in) — sätt VISUAL_AUTH_EMAIL + VISUAL_AUTH_PW +
 *     VISUAL_BACKEND_URL + VISUAL_BASE_URL (live-frontend). Capturerar då
 *     ÄVEN auth-gated sidor mot live-deploy per runbookens tre-nivå-policy.
 *     Login sker via direkt backend-call (robustare än formulärdrivning);
 *     session-cookien injiceras i browser-context och persisteras ALDRIG
 *     till disk (ingen storageState-fil — CLAUDE.md §5.4). En temporär
 *     "fixture"-sökning skapas via API så att populerade lista-/detalj-/
 *     dialog-tillstånd kan capureras, och raderas i teardown.
 *
 * Inga creds i kod eller repo — endast via env (CLAUDE.md §5.4).
 *
 * Kör (publikt):  pnpm dev   (separat terminal)
 *                 pnpm visual-verify
 *
 * Kör (auth, live):
 *   VISUAL_BASE_URL=https://www.jobbpilot.se \
 *   VISUAL_BACKEND_URL=https://dev.jobbpilot.se \
 *   VISUAL_AUTH_EMAIL=... VISUAL_AUTH_PW=... pnpm visual-verify
 */
import { chromium, type BrowserContext, type Page } from "@playwright/test";
import { mkdirSync, rmSync, existsSync } from "node:fs";
import { join } from "node:path";

const BASE_URL = process.env.VISUAL_BASE_URL ?? "http://localhost:3000";
const ROOT = process.env.VISUAL_OUT_ROOT ?? "C:/tmp/jobbpilot-visual";

const AUTH_EMAIL = process.env.VISUAL_AUTH_EMAIL;
const AUTH_PW = process.env.VISUAL_AUTH_PW;
const BACKEND_URL = process.env.VISUAL_BACKEND_URL;
const AUTH_MODE = Boolean(AUTH_EMAIL && AUTH_PW && BACKEND_URL);

// Session-cookie satt av frontend efter login (lib/auth/session.ts).
// __Host--prefix kräver host-only + Secure + path=/ → injiceras via `url`.
const SESSION_COOKIE = "__Host-jobbpilot_session";

interface PageTarget {
  path: string;
  name: string;
  /** Auth-gated → bara i auth-läge. */
  auth?: boolean;
}

// Publika sidor — alltid (ingen backend krävs).
const PUBLIC_PAGES: PageTarget[] = [
  { path: "/", name: "landing" },
  { path: "/logga-in", name: "logga-in" },
  { path: "/registrera", name: "registrera" },
  { path: "/vantelista", name: "vantelista" },
];

const VIEWPORTS = [
  { w: 1280, h: 800, tag: "1280" },
  { w: 1920, h: 1080, tag: "1920" },
  { w: 3440, h: 1440, tag: "3440" },
];

const THEMES = ["light", "dark"] as const;

function timestamp(): string {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}`;
}

/** Direkt backend-login → opaque sessionId. Inga creds loggas. */
async function login(): Promise<string> {
  const res = await fetch(`${BACKEND_URL}/api/v1/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email: AUTH_EMAIL, password: AUTH_PW }),
  });
  if (!res.ok) {
    throw new Error(
      `Login misslyckades (HTTP ${res.status}) mot ${BACKEND_URL}/api/v1/auth/login`,
    );
  }
  const data = (await res.json()) as { sessionId?: string };
  if (!data.sessionId) {
    throw new Error("Login-svar saknar sessionId.");
  }
  return data.sessionId;
}

const FIXTURE_NAME = `F2 visuell verifiering (temp ${timestamp()})`;

/** Skapar en temporär sökning så populerade UI-tillstånd kan capureras. */
async function createFixture(sessionId: string): Promise<string> {
  const auth = {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
  const createRes = await fetch(`${BACKEND_URL}/api/v1/saved-searches`, {
    method: "POST",
    headers: auth,
    body: JSON.stringify({
      name: FIXTURE_NAME,
      ssyk: null,
      region: null,
      q: "utvecklare",
      sortBy: 0, // PublishedAtDesc (projektkontrakt: numeriskt enum-värde)
      notificationEnabled: false,
    }),
  });
  if (!createRes.ok) {
    throw new Error(`Skapa fixture-sökning misslyckades (HTTP ${createRes.status}).`);
  }
  // Lista och hitta fixture-id (create-svaret konsumeras ej av frontend).
  const listRes = await fetch(`${BACKEND_URL}/api/v1/saved-searches`, {
    headers: { Authorization: `Bearer ${sessionId}` },
  });
  if (!listRes.ok) {
    throw new Error(`Lista sökningar misslyckades (HTTP ${listRes.status}).`);
  }
  const list = (await listRes.json()) as { id: string; name: string }[];
  const fixture = list.find((s) => s.name === FIXTURE_NAME);
  if (!fixture) {
    throw new Error("Fixture-sökning hittades inte i listan efter create.");
  }
  return fixture.id;
}

async function deleteFixture(sessionId: string, id: string): Promise<void> {
  const res = await fetch(`${BACKEND_URL}/api/v1/saved-searches/${id}`, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${sessionId}` },
  });
  if (!res.ok) {
    console.warn(
      `[visual-verify] VARNING: fixture-teardown misslyckades (HTTP ${res.status}) — ` +
        `temp-sökning ${id} kan ligga kvar i dev-DB, radera manuellt.`,
    );
  }
}

async function shoot(page: Page, outDir: string, name: string): Promise<void> {
  await page.screenshot({ path: join(outDir, `${name}.png`), fullPage: true });
}

async function main(): Promise<void> {
  // Self-cleaning: radera ALLA tidigare körningar innan ny mapp skapas.
  if (existsSync(ROOT)) {
    rmSync(ROOT, { recursive: true, force: true });
  }
  const outDir = join(ROOT, timestamp());
  mkdirSync(outDir, { recursive: true });

  let sessionId: string | null = null;
  let fixtureId: string | null = null;

  if (AUTH_MODE && !BASE_URL.startsWith("https://")) {
    // __Host--prefix kräver en secure (https) origin — Chromium avvisar
    // cookien på http://localhost. Auth-gated verifiering sker därför mot
    // live-deploy (https) per runbookens tre-nivå-policy, inte lokal http.
    throw new Error(
      `Auth-läge kräver https VISUAL_BASE_URL (__Host--cookie). Fick: ${BASE_URL}. ` +
        `Verifiera auth-gated mot live-deploy (t.ex. https://www.jobbpilot.se).`,
    );
  }

  if (AUTH_MODE) {
    console.log("[visual-verify] Auth-läge: login + fixture-setup ...");
    sessionId = await login();
    fixtureId = await createFixture(sessionId);
    console.log("[visual-verify] Fixture-sökning skapad (raderas i teardown).");
  } else {
    console.log("[visual-verify] Publikt läge (inga auth-env satta).");
  }

  const authPages: PageTarget[] = AUTH_MODE
    ? [
        { path: "/jobb", name: "jobb-spara-sokning", auth: true },
        { path: "/sokningar", name: "sokningar-lista", auth: true },
        {
          path: `/sokningar/${fixtureId}`,
          name: "sokningar-kor-resultat",
          auth: true,
        },
      ]
    : [];
  const pages = [...PUBLIC_PAGES, ...authPages];

  const browser = await chromium.launch();
  let count = 0;

  try {
    for (const theme of THEMES) {
      for (const vp of VIEWPORTS) {
        const context: BrowserContext = await browser.newContext({
          viewport: { width: vp.w, height: vp.h },
          colorScheme: theme === "dark" ? "dark" : "light",
        });

        if (AUTH_MODE && sessionId) {
          // Host-only Secure-cookie via `url` → uppfyller __Host--prefix.
          // Endast i browser-context (in-memory); aldrig till disk.
          await context.addCookies([
            {
              name: SESSION_COOKIE,
              value: sessionId,
              url: BASE_URL,
              httpOnly: true,
              secure: true,
              sameSite: "Strict",
            },
          ]);
        }

        const page = await context.newPage();
        for (const target of pages) {
          await page.goto(`${BASE_URL}${target.path}`, {
            waitUntil: "networkidle",
          });
          await shoot(page, outDir, `${target.name}__${theme}__${vp.tag}`);
          count++;
        }

        // Bekräftelse-dialog (DESIGN.md §6) — öppna och capurera öppet
        // tillstånd. Bara om fixture finns (annars ingen rad att radera).
        if (AUTH_MODE) {
          await page.goto(`${BASE_URL}/sokningar`, {
            waitUntil: "networkidle",
          });
          const radera = page.getByRole("button", { name: "Radera" }).first();
          if (await radera.isVisible().catch(() => false)) {
            await radera.click();
            await page
              .getByText("Radera sparad sökning?")
              .waitFor({ state: "visible", timeout: 5000 });
            // Låt overlay/dialog-fade (DESIGN.md §10, 150ms) settla helt så
            // screenshoten inte fångar ett mid-transition-tillstånd.
            await page.waitForTimeout(600);
            await shoot(
              page,
              outDir,
              `sokningar-radera-dialog__${theme}__${vp.tag}`,
            );
            count++;
          }
        }

        await context.close();
      }
    }
  } finally {
    if (AUTH_MODE && sessionId && fixtureId) {
      await deleteFixture(sessionId, fixtureId);
      console.log("[visual-verify] Fixture-sökning raderad (teardown).");
    }
    await browser.close();
  }

  console.log(
    `[visual-verify] ${count} screenshots → ${outDir}\n` +
      `[visual-verify] Raderas automatiskt vid nästa körning. ` +
      (AUTH_MODE
        ? `Auth-gated sidor capturerade mot ${BASE_URL}.`
        : `Auth-gated sidor: verifieras vid live-deploy (se runbook).`),
  );
}

main().catch((err) => {
  console.error("[visual-verify] FEL:", err);
  process.exit(1);
});
