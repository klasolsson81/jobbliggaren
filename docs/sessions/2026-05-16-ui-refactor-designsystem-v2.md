---
session: UI-refactor — designsystem v2
datum: 2026-05-16
slug: ui-refactor-designsystem-v2
status: levererad (commit + push)
commits:
  - 261ea12 feat(web): designsystem v2 — civic slate-palett + dark mode + Shell B + landing
  - <docs-sync> docs: session-end UI-refactor v2
adr:
  - 0037 Designsystem v2 — civic slate-skala + dark mode
---

# UI-refactor — JobbPilot designsystem v2

## Mål

Återskapa `C:\DOTNET-UTB\JobbPilotNEWDESIGN`-handoffen (civic-utility, dark mode,
Shell Variant B, landing) i Next.js-kodbasen och uppdatera alla design-specs
(DESIGN.md, 5 skills, 2 agenter). 8 steg, flera STOPP-gates.

## Levererat per steg

- **Steg 1** Token-migrering: `globals.css` slate `--jp-*`-palett (light +
  `[data-theme="dark"]`), `@custom-variant dark (&:where([data-theme="dark"],…))`,
  Tailwind `@theme inline`→`--jp-*`, shadcn-bridge slate, full `.jp-*`-utility-
  system. `layout.tsx`: JetBrains Mono + Hanken via `next/font`, `data-density`,
  `suppressHydrationWarning`, ThemeScript/ThemeProvider inwirad.
- **Steg 2** DESIGN.md §1.2/§3/§4/§5/§6 → v2 (Klas-GO, dark auto-detect-
  formulering). 5 skills uppdaterade (delegerad subagent). 2 agenter
  (design-reviewer + nextjs-ui-engineer) Klas-GO båda.
- **Steg 3** ThemeProvider Variant A (CTO-beslut): inline no-flash-script +
  `useSyncExternalStore`, localStorage `jp-theme` + `prefers-color-scheme`-
  fallback, noll externa deps.
- **Steg 4** Shell Variant B: `app-shell.tsx` (client) + Server-`(app)/layout`
  bevarar auth/rollgejt. Nav-grupper: Söka jobb / Mina ansökningar / Min profil /
  Administration (Granskning rollgejtad — beslut **A**, endast admin ser den).
- **Steg 5** Landing `(marketing)/page.tsx`: civic two-column, login/register-
  tabs → `/logga-in` / `/registrera`, OAuth-monogram, footer.
- **Steg 6** Primitiv: `status-dot`, `status-pill`, `match-bar`, delad
  `theme-toggle`; shadcn button/input/select alignade (32/28px, 4px, 80ms).
- **Steg 7** Ledger-restyle: /jobb (+job-ad-card/list/filters), /ansokningar
  (+application-card), /cv (+resume-card), /mig (minimal), /admin/granskning
  (+audit-log-table → `.jp-table`).
- **Steg 8** ADR 0037, docs, commit + push.

## Beslut & detours

- **CTO-rond (senior-cto-advisor):** ThemeProvider-strategi = Variant A
  (inline blocking-script + client provider, `useSyncExternalStore`). CTO
  eskalerade en blockerare CC missade: `globals.css` förbjöd dark mode utan
  Klas-GO + ADR (Fas 0-borttagning pga shadcn oklch-violetter). Klas gav GO
  2026-05-16 ("Det är jag som tagit fram denna refactor") → ADR 0037 skapad.
- **STOPP #0:** Klas valde **A** (behåll ADMIN, rollgejtad — vanlig användare
  ser den aldrig). Nav-grupp-namn delegerade till CC (civic-passande).
- **Dark mode auto-detect:** Klas frågade om browserns dark mode respekteras.
  Ja — ThemeScript läser `prefers-color-scheme` när inget eget val finns, före
  paint (ingen flash). Light = default endast utan systempreferens.
- **design-reviewer (veto):** 2 Blockers (WCAG-kontrast: `text-tertiary` på
  informationsbärande empty-state-text + kort/tabell-metadata) + 3 Majors
  (landing aria-disabled + tab-semantik, delad ThemeToggle). Alla åtgärdade
  in-block per §9.6 (samma batch, current phase) — inga TD lyfta.

## Öppen punkt (ej blockerande)

- **`.jp-h1`/`.jp-h2`/display font-weight + display-storlek-drift:**
  `JobbPilotNEWDESIGN/jobbpilot.css` (verbatim-implementerad i `globals.css`)
  använder 500/36px medan `tokens-full.md`/DESIGN.md §4 säger 600/56px. Flaggat
  av design-reviewer + adr-keeper. Kräver Klas auktoritetsbeslut vilken källa
  som gäller. Påverkar inte ADR 0037:s giltighet. docs-keeper noterar vid
  drift-verifiering.

## Verifiering

`tsc --noEmit` rent · `eslint src` 0 errors (3 pre-existing warnings i orörda
filer) · 313/313 vitest · `next build --webpack` grön · pre-commit:
242+354+50 .NET-tester gröna.

## Iteration 2 — Klas-feedback efter live-test (2026-05-16)

Klas underkände v2 visuellt (testade jobbpilot.se på 3440px). Rotorsak:
agent-review granskade kod/diff, inte renderad UI i bred viewport.

**senior-cto-advisor-beslut (5 punkter):**
1. **Ny obligatorisk rutin:** `docs/runbooks/frontend-visual-verification.md` +
   `scripts/visual-verify.ts` + `pnpm visual-verify`. Playwright headless,
   1280/1920/3440 × light/dark, bilder i `C:/tmp/jobbpilot-visual/<ts>/`
   (utanför repot), self-cleaning (raderar tidigare körningar vid start),
   design-reviewer granskar **mot bilderna**, Klas slutgodkänner. Auth-gated
   sidor deferras till live-deploy.
2. Dubbel-login löst: landing-panelen renderar nu **riktiga**
   `LoginForm`/`RegisterForm` (wire mot `loginAction`/`registerAction`),
   `/logga-in`+`/registrera` kvar som fallback.
3. Post-login `/mig` → `/jobb` (`safeRedirectPath` + form-defaults). Översikt
   byggs ej (saknar aggregat-query-dependency) → **TD-82** (Minor, Fas 2).
4. Bred-skärm: `.jp-page` `max-width:1180px; margin-inline:auto`; landing
   centrerad `max-w-[1200px]`-container.
5. Jobb-rad-separation: `border-border-strong` + py-6 + hover-bg + inset
   brand-vänsterkant (inom civic-ledger, ingen card/shadow).

**Verifiering iteration 2:** visual-verify-loop körd (24 shots).
design-reviewer mot bilderna: Klas-klagomål (3440-centrering + dubbel-login)
verifierat **lösta**; 1 Blocker funnen+fixad (auth-sidor tvåtons-band →
`(auth)/layout` `min-h-full`→`min-h-screen`); re-run bekräftade fix.
tsc/lint(0 err)/313 vitest/next build gröna.

**Kräver Klas-GO (ej gjort):**
- Rad i `web/jobbpilot-web/AGENTS.md` som pekar på visual-verification-runbook
  (scaffolding-fil — memory `feedback_dont_delete_auto_files`).
- TD-82 fas-placering (Fas 2 satt — Klas äger roadmap-strategi).

## Iteration 3 — läsbarhets-omkalibrering (ADR 0038, Klas live-test mot Platsbanken)

Klas jämförde /jobb mot Platsbanken side-by-side: v2 för litet/tunt/otydligt.
Rotorsak: v2-handoffen drev BORT från JobbPilots egen spec (DESIGN.md §1.1
55-årig målanvändare + GOV.UK-referens + PRINCIPLES r7 mening 2 "luftigt nog
att läsa" + egen contrast-table som redan dömer text-tertiary).

Web-research: GOV.UK Frontend = 16px brödtextgolv (aldrig under), accessibility
per British Dyslexia Association. Nielsen/WCAG: beskrivande placeholder-exempel
= anti-pattern.

**senior-cto-advisor-beslut + Klas full GO → ADR 0038** (delvis supersession av
0037 — endast typo/density; dark/palett orört):
- Brödtext 14→16px, lede 17, h1/h2/h3 vikt→600 (stänger 0037:s öppna jp-h1-
  drift), mono data 13/500 secondary, mono caps 11.5 secondary, text-tertiary
  ENDAST dekorativt, input 32→44px, knapp 32→40 (sm 36), sök/filter-placeholder-
  exempel borttagna (auth format-placeholder kvar). Global token-fix (DRY/SPOT),
  civic-ledger-FORMEN orörd.

Kedja: adr-keeper (ADR 0038) → general-purpose (skills+PRINCIPLES+README) →
DESIGN.md §4/§6 (spec-guard-token, Klas-GO) → nextjs-ui-engineer (globals.css+
shadcn+placeholder) → visual-verify → design-reviewer mot screenshots: vetade
1 blocker (tertiary-on-info) → fixad (genuina brott → secondary; legitim
secondary INTE plattad — #475569 7.4:1 är designens mellantier) → re-review
**✓ Approved 0 blockers**. tsc/lint(0)/313 vitest gröna. Klas slutgodkände
diff+screenshots före push.

## Nästa session

- Klas-beslut om `.jp-h1`/display font-weight-auktoritet (jobbpilot.css vs
  tokens-spec) → reconcilera globals.css ELLER tokens-skill därefter.
- Visuell QA i browser (light + dark) på alla vyer — `pnpm dev`.
- Ev. Vercel-deploy av v2 (tag-push kräver Klas-GO).
- TD-13 PII-encryption kvarstår som enda Major Fas 2-blocker för v0.2-prod.
