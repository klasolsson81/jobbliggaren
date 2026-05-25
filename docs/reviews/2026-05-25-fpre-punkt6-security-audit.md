# Security-audit: F-Pre Punkt 6 brand-paket

**Status:** BLOCKED — 1 Major (deploy/storlek), 0 Critical, 4 Minor (defense-in-depth)
**Granskat:** 2026-05-25
**HEAD:** `f3c6f1325b7d8a33cd8432dceaf99411e5c490d1`
**Auktoritet:** CLAUDE.md §5.4 (säkerhet), GDPR Art. 5/32 (storage/security), Replicate-API-policy
**Reviewer:** security-auditor

---

## Scope-summering

Granskat material i untracked-tillstånd (inga commits ännu):

- `scripts/generate-brand-assets.mjs` — Replicate Flux 1.1 Pro Ultra-klient
- `scripts/fas1-batch.mjs`, `fas1-rev1-batch.mjs`, `fas2-batch.mjs` — wrapper-batch-skript
- `web/jobbpilot-web/public/brand/raw/` — 34 PNG-filer + log + counter (40 MB totalt)
- `web/jobbpilot-web/src/app/{apple-icon,opengraph-image,twitter-image}.tsx` — Edge Route Handlers
- `web/jobbpilot-web/src/app/{icon.svg,manifest.ts,layout.tsx}` — file-conventions + root layout
- `web/jobbpilot-web/src/components/brand/{brand-logo.tsx,brand-logo.test.tsx}` — RSC-komponent
- `.env` (gitignored) — verifierad att den innehåller `REPLICATE_API_TOKEN`

---

## Critical (BLOCKERS — GDPR/säkerhet)

**Inga.** REPLICATE_API_TOKEN är intakt skyddad:

- `.env` matchas av `.gitignore:10` (`.env`-raden)
- `git ls-files --error-unmatch .env` returnerar `did not match` → aldrig committad
- `git log --all --full-history -- .env` returnerar tom → ingen historik
- Token läses endast via `readEnvToken()` (line 24-32) från lokal disk; aldrig
  loggas, aldrig serialiseras i error-meddelanden, aldrig skickas till stdout
- Token-läge i HTTP: `Authorization: Bearer ${token}` (line 61, 87) — korrekt
  pattern; ingen interpolation i URL eller log-rad
- Replicate-delivery-URL:erna i `generation-log.txt` är pre-signed CDN-länkar
  (form `https://replicate.delivery/xezq/<opaque>/<tmp>.png`); de innehåller
  **inte** API-token. De är time-limited (Replicate dokumenterar ~24h TTL).
  Risk efter expiry = noll. Inget blockerar commit utifrån token-perspektiv.

---

## Major (måste fixas innan commit)

### M1 — `public/brand/raw/` läggs in i production-bundle (40 MB binär-payload)

**Fil:** `web/jobbpilot-web/public/brand/raw/` (34 PNG-filer, 40 MB total)
**Källa:** `git check-ignore -v <fil>` returnerar tomt → **inte gitignored**.
`web/jobbpilot-web/.gitignore` saknar regel för `public/brand/raw/`. Ingen
`.vercelignore` finns. Next.js `public/`-konvention servar **allt** innehåll
direkt på origin-URL utan filtrering.

**Konsekvens om commit utförs som-is:**

1. **Git-repo-bloat:** 40 MB binär-content commitas till `main`. Repot är
   idag ~enstaka MB-storlek; en commit på 40 MB är synlig forever i historik
   även efter senare borttagning (kräver `git filter-repo` för riktig rensning).
2. **Production-deploy:** Vercel bundlar `public/` i deploy-artefakten. Varje
   Fas 1-rev1/Fas 2-PNG (`fas1rev1-promptA-ring-v1-...png` etc.) blir publikt
   nåbar på `https://dev.jobbpilot.se/brand/raw/<filnamn>.png`. Detta exponerar:
   - Iteration-historik (visar att 4-point-compass valdes efter 33 misslyckade
     försök — varumärkesintern process synlig externt)
   - Prompt-metadata via filnamn (`promptA-ring`, `promptC-badge` etc.)
   - 40 MB onödig payload i CDN-cache och deploy-storlek (Vercel Hobby har 1 GB
     deploy-cap; detta äter 4% av kvoten utan användarnytta)
3. **GDPR-bedömning:** ingen PII i raw-bilderna (rena logo-iterationer på vit
   bakgrund). Detta är **inte** GDPR-brott, vilket är varför detta klassas
   Major (deploy-hygien + git-historik-bloat), inte Critical.

**Krävs (välj en):**

**Alt A (rekommenderad — flytta utanför `public/`):**

```bash
mkdir -p docs/brand-iterations/
git mv web/jobbpilot-web/public/brand/raw docs/brand-iterations/raw
# eller: lokalt-only via tmp/
mkdir -p tmp/brand-iterations/
mv web/jobbpilot-web/public/brand/raw/* tmp/brand-iterations/
```

Plus uppdatera `scripts/generate-brand-assets.mjs:17` `OUT_DIR`:

```js
const OUT_DIR = path.join(REPO_ROOT, "tmp/brand-iterations");
```

**Alt B (acceptera intermediär lagring i git men förhindra publish):**

Lägg till i `web/jobbpilot-web/.gitignore`:

```
# F-Pre Punkt 6 raw brand-iterations — får inte deployas till production
public/brand/raw/
```

Men `git rm -r --cached web/jobbpilot-web/public/brand/raw` krävs först om
filerna någonsin staged:s. Just nu untracked → bara ignore-regeln räcker.

**Min rekommendation:** Alt A. Iteration-arkivet är värdefullt för Klas att
behålla men hör hemma i `docs/`-träd (eller helt utanför repo i `tmp/`), inte
i `public/` där `next build` servar det publikt.

**Delegera till:** nextjs-ui-engineer + Klas-beslut Alt A vs B vs hybrid.

---

## Minor (defense-in-depth — bör fixas, blockerar inte commit)

### m1 — `.env`-parsing i `readEnvToken()` är hand-rullad regex

**Fil:** `scripts/generate-brand-assets.mjs:24-32`
**Risk:** låg. Parser hanterar inte `export VAR=...`-syntax, multi-line-värden
eller escapad quotes. Inte aktiv risk med nuvarande `.env`-format, men en
framtida `.env`-edit (t.ex. `export REPLICATE_API_TOKEN="r8_..."`) skulle
tyst returnera `r8_...` istället för att stripa export-keyword + quotes
korrekt. Worst case: corrupt token leakar till `Authorization`-header och
Replicate svarar 401 — ingen säkerhetspåverkan, bara skript-fail.

**Föreslagen åtgärd:** ingen nu. Om skripten överlever Fas 1+2 och används
igen i framtida fas: byt till `dotenv`-paketet (npm-dep) eller dokumentera
`.env`-formatet i en kommentar i `readEnvToken()`.

### m2 — Error-meddelanden i `createPrediction` läcker hela Replicate-response-body

**Fil:** `scripts/generate-brand-assets.mjs:67, 78`
**Kod:** `throw new Error(`Create failed ${res.status}: ${text}`);`
**Risk:** låg. `text` är Replicate-API-error-body. Replicate dokumenterar att
deras error-payloads kan innehålla request-detaljer men inte API-token.
Verifierat: `Authorization`-header skickas en väg (request); Replicate echoar
inte tillbaka headers i error-body. Däremot kan body i sällsynta fall innehålla
prediction-ID:n som anses interna.

**Föreslagen åtgärd:** ingen nu. Om skripten flyttas till CI eller delas:
trunkera `text` till första 200 tecken eller logga bara `res.status` plus
första-rad-felmeddelande.

### m3 — Replicate-delivery-URL:er i `generation-log.txt` är fortfarande nåbara ~24h

**Fil:** `web/jobbpilot-web/public/brand/raw/generation-log.txt`
**Risk:** låg. URL:erna är opaque (pre-signed `xezq/<random>/tmp<random>.png`).
Innehållet är **redan** identiskt med PNG-filerna bredvid → ingen ny
information leakar. Efter ~24h returnerar URL:erna 403/404.

**Konsekvens kopplad till M1:** om `generation-log.txt` deployas till
production via `public/brand/raw/` (per M1) så ligger 34 Replicate-CDN-länkar
publikt i ~24h efter senaste generation. Inom tidsfönstret kan tredje part
hämta exakt samma PNG-content som redan ligger bredvid → effektivt nollvärde
för en angripare, men ändå onödig exponering av tredje-parts-CDN-mönster.

**Föreslagen åtgärd:** ingår i M1-fixen (flytta hela `raw/`-mappen utanför
`public/`). Om M1 fixas är detta automatiskt löst.

### m4 — Saknat metadata för `NEXT_PUBLIC_SITE_URL`-fallback

**Fil:** `web/jobbpilot-web/src/app/layout.tsx:21-23`
**Kod:** `metadataBase: new URL(process.env.NEXT_PUBLIC_SITE_URL ?? "https://dev.jobbpilot.se")`
**Risk:** låg. Fallback `dev.jobbpilot.se` är publikt, hörsamt CTA-mål — inte
ett internt API-endpoint. OG/twitter-image-länkar i Slack/LinkedIn-previews
kommer peka mot `dev.jobbpilot.se` om env-vari saknas i build-miljö
(t.ex. lokal dev). Inte säkerhetsrisk, bara UX-risk om dev-länkar läcker till
sociala medier.

**Föreslagen åtgärd:** ingen nu. När `prod.jobbpilot.se` (eller motsvarande)
existerar: säkerställ att Vercel production-deployment har
`NEXT_PUBLIC_SITE_URL=https://jobbpilot.se` satt explicit i env-config.

---

## Praise (säkerhets-medvetna val)

- **`.env` korrekt gitignored** och aldrig commit:ad — verifierat via
  `git ls-files --error-unmatch` + `git log --all --full-history`
- **Token läcker aldrig till log eller stdout** — `console.log`-rader i
  `generate-brand-assets.mjs` skriver bara `tag`, `prompt-prefix`, `bytes`
  och `elapsedMs`, aldrig `token`
- **Replicate-API-anrop använder `Bearer` korrekt** via `Authorization`-header,
  inte query-string (där det skulle logats av reverse-proxies)
- **Edge Route Handlers (`apple-icon.tsx`, `opengraph-image.tsx`,
  `twitter-image.tsx`) är helt statiska** — ingen user-input → noll
  injection-yta. `ImageResponse` (satori) sandboxar SVG-rendering → noll XSS.
- **Hard-coded svensk copy** ("JobbPilot", "Den svenska jobbansökningshanteraren")
  är säker — ingen interpolation från user-data
- **Hard-cap på 50 generations** (`HARD_CAP` line 19) skyddar mot oavsiktlig
  cost-runaway vid skript-bugg
- **Retry-logik med 429-throttling** (line 68-76) respekterar Replicate-rate-limits
  — defensiv extern-API-hygien
- **BrandLogo-test täcker variant/markSize/accessibility-attribut** — bra
  test-coverage på en UI-konstant
- **`icon.svg` använder CSS-vars med hex-fallback** — tema-flexibilitet utan
  att kompromissa standalone-rendering (favicon, OG)
- **`metadataBase` använder env-var med säker fallback** till publikt
  `dev.jobbpilot.se` — exponerar inte internal endpoints
- **Inga nya npm-dependencies** introducerade (allt via befintliga `next/og`,
  `react`) → supply-chain-yta oförändrad
- **Manifest använder `lang: "sv"` + svensk copy** korrekt; ingen PII

---

## GDPR-bedömning

**Inga PII-implikationer.** Brand-paketet innehåller:

- Wordmark "JobbPilot" (publikt varumärke)
- Tagline "Den svenska jobbansökningshanteraren" (publik marketing-copy)
- Färgvärden (`#0A2647`, `#FFCD00`)
- 4-point compass-star geometri

Inga user-tracking-pixels i OG/twitter-images. Ingen analytics. Ingen
external-resource-fetching i Edge Route Handlers utöver satori-rendering.
Replicate-API-anrop sker **endast lokalt på Klas dev-maskin** vid
generation; production-runtime har noll Replicate-beroende.

**GDPR-sub-processor-bedömning:** Replicate är en **dev-tool-leverantör**,
inte en runtime-sub-processor som behandlar JobbPilots användardata. Ingen
DPA krävs (jämför analog med GitHub Copilot eller andra dev-tools). Om
Replicate-integration någonsin flyttas till runtime (t.ex. dynamiska AI-bilder
för användare): då **kräver** vi DPA + EU-region-verifiering + ny ADR.

---

## Sammanfattning och rekommendation

**1 Major (M1) blockerar commit.** Detta är inte en GDPR-blocker eller
secret-leak — det är en deploy-hygien/git-historik-blocker. Att commita
40 MB raw-iterations till `main` och servera dem publikt på `dev.jobbpilot.se`
är onödig exponering av iteration-process och oacceptabel binär-bloat i
git-historiken.

**Innan commit:**

1. Klas beslutar M1 Alt A (flytta utanför `public/`) vs Alt B (gitignore i
   `public/`) — min rekommendation är **Alt A → flytta till `docs/brand-iterations/`
   eller `tmp/brand-iterations/`**
2. Uppdatera `scripts/generate-brand-assets.mjs:17` `OUT_DIR` så framtida
   genereringar landar på rätt plats
3. Verifiera med `git status` att inga PNG-filer ligger kvar under
   `web/jobbpilot-web/public/brand/raw/` innan commit

**Efter M1-fix:** Critical = 0, Major = 0, Minor = 4 (alla defer-bara) →
**APPROVED för commit.**

**Re-review krävs inte** efter M1-fix om bara filer flyttas och `OUT_DIR`
uppdateras — det är mekanisk omplacering utan ny attack-yta. Klas verifierar
manuellt via `git status` att raw/ är borta från `public/`.

---

## Memory-konflikter och anti-patterns kontrollerade

- **feedback_subagent_hook_bypass_watch:** ingen `core.hooksPath`-manipulation
  identifierad. Pre-push-hooks (gitleaks etc.) kommer köras normalt vid commit.
- **feedback_pathspec_commit_parallel_cc:** explicit paths vid commit
  rekommenderas — använd t.ex. `git commit -- scripts/ web/jobbpilot-web/src/app/
  web/jobbpilot-web/src/components/brand/ docs/brand-iterations/` snarare än
  `git commit -a`
- **CLAUDE.md §5.4 secrets-policy:** alla krav uppfyllda (ingen hardcoded
  secret, `.env` gitignored, ingen secret i log, ingen secret i URL)
- **CLAUDE.md §10 svensk copy:** OG-tagline "Den svenska jobbansökningshanteraren"
  följer civic-utility-ton, ingen emoji, inget utropstecken
