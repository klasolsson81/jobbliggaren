# Code-review: FAS 3 STOPP 3b — /ansokningar-omarbetning (frontend)

**Status:** CHANGES REQUESTED
**Granskat:** 2026-05-18
**Auktoritet:** CLAUDE.md §4 (TS/Next.js), §5.2 (FE-anti-patterns), §7 (test), §10 (svensk copy) + plan-spec-trohet (`docs/design/ansokningar-redesign-plan.md` §2/§3/§5/§7/§8)
**Scope:** Frontend — `(app)/ansokningar/*`, 3 nya komponenter, radio-group-primitiv, lib-actions/schemas/status, tester, e2e. Backend orört (verifierat). EJ design-estetik/render (design-reviewer render-VETO separat, ADR 0047 Area 5).

---

## Sammanfattning

Hög kodkvalitet genomgående. Clean Arch frontend intakt, ingen `any`, server-component-default bevarad, deterministiska tester, svensk civic-copy korrekt, plan-spec-trohet stark (L1/L2/L6/J1/Variant A alla uppfyllda). **En Major:** dead-code-raderingen är ofullständig — de tre orphaned-filerna finns kvar på disk trots att planen (§2/§9) föreskriver radering och STOPP-uppdraget kräver grep-bevisad komplett radering. Inga Blockers. 3 Minor.

- **Blocker:** 0
- **Major:** 1 (orphaned dead-code ej raderad)
- **Minor:** 3 (in-block §9.6)

---

## Major (måste åtgärdas innan commit)

### M1. Orphaned dead-code ej raderad — radering ofullständig

**Filer kvar på disk (ej i `git status` som `D`):**
- `web/jobbpilot-web/src/components/applications/application-card.tsx`
- `web/jobbpilot-web/src/components/applications/application-status-badge.tsx`
- `web/jobbpilot-web/src/components/applications/status-card.tsx`

**Grep-bevis (0 produktionsreferens — radering är säker):**

`status-card.tsx`: referens endast från sig själv + en kommentar i `status-edit-card.tsx:40` ("ersätter StatusCard"). `status-card.test.tsx` redan raderad i denna diff. → **Säkert orphaned.**

`application-card.tsx`: referens endast från sig själv + kommentar i `application-row.tsx:25` ("ersätter ApplicationCard"). Importeras av ingen produktionsyta (page.tsx ×3 importerar `ApplicationRow`, ej `ApplicationCard`). → **Säkert orphaned.**

`application-status-badge.tsx`: enda icke-själv-referensen är `application-card.tsx:2` (`import { ApplicationStatusBadge } from "./application-status-badge"`). Greppat hela `web/jobbpilot-web` — INGEN annan yta använder badgen (ej list, detalj, ny, eller annan komponent). När `application-card.tsx` raderas är badgen fullständigt orphaned. → **Säkert orphaned, bekräftat att ingen annan yta använder den (ej bara application-card-kedjan).**

**Problem:** Planen §2 ("Gammal `ApplicationCard` raderas om orphaned (§9.6 dead-code)") och §9 ("ev. radera orphaned `application-card.tsx`/`status-card.tsx`, grep-bevis 0 ref") föreskriver radering. STOPP-uppdraget kräver explicit komplett radering med grep-bevis. Att lämna kvar de tre filerna lämnar död kod i trädet — bryter mot dead-code-precedensen (transition-form-precedensen, §9.6) och ger falsk yta vid framtida sökningar/refactors.

**Krävs:** Radera de tre filerna i samma commit-batch (`git rm`). Verifiera `tsc`/`vitest` fortsatt 0/grönt efter radering (inga test-filer kvar som importerar dem — `status-card.test.tsx` redan borta; `application-card`/`application-status-badge` har inga test-filer kvar enligt `ls`).
**Motivering:** CLAUDE.md §9.6 dead-code-disciplin + plan §2/§9 spec-trohet.
**Delegera till:** nextjs-ui-engineer (trivial `git rm` + verifiering, in-block).

---

## Minor (in-block §9.6 — fixas i samma batch)

### m1. `assertNever`-grenar efter `redirect()` saknar `break`/`return` men är korrekta — dokumentationsvärt

`page.tsx:32-33` och `[id]/page.tsx:27-30`: `case "unauthorized": redirect("/logga-in");` faller igenom till nästa case utan `break`. Detta är korrekt eftersom Next.js `redirect()` kastar internt (`NEXT_REDIRECT`), men en läsare som inte vet det ser ut som en fallthrough-bug. Befintligt mönster i kodbasen — ingen regression, men en kort kommentar (`// redirect() kastar — ingen fallthrough`) höjer läsbarheten. Icke-blockerande.

### m2. `assertNever`-grenens fallthrough täcker `notFound`/`forbidden`/`error` ihop — OK men `notFound` i `[id]/page.tsx` likadant

`[id]/page.tsx:29-30`: `case "notFound": notFound();` samma mönster (kastar). Konsekvent med m1 — samma kommentar-rekommendation. Ingen funktionell brist.

### m3. `radio-group.test.tsx:106-113` testnamn vs assertion-glapp

Testet heter `"carries the global focus-visible ring utility on items (no per-component token)"` men asserterar endast `toHaveClass("rounded-pill")` — det verifierar pill-formen, inte focus-visible-ringen (som kommenteras bort som "global *:focus-visible-ring sätts ej per komponent"). Assertionen är inte fel, men namnet lovar mer än testet bevisar. Byt testnamn till något som matchar assertionen (t.ex. `"renders the radio indicator with pill geometry"`) eller lägg till en assertion som faktiskt rör focus-utility. Fragil-test-risk är låg; detta är en namngivnings-precision-Minor.

---

## Granskning per område

### Clean Architecture frontend — PASS
- `page.tsx`, `[id]/page.tsx` är server-components (ingen `"use client"`); auth (`getServerSession` → `redirect`), datahämtning (`getPipeline`/`getApplicationById`) och alla error-cases (`unauthorized`/`rateLimited`/`notFound`/`forbidden`/`error` + `assertNever` exhaustivitet) intakta.
- `ny/page.tsx` `"use client"` med motiverande kommentar (`useActionState`, rad 1) — §4.3/§5.2-konformt.
- `status-edit-card.tsx`, `job-info-panel.tsx`, `radio-group.tsx` `"use client"` — befogat (interaktivitet: `useTransition`/`useState`/disclosure/Radix). Komment-rationale finns i komponent-docblock.
- **Ingen `useEffect` för datahämtning** (§5.2) — bekräftat: `useState`/`useTransition`/`useId` används, ingen `useEffect` i någon ny fil. Server-action-mönster (`transitionStatusAction`, `createApplicationAction`) konsekvent med befintlig kodbas.

### TypeScript-konventioner (§4) — PASS
- **Ingen `any`** i någon ny/ändrad fil. `unknown` + cast med kommentar i `applications.create-payload.test.ts:74` (`as unknown as [string, RequestInit]` — testfil, motiverat fetch-mock-introspektion).
- Namngivning/fil-org (§4.2): komponenter `PascalCase.tsx` en-export (`ApplicationRow`, `JobInfoPanel`, `StatusEditCard`, `RadioGroup`/`RadioGroupItem` — sammanhörande primitiv-par, etablerat shadcn-mönster i kodbasen). Tester co-lokaliserade (`*.test.tsx` bredvid). Helpers i `lib/applications/status.ts` (`getSourceLabel`, `formatSvDate`) korrekt placerade.
- `strict`-konformt: `useState<ApplicationStatus | "">`, diskriminerade union-resultat (`ActionResult`), `Partial<>`-fabriker i tester. Inga implicita returer.

### Plan-spec-trohet — PASS
- **Zod `createApplicationSchema`** (§7): `title`/`company` obligatoriska (trim + min(1) + max(200)), `url` scheme-validering (`http://`/`https://` refine + `z.url()`), `expiresAt` frivillig + `Date.parse`-validering, `coverLetter` frivillig max 5000. Tom-sträng → `undefined`-transform korrekt. Matchar backend `ManualPostingInput`-kontrakt.
- **`createApplicationAction`** skickar `manual:{title,company,url,expiresAt}` + `coverLetter`, **ingen `source`** (Source struken konsekvent — verifierat i action-kod + `create-payload.test.ts:85,113` `expect(body).not.toHaveProperty("source")`).
- **J1** (`JobInfoPanel`): "Publicerad"-raden renderas endast `published &&` (rad 52) — utelämnas helt när `publishedAt == null`. Ingen `CreatedAt`-läcka (komponenten tar `jobAd.publishedAt`, aldrig application-fält). Test `job-info-panel.test.tsx:28-37` låser invarianten.
- **Variant A** (`StatusEditCard`): en `[Spara]`-knapp, disabled tills `selected !== "" && selected !== currentStatus` (rad 209); 1-övergång → enskild primär knapp (rad 121-154); 0-övergångar → civic-text "Den här ansökan är avslutad och kan inte ändras." (ej intern term "slutläge"); destruktiv → Dialog bevarad (rad 221-264) + additiv inline-konsekvenstext (rad 189-194, ersätter ej dialogen). L1 synlig instruktionsrad via `aria-labelledby={instructionId}` (rad 159-170). L2-mönster korrekt.
- **L6** (`[id]/page.tsx:120-150`): `jobAd == null` → single-column, civic-not "Ingen kopplad annons — manuellt skapad ansökan", StatusEditCard + cover-letter full-width. Ingen tom vänsterkolumn.
- **Fallback** (`ApplicationRow`): `jobAd == null` → `Ansökan #${id.slice(0,8)}` font-mono (rad 36-38, 48-56). Detalj-H1 likadant (`[id]/page.tsx:73,106`).
- **jobAdId-villkorad ny-form:** `/ansokningar/ny` skapar alltid manuell ansökan (jobAdId == null), kommenterat i `applications.ts`. "Inget Källa-fält" bekräftat (formuläret har title/company/url/expiresAt/coverLetter, ingen source-input).

### Test (§7) — PASS (med m3 Minor)
- Happy + failure + edge genomgående: `application-row.test.tsx` (jobAd present/null, StatusDot ej pill, expiresAt present/null, hel-rad-länk); `job-info-panel.test.tsx` (J1 published null/set, källa-label, extern länk rel/target/aria, url null, disclosure-toggle aria-expanded); `status-edit-card.test.tsx` (pill ej self-radio, L1 aria-labelledby, endast tillåtna övergångar, Variant A disabled-logik, destruktiv dialog-gate, inline-konsekvens, avbryt, fel-alert); `radio-group.test.tsx` (roll, onValueChange, controlled, roving tabindex, disabled).
- Deterministiska: fasta datum-strängar, `vi.fn`-mockar med `mockReset` i `beforeEach`, ingen `Date.now`/timing-beroende.
- Mock-mönster konsekvent med repo (server-action via `vi.mock`-factory, dokumenterad referens till `delete-account-dialog.test`/`record-follow-up-outcome-form.test`). **Radix-mock-skäl dokumenterat** (`status-edit-card.test.tsx:26-31`: äkta RadioGroup+Dialog, klick-driven, ej pil-nav som kräver roving-fokus i jsdom — välmotiverat).
- `create-payload.test.ts`: kontraktslås `manual`-payload + `not.toHaveProperty("source")` + validation-fel-utan-fetch. NEXT_REDIRECT-throw-mock dokumenterad (rad 8-11). Ingen täckningssänkning (`status-card.test.tsx` → `status-edit-card.test.tsx` ersatt, 447/447).

### Svensk copy (§10) — PASS
- "du"-tilltal genomgående ("Du kan komplettera...", "Du har gjort..."). Ingen emoji, inga utropstecken i någon ny komponent/copy. Datum `toLocaleDateString("sv-SE", {day:"numeric",month:"short",year:"numeric"})` → "18 maj 2026" (§10.2). Felmeddelanden informativa ej skyllande ("Jobbtitel krävs.", "Annonslänken måste börja med http:// eller https://."). `getSourceLabel`: "Manual" → "Manuellt" (svensk etikett, ej rå literal i UI).
- **Cross-ref-risk (memory `project_crossref_badge_status`):** `STATUS_LABELS`/`STATUS_BADGE_VARIANT`/`ALLOWED_TRANSITIONS` oförändrade i denna diff — ingen ny etikett-drift mot backend-SmartEnum införd. Ingen åtgärd krävs i denna touch.

### Token-disciplin (§8 — disciplin, ej estetik) — PASS
- Inga hex-värden, inga inline-`px` (utom Tailwind-spacing-utilities som `gap-6`/`px-4`/`py-3`/`size-4` — token-backade). Färger via `text-text-*`/`border-border-*`/`bg-surface-*`/`brand-600`/`danger-700`. `font-mono` för id/datum per plan §2. `rounded-md`/`rounded-pill`/`rounded-sm` token-radius. Inga shadow/gradient. (Render-estetik = design-reviewer VETO separat.)

### Call-site/import-integritet — PASS
- `page.tsx` importerar `ApplicationRow` (ej raderad orphaned). `[id]/page.tsx` importerar `StatusEditCard`/`JobInfoPanel`. `status-dot`/`status-pill`-API verifierat (`StatusTone`/`PillTone`/`StatusDot`/`StatusPill` exporteras). Inga brutna imports efter komponent-ersättning. `tsc` 0 bekräftar.

### e2e-spec — PASS
- UX-mappnings-kommentar (rad 16-31) dokumenterar selektor-ändringar utan att försvaga scenariointention. Disclosure-borttagning → enskild knapp/radiogrupp-selektorer; tom-submit → nytt scenario som verifierar att klientvalidering stannar kvar (ny täckning, ej borttagen); destruktiv-flöde behåller dialog-bekräftelse-assertion. Scenariointention bevarad, täckning ej försvagad (snarare utökad: "skapad ansökan dyker upp som rad").

---

## Delegationer

| Fynd | Åtgärd | Ägare |
|---|---|---|
| M1 orphaned dead-code | `git rm` 3 filer + verifiera tsc/vitest, samma batch | nextjs-ui-engineer |
| m1/m2 fallthrough-kommentar | Kort kommentar vid `redirect()`/`notFound()`-cases | nextjs-ui-engineer (in-block) |
| m3 testnamn | Byt testnamn el. lägg matchande assertion | nextjs-ui-engineer (in-block) |

**Re-review krävs ej** för Minor (in-block §9.6). M1 verifieras via `git status` (3 filer som `D`) + grön `vitest`/`tsc` i nästa STOPP-rapport — ingen ny full review-cykel.

**Render-VETO (design-reviewer, ADR 0047 Area 5) kvarstår separat** — denna review täcker EJ rendered-UI/estetik/flödesbegriplighet.
