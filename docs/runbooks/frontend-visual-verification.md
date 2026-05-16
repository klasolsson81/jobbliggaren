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

Ren copy- eller token-färgändring utan strukturell påverkan triggar **inte**.
Vid tvekan: kör loopen — den är billig.

## Hur

1. Starta dev-servern i en separat terminal:
   `cd web/jobbpilot-web && pnpm dev`
2. Kör loopen: `cd web/jobbpilot-web && pnpm visual-verify`
3. Scriptet (`scripts/visual-verify.ts`) tar screenshots i **tre viewports
   (1280 / 1920 / 3440)** × **light + dark** av alla publika sidor.

### Viewports

| Bredd | Varför |
|-------|--------|
| 1280  | Vanlig laptop — baslinje |
| 1920  | Vanlig desktop |
| 3440  | Bred/ultrawide — **obligatorisk**; broad-screen-buggen var osynlig under denna bredd |

### Lagring och cleanup (self-cleaning by construction)

- Bilder sparas i `C:/tmp/jobbpilot-visual/<tidsstämpel>/` — **utanför repot**
  (aldrig under `web/` eller `docs/`; repo-renhet per CLAUDE.md §1.5).
- Cleanup är **inte** ett kom-ihåg-steg. `visual-verify.ts` raderar **alla**
  tidigare körningars mappar vid start av varje ny körning. Inget att städa
  manuellt, inget att glömma.

### Auth-gated sidor (tre-nivå-policy)

| Nivå | Sidor | Verifiering |
|------|-------|-------------|
| Publika | `/`, `/logga-in`, `/registrera`, `/vantelista` | Alltid i batchen (ingen backend krävs) |
| Auth-gated | `/jobb`, `/ansokningar`, `/cv`, `/mig`, `/admin/granskning`, `/sokningar`, `/sokningar/[id]` | Verifieras vid **live-deploy mot dev-backend** efter Klas tag-push, via `visual-verify.ts` **auth-läge** (opt-in). Om creds saknas i sessionen: noteras i STOPP-rapporten som "visuell verifiering pending live-deploy" om batchen rör en auth-gated yta. |

Mock-session används **inte** — det verifierar inte sann render (tomma
data-states, layout-skew från riktig data missas). Ingen Docker-up tvingas i
ren frontend-batch (YAGNI).

### Auth-läge (opt-in) — env-kontrakt

`visual-verify.ts` capturerar auth-gated sidor när alla tre sätts (annars
oförändrat publikt default):

| Env | Innebörd |
|-----|----------|
| `VISUAL_BASE_URL` | Live-frontend, **måste vara https** (`__Host-`-cookien avvisas av Chromium på http) — auth-gated verifieras därför mot live-deploy, inte lokal http. T.ex. `https://www.jobbpilot.se` |
| `VISUAL_BACKEND_URL` | Live-backend för direkt login + fixture-API, t.ex. `https://dev.jobbpilot.se` |
| `VISUAL_AUTH_EMAIL` / `VISUAL_AUTH_PW` | Dev JobSeeker-creds — **endast via env, aldrig i repo/kod** (CLAUDE.md §5.4) |

Login sker via direkt backend-call (robustare än formulärdrivning). Den
opaka session-cookien injiceras enbart i Playwright-context (in-memory) och
persisteras **aldrig** till disk — ingen `storageState`-fil (eliminerar
§5.4-risken vid källan i stället för att gitignore:a den). En temporär
fixture-sökning skapas via API för att capurera populerade lista-/detalj-/
dialog-tillstånd och raderas i teardown.

> **Beslut:** senior-cto-advisor 2026-05-16 (Variant A — utöka det befintliga
> verktyget; Variant B/C avvisade på DRY/CCP vs YAGNI/§5.4). Entydigt mot
> principer — ingen separat Klas-GO för approach; dev-creds är den enda
> Klas-beroende inputen.

### Dev-test-konto — plats, återanvändning, livscykel

Ett **dedikerat syntetiskt dev-test-JobSeeker-konto** används för auth-läget,
återanvändbart över faser (senior-cto-advisor-beslut 2026-05-16, **Variant C**
— creds utanför repo-trädet; Klas-GO 2026-05-16).

- **Cred-plats:** `%USERPROFILE%\.jobbpilot\dev-test-creds.env`
  (Windows: `C:\Users\zebac\.jobbpilot\dev-test-creds.env`). **Utanför
  repo-trädet** — noll repo-yta för creds, kan per konstruktion inte fångas
  av felaktig `git add` / bruten `.gitignore` (§5.4, defense-in-depth). Denna
  runbook och MEMORY.md innehåller **endast pekare till path:en, aldrig
  creds:en själva.**
- **Återanvändningsprotokoll (framtida CC):** source:a filen till env före
  `pnpm visual-verify`. PowerShell:
  ```powershell
  Get-Content $env:USERPROFILE\.jobbpilot\dev-test-creds.env |
    ForEach-Object { if ($_ -match '^export (\w+)=(.*)$') {
      [Environment]::SetEnvironmentVariable($matches[1], $matches[2]) } }
  ```
  (Git-bash: `source ~/.jobbpilot/dev-test-creds.env`.) Sätt även
  `VISUAL_BASE_URL` (https live-frontend) per körning.
- **Konto-livscykel:** syntetiskt verifieringskonto, ägt av dev-processen,
  ej kopplat till fysisk person, **ingen PII** utöver syntetisk e-post.
  Skapat via direkt `/api/v1/auth/register` (se observation nedan). Lösenord
  roteras vid misstänkt exponering (ingen schemalagd rotation för
  dev-fixture). Kontot + dev-DB-raden raderas när dev-miljön rivs eller
  verktyget avvecklas. Ingen produktionsdata berörs.
- **Observation (ADR 0005-efterlevnad):** kontot skapades via direkt
  `/api/v1/auth/register`, som **inte** är `RegistrationsOpen`-flag-gejtat
  (kill-switchen täcker endast waitlist/invitation-flöden). Inkonsistens mot
  ADR 0005-kostnadsskyddsmönstret — triageras separat i en auth-fokuserad
  touch (senior-cto-advisor 2026-05-16: ej formell TD nu, ej denna touch's
  fas-scope).
- **Future-watch:** behövs creds delas mellan maskiner / köras i CI /
  auto-roteras → migrera till AWS Secrets Manager (Variant D). Lyfts ej som
  TD nu (YAGNI — kravet finns inte).

## Vem gör vad

| Roll | Ansvar |
|------|--------|
| CC | Kör loopen efter implementation, innan STOPP-rapport. Det är ett verifieringssteg, inte ett granskningsbeslut. |
| design-reviewer | Invokeras **mot bilderna** (inte mot diff). Detta är spärren som rotfelet kräver. |
| Klas | Slutgodkänner bilderna i STOPP-rapporten. |

## STOPP-rapport — obligatorisk rad

Varje STOPP-rapport för en triggande batch innehåller:

```
Visuell verifiering: C:/tmp/jobbpilot-visual/<tidsstämpel>/
  — N screenshots (publika sidor × 1280/1920/3440 × light/dark)
  — design-reviewer-verdikt mot bilderna: <kort>
  — auth-gated: <pending live-deploy | ej berört i denna batch>
  — raderas automatiskt vid nästa körning
```

## Referens

- senior-cto-advisor-beslut 2026-05-16 (denna runbook + `scripts/visual-verify.ts`)
- CLAUDE.md §1.5 (repo-renhet), §1.6 (runbooks), §9.4 (strukturella spärrar)
- Kent Beck, *XP* — "make the right thing the easy thing" (self-cleaning cleanup)
