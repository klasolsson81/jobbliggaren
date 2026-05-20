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

/**
 * ADR 0060: RecentJobSearches fångas automatiskt när ListJobAdsQuery
 * kör med filter (post-handler-pipeline-behavior). Vi triggar capture
 * genom att kalla /api/v1/job-ads med ett q-filter. Två separata
 * sökningar görs så /sokningar-listan har minst två rader.
 *
 * Teardown via GET /api/v1/me/recent-searches → DELETE per id, så
 * dev-DB inte växer monotont. Best-effort (varnar men kraschar inte
 * vid teardown-fel).
 */
async function createFixture(sessionId: string): Promise<void> {
  // ADR 0060 Beslut 4 N+1 COUNT-projektion + full-text-q-criteria utan
  // trigram-index på dev → COUNT/rad >25s, /me/recent-searches kan 504:a.
  // Visual-verify för F6 P4a fokuserar därför på empty-state + komponent-
  // chrome (tom hero-chip, tom /sokningar-route). Populerade screenshots
  // är deferred tills BE perf-fix (separat prompt). Login-rensning först
  // — så testkontots ev. tidigare auto-fångade sökningar inte hänger
  // requests under capture.
  const auth = { Authorization: `Bearer ${sessionId}` };
  let listRes: Response;
  try {
    listRes = await fetch(`${BACKEND_URL}/api/v1/me/recent-searches`, {
      headers: auth,
      signal: AbortSignal.timeout(5_000),
    });
  } catch {
    console.warn(
      "[visual-verify] VARNING: kunde inte lista recent-searches för rensning " +
        "(timeout) — fortsätter ändå; renderar troligen empty/error-state.",
    );
    return;
  }
  if (!listRes.ok) return;
  const list = (await listRes.json()) as { id: string }[];
  for (const item of list) {
    await fetch(`${BACKEND_URL}/api/v1/me/recent-searches/${item.id}`, {
      method: "DELETE",
      headers: auth,
    });
  }
}

async function deleteFixture(sessionId: string): Promise<void> {
  const auth = { Authorization: `Bearer ${sessionId}` };
  const listRes = await fetch(`${BACKEND_URL}/api/v1/me/recent-searches`, {
    headers: auth,
  });
  if (!listRes.ok) {
    console.warn(
      `[visual-verify] VARNING: fixture-teardown list misslyckades ` +
        `(HTTP ${listRes.status}) — temp-sökningar kan ligga kvar i dev-DB.`,
    );
    return;
  }
  const list = (await listRes.json()) as { id: string }[];
  for (const item of list) {
    const res = await fetch(
      `${BACKEND_URL}/api/v1/me/recent-searches/${item.id}`,
      { method: "DELETE", headers: auth },
    );
    if (!res.ok) {
      console.warn(
        `[visual-verify] VARNING: teardown ${item.id} misslyckades ` +
          `(HTTP ${res.status}).`,
      );
    }
  }
}

/**
 * FAS 3 STOPP 3b (/ansokningar-omarbetning): redesignens kärna är de **tre
 * jobbidentitets-tillstånden** (ansokningar-redesign-plan.md §7):
 *
 *  1. JobAd-kopplad   → list-rad/H1 "{titel} — {företag}", JobInfoPanel.
 *  2. ManualPosting   → manuell ansökan, Källa=Manuellt, ingen "Publicerad"-rad (J1).
 *  3. Fallback        → cover-letter-only, "Ansökan #{kort-id}", civic-not.
 *
 * Gammal fixtur skapade ENDAST tillstånd 3 ({jobAdId:null, coverLetter}) →
 * design-reviewer/Klas hade aldrig sett redesignens primärväg (tillstånd 1/2).
 * Det är exakt Batch-6-VETO-grunden (korpus saknar bild av ändrad yta). Denna
 * fixtur skapar alla tre + behåller en Pending follow-up på tillstånd 3 så att
 * `RecordFollowUpOutcomeForm` (FAS 3 Batch 1) fortfarande capureras.
 *
 * Ingen DELETE-endpoint finns för applications (medvetet, soft-delete-domän,
 * ej API-exponerad) → teardown är best-effort: de syntetiska fixtur-ansökningarna
 * blir kvar i dev-DB på dev-test-kontot (syntetiskt, ingen PII). Konsekvent
 * med runbookens saved-search-teardown-not.
 */
interface ApplicationFixtures {
  /** Tillstånd 1 — JobAd-kopplad. null om dev-korpusen saknar träff. */
  jobAdLinked: string | null;
  /** Tillstånd 2 — ManualPosting med URL (L5: "Visa annonsen"-länk visas). */
  manual: string;
  /** Tillstånd 2b — ManualPosting UTAN URL (L5: ingen länk — VETO Block 1). */
  manualNoUrl: string;
  /** Tillstånd 3 — cover-letter-only fallback + Pending follow-up. */
  fallback: string;
  /**
   * Submitted-status — radiogrupp med flera (Acknowledged/Nekad/Återtagen)
   * varav destruktiva. Draft-fixturerna ger bara 1-övergångs-knappen; §5:s
   * kärna (shadcn radio-group + L2 destruktiv-Dialog) capurerades aldrig
   * (Area 5-VETO L2 ej clear:ad).
   */
  submitted: string;
}

/** Plockar ett JobAd-id ur live dev-korpusen för tillstånd-1-fixturen. */
async function firstJobAdId(sessionId: string): Promise<string | null> {
  const res = await fetch(
    `${BACKEND_URL}/api/v1/job-ads/?page=1&pageSize=1&q=utvecklare`,
    { headers: { Authorization: `Bearer ${sessionId}` } },
  );
  if (!res.ok) {
    console.warn(
      `[visual-verify] VARNING: kunde inte lista job-ads (HTTP ${res.status}) ` +
        `— tillstånd 1 (JobAd-kopplad) hoppas över.`,
    );
    return null;
  }
  const data = (await res.json()) as { items?: { id?: string }[] };
  const id = data.items?.[0]?.id ?? null;
  if (!id) {
    console.warn(
      "[visual-verify] VARNING: dev-korpusen gav ingen job-ad-träff " +
        "— tillstånd 1 (JobAd-kopplad) hoppas över.",
    );
  }
  return id;
}

async function postApplication(
  auth: Record<string, string>,
  body: Record<string, unknown>,
): Promise<string> {
  const res = await fetch(`${BACKEND_URL}/api/v1/applications`, {
    method: "POST",
    headers: auth,
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    throw new Error(
      `Skapa fixture-ansökan misslyckades (HTTP ${res.status}).`,
    );
  }
  const created = (await res.json()) as { id?: string };
  if (!created.id) {
    throw new Error("Create-svar saknar ansöknings-id.");
  }
  return created.id;
}

async function createApplicationFixture(
  sessionId: string,
): Promise<ApplicationFixtures> {
  const auth = {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
  const stamp = timestamp();

  // Tillstånd 1 — JobAd-kopplad (om dev-korpusen har en träff).
  const jobAdId = await firstJobAdId(sessionId);
  const jobAdLinked = jobAdId
    ? await postApplication(auth, {
        jobAdId,
        coverLetter: `FAS 3 visuell verifiering — JobAd-kopplad (temp ${stamp})`,
      })
    : null;

  // Tillstånd 2 — ManualPosting (manuell ansökan, Källa=Manuellt).
  const expiresAt = new Date(
    Date.now() + 30 * 24 * 60 * 60 * 1000,
  ).toISOString();
  const manual = await postApplication(auth, {
    jobAdId: null,
    coverLetter: `FAS 3 visuell verifiering — manuell m. URL (temp ${stamp})`,
    manual: {
      title: "Frontendutvecklare",
      company: "Exempelbolaget AB",
      url: "https://example.com/jobb/visuell-verifiering",
      expiresAt,
    },
  });

  // Tillstånd 2b — ManualPosting UTAN url → L5: ingen "Visa annonsen"-länk
  // (Area 5-VETO Block 1: korpusen saknade detta tillstånd, vilket gjorde
  // länken-med-url till en falsk Block. Båda manuell-varianterna behövs).
  const manualNoUrl = await postApplication(auth, {
    jobAdId: null,
    coverLetter: `FAS 3 visuell verifiering — manuell utan URL (temp ${stamp})`,
    manual: {
      title: "Backendutvecklare",
      company: "Annat Exempelbolag AB",
      url: null,
      expiresAt: null,
    },
  });

  // Submitted — radiogrupp >1 övergång inkl. destruktiva (L2). Skapa Draft
  // och transitera Draft→Submitted (Submitted = enda Draft-övergången).
  const submitted = await postApplication(auth, {
    jobAdId: null,
    coverLetter: `FAS 3 visuell verifiering — Submitted/L2 (temp ${stamp})`,
    manual: {
      title: "Systemutvecklare",
      company: "Statusexempel AB",
      url: null,
      expiresAt: null,
    },
  });
  const transRes = await fetch(
    `${BACKEND_URL}/api/v1/applications/${submitted}/transition`,
    {
      method: "POST",
      headers: auth,
      body: JSON.stringify({ targetStatus: "Submitted" }),
    },
  );
  if (!transRes.ok) {
    throw new Error(
      `Transition Draft→Submitted misslyckades (HTTP ${transRes.status}).`,
    );
  }

  // Tillstånd 3 — cover-letter-only fallback + Pending follow-up
  // (RecordFollowUpOutcomeForm renderas endast när fu.outcome === "Pending").
  const fallback = await postApplication(auth, {
    jobAdId: null,
    coverLetter: `FAS 3 visuell verifiering — fallback (temp ${stamp})`,
  });
  const followUpRes = await fetch(
    `${BACKEND_URL}/api/v1/applications/${fallback}/follow-ups`,
    {
      method: "POST",
      headers: auth,
      body: JSON.stringify({
        channel: "Email",
        scheduledAt: new Date().toISOString(),
        note: "Visuell verifiering — väntar svar",
      }),
    },
  );
  if (!followUpRes.ok) {
    throw new Error(
      `Skapa fixture-follow-up misslyckades (HTTP ${followUpRes.status}).`,
    );
  }

  return { jobAdLinked, manual, manualNoUrl, fallback, submitted };
}

/**
 * Tema appliceras av en pre-paint ThemeScript som sätter `data-theme="dark"`
 * på <html> (dark) eller tar bort attributet (light) — styrt av Playwrights
 * colorScheme-emulering. Vissa sidladdningar capturerades i fel tema när
 * screenshoten fyrade före ThemeScript/hydration (FAS 3 STOPP 3b Area 5-VETO
 * Major 1: jobad-kopplad__dark renderades light). Vänta deterministiskt tills
 * upplöst tema = begärt tema innan shoot. Timeout → LOUD varning (aldrig en
 * tyst fel-tema-bild som passerar review).
 */
async function ensureTheme(page: Page, theme: string): Promise<void> {
  const want = theme === "dark" ? "dark" : null; // light = attribut frånvarande
  try {
    await page.waitForFunction(
      (w) =>
        (document.documentElement.getAttribute("data-theme") ?? null) === w,
      want,
      { timeout: 4000 },
    );
  } catch {
    console.warn(
      `[visual-verify] VARNING: tema '${theme}' ej applicerat på ` +
        `${page.url()} inom timeout — skärmbilden kan ha fel tema.`,
    );
  }
}

async function shoot(page: Page, outDir: string, name: string): Promise<void> {
  await page.screenshot({ path: join(outDir, `${name}.png`), fullPage: true });
}

/**
 * Interaktiva states på /jobb — F4 Platsbanken-popover (ADR 0055 + amendment
 * 2026-05-19). Verktyget tar annars bara sidladdnings-screenshots;
 * design-reviewer bindande rendered-veto kräver popover-öppet-tillståndet i
 * korpus (F4:s centrala leverans). Selektorerna mot v2 JobAdFilters
 * (disclosure/typeahead/OccupationPicker-select) är BORTTAGNA — de UI:na
 * existerar inte efter F4 (JobAdFilters + pickers raderade).
 *
 * Robusta selektorer (role/aria/text):
 *  - Hero-pill: `<button class="jp-hero-pill">Yrke|Ort</button>` med
 *    aria-haspopup="dialog" + aria-expanded (jobb-hero-filters).
 *  - Popover: role="dialog" + aria-label (jobb-filter-popover). Yrke =
 *    tvåkolumns (vänsterkolumn role="listbox"); Ort = enkelkolumns Län.
 *
 * Gated till 1280 + 3440 (breddspann); körs i light+dark via THEME-loopen.
 * Best-effort: en miss fäller inte hela körningen (defensiv catch → varning,
 * loggas så design-reviewer ser luckan). Filter-pill capureras INTE —
 * deferred helt per ADR 0055-amendment (existerar ej i UI).
 */
async function shootJobbInteractiveStates(
  page: Page,
  outDir: string,
  theme: string,
  vpTag: string,
): Promise<number> {
  let shot = 0;

  // State 1 — Yrke-popover öppen (tvåkolumns Yrkesområde→Yrken).
  // Behåller namnet `jobb-filter-expanded` (design-reviewer/manifest
  // refererar det som popover-öppet-tillståndet).
  try {
    await page.goto(`${BASE_URL}/jobb`, { waitUntil: "load", timeout: 15_000 });
    const yrkePill = page
      .getByRole("button", { name: /^Yrke/ })
      .first();
    await yrkePill.waitFor({ state: "visible", timeout: 5000 });
    if ((await yrkePill.getAttribute("aria-expanded")) !== "true") {
      await yrkePill.click();
    }
    // Popovern är role="dialog"; vänster kategorikolumn är role="listbox"
    // (jobb-filter-popover tvåkolumns) — bevisar att popovern faktiskt
    // renderat, inte bara att klicket gick igenom.
    await page
      .getByRole("dialog")
      .first()
      .waitFor({ state: "visible", timeout: 5000 });
    await page
      .getByRole("listbox")
      .first()
      .waitFor({ state: "visible", timeout: 5000 });
    // Fade/rise-animation (DESIGN.md §10) settlar.
    await page.waitForTimeout(300);
    await shoot(page, outDir, `jobb-filter-expanded__${theme}__${vpTag}`);
    shot++;
  } catch (err) {
    console.warn(
      `[visual-verify] VARNING: jobb-filter-expanded (${theme}/${vpTag}) ` +
        `kunde inte capureras: ${(err as Error).message}`,
    );
  }

  // State 3 — Jobbmodalen öppen (parsa-d annons-text per Prompt 2).
  // Klickar på första jobb-raden → @modal/(.)jobb/[id] fångar och visar
  // modal med JobAdDetail-komponenten. Verifierar att formatAdDescription
  // renderar h3/p/ul-struktur från råtexten.
  try {
    await page.goto(`${BASE_URL}/jobb`, { waitUntil: "load", timeout: 15_000 });
    const firstRow = page.locator(".jp-job").first();
    await firstRow.waitFor({ state: "visible", timeout: 5000 });
    await firstRow.click();
    // Modalen är role="dialog" (ADR 0053). aria-labelledby pekar på
    // titel-element så name-matchning kan vara ad-titel; använd generisk dialog.
    await page
      .getByRole("dialog")
      .first()
      .waitFor({ state: "visible", timeout: 5000 });
    // Vänta in description-elementet (parsad markup).
    await page
      .locator("#jp-modal-desc")
      .waitFor({ state: "visible", timeout: 5000 });
    await page.waitForTimeout(300);
    await shoot(page, outDir, `jobb-modal-detalj__${theme}__${vpTag}`);
    shot++;
  } catch (err) {
    console.warn(
      `[visual-verify] VARNING: jobb-modal-detalj (${theme}/${vpTag}) ` +
        `kunde inte capureras: ${(err as Error).message}`,
    );
  }

  // State 2 — Ort-popover öppen (enkelkolumns Län; ADR 0055-amendment —
  // ingen kommun-nivå, regions enkelnivå).
  try {
    await page.goto(`${BASE_URL}/jobb`, { waitUntil: "load", timeout: 15_000 });
    const ortPill = page
      .getByRole("button", { name: /^Ort/ })
      .first();
    await ortPill.waitFor({ state: "visible", timeout: 5000 });
    if ((await ortPill.getAttribute("aria-expanded")) !== "true") {
      await ortPill.click();
    }
    await page
      .getByRole("dialog")
      .first()
      .waitFor({ state: "visible", timeout: 5000 });
    // Enkelkolumns Ort: "Välj alla län"-checkitem bevisar Län-listan
    // renderat (role="checkbox", jobb-filter-popover enkelkolumns).
    await page
      .getByRole("checkbox", { name: /Välj alla län/ })
      .first()
      .waitFor({ state: "visible", timeout: 5000 });
    await page.waitForTimeout(300);
    await shoot(page, outDir, `jobb-ort-popover__${theme}__${vpTag}`);
    shot++;
  } catch (err) {
    console.warn(
      `[visual-verify] VARNING: jobb-ort-popover (${theme}/${vpTag}) ` +
        `kunde inte capureras: ${(err as Error).message}`,
    );
  }

  return shot;
}

/**
 * FAS 3 STOPP 3b §5/L2 — StatusEditCard destruktiv övergång. Submitted-status
 * ger radiogrupp [Bekräftad/Nekad/Återtagen]. Capurerar: (1) radiogrupp +
 * vald destruktiv → inline konsekvenstext, (2) [Spara] → Dialog-bekräftelse
 * öppen (L2 bindande: destruktiv MÅSTE gå via Dialog, ej inline-istället).
 * Area 5-VETO L2 var "ej clear:ad" — alla tidigare fixturer var Draft.
 * Best-effort: en miss fäller ej körningen (loggas så luckan syns).
 */
async function shootStatusDestructiveStates(
  page: Page,
  outDir: string,
  theme: string,
  vpTag: string,
  submittedAppId: string,
): Promise<number> {
  let shot = 0;
  try {
    await page.goto(`${BASE_URL}/ansokningar/${submittedAppId}`, {
      waitUntil: "load",
      timeout: 15_000,
    });
    await ensureTheme(page, theme);
    // "Nekad" = destruktiv (Rejected). Label kopplad via <label htmlFor>.
    const nekad = page.getByRole("radio", { name: "Nekad" });
    await nekad.waitFor({ state: "visible", timeout: 5000 });
    await nekad.check();
    // Inline konsekvenstext renderas när destruktivt val gjorts.
    await page
      .getByText(/avslutar\s+ansökan/i)
      .first()
      .waitFor({ state: "visible", timeout: 5000 });
    await page.waitForTimeout(150);
    await shoot(page, outDir, `ansokningar-status-destruktiv-inline__${theme}__${vpTag}`);
    shot++;

    await page.getByRole("button", { name: "Spara", exact: true }).click();
    // L2: Dialog-bekräftelse — DialogTitle "Markera som Nekad?".
    await page
      .getByRole("dialog")
      .getByText(/^Markera som Nekad\?$/)
      .waitFor({ state: "visible", timeout: 5000 });
    await page.waitForTimeout(600); // overlay/dialog-fade settlar (DESIGN.md §10)
    await shoot(page, outDir, `ansokningar-status-destruktiv-dialog__${theme}__${vpTag}`);
    shot++;
  } catch (err) {
    console.warn(
      `[visual-verify] VARNING: status-destruktiv (${theme}/${vpTag}) ` +
        `kunde inte capureras: ${(err as Error).message}`,
    );
  }
  return shot;
}

async function main(): Promise<void> {
  // Self-cleaning: radera ALLA tidigare körningar innan ny mapp skapas.
  if (existsSync(ROOT)) {
    rmSync(ROOT, { recursive: true, force: true });
  }
  const outDir = join(ROOT, timestamp());
  mkdirSync(outDir, { recursive: true });

  let sessionId: string | null = null;
  let appFixtures: ApplicationFixtures | null = null;

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
    await createFixture(sessionId);
    appFixtures = await createApplicationFixture(sessionId);
    console.log(
      "[visual-verify] Fixturer skapade (RecentJobSearches: empty-state " +
        "capture — populerade screenshots deferred tills BE perf-fix för " +
        "N+1 COUNT-projektion; 5 ansökningar " +
        "[JobAd-kopplad/manuell±url/fallback/Submitted]: best-effort, ingen " +
        "DELETE-endpoint — se funktions-doc).",
    );
  } else {
    console.log("[visual-verify] Publikt läge (inga auth-env satta).");
  }

  const authPages: PageTarget[] = AUTH_MODE
    ? [
        { path: "/jobb", name: "jobb-hero-recent-chip", auth: true },
        { path: "/sokningar", name: "sokningar-lista", auth: true },
        { path: "/ansokningar", name: "ansokningar-lista", auth: true },
        { path: "/ansokningar/ny", name: "ansokningar-ny", auth: true },
        { path: "/installningar", name: "installningar", auth: true },
        { path: "/cv", name: "cv-lista", auth: true },
        ...(appFixtures?.jobAdLinked
          ? [
              {
                path: `/ansokningar/${appFixtures.jobAdLinked}`,
                name: "ansokningar-detalj-jobad-kopplad",
                auth: true,
              } as PageTarget,
            ]
          : []),
        {
          path: `/ansokningar/${appFixtures!.manual}`,
          name: "ansokningar-detalj-manuell",
          auth: true,
        },
        {
          path: `/ansokningar/${appFixtures!.manualNoUrl}`,
          name: "ansokningar-detalj-manuell-utan-url",
          auth: true,
        },
        {
          path: `/ansokningar/${appFixtures!.fallback}`,
          name: "ansokningar-detalj-fallback-outcome-form",
          auth: true,
        },
        {
          path: `/ansokningar/${appFixtures!.submitted}`,
          name: "ansokningar-detalj-submitted-radiogrupp",
          auth: true,
        },
      ]
    : [];
  const pages = [...PUBLIC_PAGES, ...authPages];

  const browser = await chromium.launch();
  let count = 0;
  // Per-sida-felisolering (CTO Variant C 2026-05-19): en route som
  // degraderar (t.ex. pollande sida som ej når `load` inom timeout) ska
  // logga + skippas, inte abortera hela korpusen (Meszaros 2007 — Robust
  // Fixture; matchar try/catch-mönstret i interaktions-helpers ovan).
  const skipped: string[] = [];

  try {
    for (const theme of THEMES) {
      for (const vp of VIEWPORTS) {
        const context: BrowserContext = await browser.newContext({
          viewport: { width: vp.w, height: vp.h },
          colorScheme: theme === "dark" ? "dark" : "light",
        });

        // Deterministisk tema-setning (CTO a71bc82bd838fb0ae, FAS 3 STOPP 3b
        // Area 5-VETO Major 1). ThemeScript läser localStorage["jp-theme"]
        // FÖRE matchMedia → att sätta den via addInitScript (körs före varje
        // dokuments scripts) gör temat oberoende av CDP colorScheme-
        // emuleringen, som bevisat var bräcklig för en route med extern
        // target=_blank-kontext (jobad-kopplad gav byte-identisk light==dark
        // över 3 körningar; produktkod verifierat invariant). Meszaros 2007
        // "Fresh Fixture" — deterministisk setup, ej emuleringsberoende.
        await context.addInitScript((t: string) => {
          try {
            localStorage.setItem("jp-theme", t);
          } catch {
            // localStorage blockerat — colorScheme-emuleringen kvarstår fallback
          }
        }, theme === "dark" ? "dark" : "light");

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
          const label = `${target.name}__${theme}__${vp.tag}`;
          try {
            await page.goto(`${BASE_URL}${target.path}`, {
              waitUntil: "load",
              timeout: 15_000,
            });
            await ensureTheme(page, theme);
            await shoot(page, outDir, label);
            count++;
          } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            console.error(
              `[visual-verify] SKIPPAD ${label} (${target.path}): ${msg}`,
            );
            skipped.push(label);
          }
        }

        // ADR 0042 Beslut B/C — interaktiva /jobb-states (disclosure/
        // typeahead/chip). Auth-gated sida → bara i auth-läge. Gated till
        // breddspannet 1280 + 3440 per uppdragskrav (1920 utelämnas medvetet
        // för att hålla körtiden nere — mellanbredden tillför inget för
        // dessa interaktionstillstånd).
        if (AUTH_MODE && (vp.tag === "1280" || vp.tag === "3440")) {
          count += await shootJobbInteractiveStates(
            page,
            outDir,
            theme,
            vp.tag,
          );
          // FAS 3 §5/L2 — destruktiv övergång + Dialog (Area 5-VETO L2).
          if (appFixtures?.submitted) {
            count += await shootStatusDestructiveStates(
              page,
              outDir,
              theme,
              vp.tag,
              appFixtures.submitted,
            );
          }
        }

        // ADR 0060 — Senaste-sökningar-hero-chip-dropdown (öppet tillstånd).
        // Trigger på /jobb-hero vänster. Gated till breddspannet 1280 + 3440
        // (samma anledning som shootJobbInteractiveStates).
        if (AUTH_MODE && (vp.tag === "1280" || vp.tag === "3440")) {
          await page.goto(`${BASE_URL}/jobb`, {
            waitUntil: "load",
            timeout: 15_000,
          });
          const trigger = page
            .getByRole("button", { name: /Senaste sökningar/i })
            .first();
          if (await trigger.isVisible().catch(() => false)) {
            await trigger.click();
            await page.waitForTimeout(200);
            await shoot(
              page,
              outDir,
              `jobb-hero-recent-chip-open__${theme}__${vp.tag}`,
            );
            count++;
          }
        }

        await context.close();
      }
    }
  } finally {
    if (AUTH_MODE && sessionId) {
      await deleteFixture(sessionId);
      console.log("[visual-verify] Fixture-sökning raderad (teardown).");
    }
    if (AUTH_MODE && appFixtures) {
      const ids = [
        appFixtures.jobAdLinked,
        appFixtures.manual,
        appFixtures.manualNoUrl,
        appFixtures.fallback,
        appFixtures.submitted,
      ]
        .filter(Boolean)
        .join(", ");
      console.warn(
        `[visual-verify] NOT: fixture-ansökningar (${ids}) blir kvar i ` +
          `dev-DB (ingen DELETE-endpoint för applications, soft-delete-domän). ` +
          `Syntetiskt dev-test-konto, ingen PII — acceptabelt per runbook.`,
      );
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
  if (skipped.length > 0) {
    console.warn(
      `[visual-verify] ${skipped.length} SKIPPADE (degraderad route, ` +
        `noteras som lucka för design-reviewer): ${skipped.join(", ")}`,
    );
  }
}

main().catch((err) => {
  console.error("[visual-verify] FEL:", err);
  process.exit(1);
});
