# Runbook — Frontend visual verification (screenshot-loop)

> **Status:** Obligatorisk rutin. Beslut: senior-cto-advisor 2026-05-16
> (efter att v2-refaktorn godkändes på diff-nivå men underkändes visuellt av
> Klas i bred viewport). Rotorsak: agent-review av kod ≠ verifiering av
> renderad UI.

## Varför

design-reviewer granskar kod och diff. Den ser `border-b` och godkänner
mönstret — men ser inte att rader smälter ihop på en 3440px-skärm eller att
innehåll klistras mot vänsterkanten. Visuell verifiering i **verkliga
viewports** är den enda spärr som fångar detta.

## När (trigger — obligatorisk)

Visuell verifiering krävs när en frontend-batch:

- skapar en **ny route/sida**, eller
- gör en **markant ändring** av en renderad yta. *Markant* = något av:
  - ändrad sidlayout, grid eller shell
  - ny eller ombyggd komponent som renderas på en sida
  - ändring i `globals.css` som rör `.jp-page` / `.jp-app` / `.jp-main` /
    layout-tokens / `.jp-*`-komponentprimitiv
  - ändrad responsiv struktur

- bär ett **icke-vilotillstånd i deltat** — en fel-, vägrans-, kvittens-/
  utfalls-, tom- eller laddningsyta som införs, ändras, **eller får ändrad
  nåbarhet** (flagga/gate/env-villkor). Tillståndet **renderas** före
  designverdikt — aldrig bara asserterat som sträng: en strängassertion kan
  inte se en tom sida. Framkallning: *Hur* steg 0.
  **Kostnadsgräns:** ett rent tillstånds-delta renderas vid **1280 i varje
  nåbart färgläge** (i dag ett: light — `DARK_MODE_ENABLED = false` i
  `theme-provider.tsx`; två när flaggan sätts true), **plus 3440 när
  tillståndet ersätter sidkroppen eller sektionen** (replaces-page/-section/
  -form); hela viewport-matrisen krävs bara när ändringen också är
  strukturell enligt punkterna ovan.

Ren copy- eller token-färgändring utan strukturell påverkan triggar **inte**.
Vid tvekan: kör loopen — den är billig.

## Hur

0. **Tillståndsrendering** (triggerns icke-vilotillståndspunkt): framkalla
   tillståndet på riktigt — stubbat svar, död backend-port, eller
   konfigflagga — och läs utfallet i renderad DOM (skärmbild + computed DOM),
   aldrig i rå HTML (flight-payloaden ger falska träffar). Auth-grindade
   tillstånd renderas lokalt utan riktiga creds: en lokal stub som besvarar
   login/me/refresh plus sidans datafetch (zod-schemana i `src/lib/dto/` är
   fixturspecen) ger riktig inloggning i riktiga kaskaden — mätt 2026-08-24.
   `pnpm visual-verify` täcker INTE detta läge; tillståndskörningen är en
   egen Playwright-läsning per kostnadsgränsen i triggern.
1. Starta dev-servern i en separat terminal:
   `cd web/jobbliggaren-web && pnpm dev`
2. Kör loopen: `cd web/jobbliggaren-web && pnpm visual-verify`
3. Scriptet (`scripts/visual-verify.ts`) tar screenshots i **tre viewports
   (1280 / 1920 / 3440)** av alla publika sidor.

### Viewports

| Bredd | Varför |
|-------|--------|
| 1280  | Vanlig laptop — baslinje |
| 1920  | Vanlig desktop |
| 3440  | Bred/ultrawide — **obligatorisk**; broad-screen-buggen var osynlig under denna bredd |

### Lagring och cleanup (self-cleaning by construction)

- Bilder sparas i `C:/tmp/jobbliggaren-visual/<tidsstämpel>/` — **utanför repot**
  (aldrig under `web/` eller `docs/`; repo-renhet per CLAUDE.md §1.5).
- Cleanup är **inte** ett kom-ihåg-steg. `visual-verify.ts` raderar **alla**
  tidigare körningars mappar vid start av varje ny körning. Inget att städa
  manuellt, inget att glömma.

### Auth-gated sidor (tre-nivå-policy)

| Nivå | Sidor | Verifiering |
|------|-------|-------------|
| Publika | `/`, `/logga-in`, `/logga-in/lank`, `/vantelista` | Alltid i batchen (ingen backend krävs) |
| Auth-gated | `/jobb`, `/ansokningar`, `/cv`, `/mina-sidor`, `/admin/granskning`, `/sokningar`, `/sokningar/[id]` | Verifieras mot en **lokal Development-stack** via `visual-verify.ts` **auth-läge** (opt-in), eller live av Klas efter deploy. Går inget av dem i sessionen: noteras i STOPP-rapporten som "visuell verifiering pending" om batchen rör en auth-gated yta. |

Mock-session används **inte** — det verifierar inte sann render (tomma
data-states, layout-skew från riktig data missas). Ingen Docker-up tvingas i
ren frontend-batch (YAGNI).

### Auth-läge (opt-in) — env-kontrakt

`visual-verify.ts` capturerar auth-gated sidor när alla tre sätts (annars
oförändrat publikt default):

| Env | Innebörd |
|-----|----------|
| `VISUAL_BASE_URL` | Frontenden, **måste vara https** (`__Host-`-cookien avvisas av Chromium på http), t.ex. `pnpm dev --experimental-https` → `https://localhost:3000` |
| `VISUAL_BACKEND_URL` | En backend som kör i **Development**, t.ex. `http://localhost:5049` |
| `VISUAL_AUTH_EMAIL` | En adress på en RFC-reserverad domän, t.ex. `visual@e2e.jobbliggaren.test` |

Login sker med kod (ADR 0142): kontot öppnas via den dev-only seed-sömmen
`POST /api/v1/dev/accounts` (idempotent), en challenge begärs och koden hämtas
från `POST /api/v1/dev/login-code`. Båda sömmarna finns bara i Development och
bara för reserverade adresser, så det finns inga creds att förvara. Mot lådan
(Production) finns ingen sådan söm; auth-gated ytor där verifieras av Klas live.
Den opaka session-cookien injiceras enbart i Playwright-context (in-memory) och
persisteras **aldrig** till disk — ingen `storageState`-fil. En temporär
fixture-sökning skapas via API för att capurera populerade lista-/detalj-/
dialog-tillstånd och raderas i teardown.

> **Beslut:** senior-cto-advisor 2026-05-16 (Variant A — utöka det befintliga
> verktyget; Variant B/C avvisade på DRY/CCP vs YAGNI/§5.4). Kod-loginet ersatte
> lösenordsloginet i ADR 0142 del 5a (#1743).

## Vem gör vad

| Roll | Ansvar |
|------|--------|
| CC | Kör loopen efter implementation, innan STOPP-rapport. Det är ett verifieringssteg, inte ett granskningsbeslut. |
| design-reviewer | Invokeras **mot bilderna** (inte mot diff). Detta är spärren som rotfelet kräver. |
| Klas | Slutgodkänner bilderna i STOPP-rapporten. |

## STOPP-rapport — obligatorisk rad

Varje STOPP-rapport för en triggande batch innehåller:

```
Visuell verifiering: <utdatakatalog>
  — N screenshots (<ytor × viewports × färglägen — per körningens klass:
    full matris vid strukturell trigger, kostnadsgränsens urval vid
    tillståndstrigger>)
  — design-reviewer-verdikt mot bilderna: <kort>
  — auth-gated: <renderat lokalt via stub (Hur steg 0) | pending live-deploy
    (endast strukturell auth-gated yta) | ej berört>
  — raderas automatiskt vid nästa körning
```

## Referens

- senior-cto-advisor-beslut 2026-05-16 (denna runbook + `scripts/visual-verify.ts`)
- CLAUDE.md §1.5 (repo-renhet), §1.6 (runbooks), §9.4 (strukturella spärrar)
- Kent Beck, *XP* — "make the right thing the easy thing" (self-cleaning cleanup)
