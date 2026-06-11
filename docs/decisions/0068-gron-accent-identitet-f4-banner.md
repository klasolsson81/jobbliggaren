# ADR 0068 — Grön accent-identitet + F4 hero-banner

**Datum:** 2026-06-10
**Status:** Accepted 2026-06-10 (Klas-beslut via Claude Design-utforskning "Banner-utforskning v2", handoff `docs/handoff-banner/` + chat-direktiv samma dag: temat gäller alla sidor + landingpage; CC-mandat att uppdatera spec-filer)
**Beslutsfattare:** Klas Olsson (produktägare)
**Supersedes (delvis):** [ADR 0052](./0052-design-system-v3-modern-civic.md) **Beslut 6** (navy-800-primärknapp + navy-länkkontrakt → grön-accent-kontrakt; ADR 0052 Beslut 1–5 — neutraler, token-brygga, hybrid-strategi, radius-skala, typografi — **består oförändrade**). Superseder även E1a-implementeringen "Papperskontoret" content-first-hero på /jobb (ADR 0067 impl-notat Fas E1a, mergad #40 samma dag — varm hero-canvas ersätts av F4-banner-plattan).
**Amends:** ADR 0016/design-principles gradient-förbud + "myndighetsblå"-identiteten (scoped undantag: mörkgrön gradient ENBART hero-plattan; accent-hue blå→grön).
**Relaterad:** ADR 0037 (dark mode — accent-dark-värden), ADR 0038 (a11y-golv — oförändrat), ADR 0067 (Platsbanken sök-paritet — E2b–E2e pausade under G1; F4-bannern är /jobb-sökytans nya ram).

> **Livscykel-/proveniens-not:** Skriven av Claude Code på Klas-direktiv (bypass-mandat 2026-06-10: "alla design val etc. måste uppdateras", CC får ändra CLAUDE.md/DESIGN.md m.fl.) — samma medvetna §9.4-override-mönster som ADR 0067 (memory `feedback_klas_can_override_adr_verbatim_source`). Besluts-substansen är Klas egen (Claude Design-utforskning, beslutslogg i `docs/handoff-banner/README.md`); mekanik-domarna är grundade i senior-cto-advisor + dotnet-architect 2026-06-10 (`docs/reviews/2026-06-10-g1-*.md`).

---

## Kontext

Riktning A "Papperskontoret" (varm papperston, content-first utan banner) valdes 2026-06-10 förmiddag och levererades på /jobb (E1a, #40). Klas utvärderade renderad UI och saknade bannern — sidan blev för tom. I Claude Design utforskades en ny banner med **egen JobbPilot-identitet** (inte Platsbankens centrerade stapel): variant **"F4 Hybrid"** — inramad platta + asymmetrisk komposition — valdes, tillsammans med ett **app-wide accentbyte blå→mörkgrön**.

Grön huvudaccent var tidigare riktning B "Skogsgläntan" som avvisades samma dag p.g.a. oadresserad dark-kontrast-skuld. Denna ADR är det **nya Klas-GO:t** med dark-frågan löst: accent-dark `#6EE7A8` (≈11.9:1 mot dark canvas `#0B1525`), banner-gradienten redan mörk (oförändrad i dark), fill-färger ej dark-skiftade (knapp-kontraktet består).

Handoffens referens-HTML byggdes mot den stale v2-slate-skillen (känd docs-drift). Handoff-texten är dock explicit: *"Neutraler (oförändrade från civic-utility)"* — neutral-värdena i referensen är scaffolding, inte beslut (CTO Beslut 1).

## Beslut

### Beslut 1 — Accentbyte blå/navy → mörkgrön, app-wide, EN interaktionsfärg

All interaktionsfärg (länkar, aktiv nav, primärknappar, selektion/aktiv rad, fokus-ringar, match-indikatorer, brand-pills/badges, checkboxar/radios/toggles/progress, hover-states) byter från navy-rampen till en ny **`--jp-accent-*`-ramp**:

| Token | Light | Dark | Roll |
|---|---|---|---|
| `--jp-accent-900` | `#0B2A1E` | ej skiftad | hover-text-extrem (pagehero-knapp) |
| `--jp-accent-800` | `#15603F` | **ej skiftad** | FILL: primärknapp, checked (kontrakt: aldrig ljus knapp/mörk text) |
| `--jp-accent-800-hover` | `#1E6B4C` | ej skiftad | fill-hover |
| `--jp-accent-700` | `#15603F` | `#6EE7A8` | TEXT/BORDER: länkar, aktiv nav, titlar, fokus |
| `--jp-accent-600` | `#1E6B4C` | `#A7F3D0` | länk-hover |
| `--jp-accent-500/300/100` | `#2E8B63`/`#74C29A`/`#D3E7DC` | `#3E8E68`/`#2E5C46`/`#0E2A1E` | chart/dekor/avatar |
| `--jp-accent-50` | `#E9F2ED` | `#0E2A1E` | selektion, aktiv rad |
| `--jp-gold` | `#E8C77B` | — | signatur, sparsamt; INGEN konsument ännu (logo-översyn separat) |

Migrationen sker i **alias-lagret** (`--jp-brand-* → --jp-accent-*`; Tailwind `text-brand-*` + shadcn `--primary`/`--ring` följer automatiskt — OCP, ADR 0052 Beslut 2 betalar sig) + mekanisk navy→accent-rename av `.jp-*`-konsumenter. WCAG-verifierat: `#15603F` på vitt 7.56:1, vit text på `#15603F` 7.56:1, `#6EE7A8` på `#0B1525` 11.9:1.

**Accent-migrationen delas inte upp** — halvbytt accent ser trasig ut (handoff). **Status-färgerna (success/warning/danger/info) är oförändrade** — status är inte accent. **Neutralerna (v3 ink/border/surface/canvas) är oförändrade** (CTO Beslut 1 — referensens slate-värden var stale scaffolding; ADR 0052 Beslut 1 består).

**Undantag:** logotypens kompass-mark förblir blå med guldprick `#FFCD00` — varumärket byter INTE färg. Navy-rampen behålls definierad som logo-substrat (`--jp-navy-700` via `.jp-brand`/currentColor); övriga navy-steg städas i F-städ efter grep-verifierad nollkonsumtion. `--jp-gold` (#E8C77B) införs som token men konsolidering mot `#FFCD00` hör till den separata logo-översynen.

### Beslut 2 — F4 Hybrid-banner på /jobb; gradient-platta som scoped undantag

/jobb-heron blir en **inramad platta** (inte full-bleed): marginal runt om (24px topp / 32px sidor mot canvas), `--jp-r-md` (6px) rundning, **mörkgrön diagonal gradient** `linear-gradient(118deg, #0B2A1E 0%, #14503A 60%, #1E6B4C 100%)` (granskog — kall ton, ej FK-grön). **Asymmetrisk komposition:** display-rubrik vänster ("Lediga jobb. / I lugn och ro." — 44px/800/vit), sök + åtgärder höger (Senaste sökningar/Sparade annonser-knappar, sökrad, Ort/Yrke-chips). Lede: "Sök bland aktiva annonser från Platsbanken. Filtrera och jämför utan att tappa en enda annons."

**Kontrollerna i bannern är neutrala** — vita solida knappar (banner-lokal border `#CBD5E1`), ink-mörk Sök-knapp (`#0C1A2E` v3-ink, tema-stabil) — **inte gröna**. Bannern bär färgen, kontrollerna gör det inte. Inga skuggor, ingen logga/vattenmärke, inga stats i bannern (stats är globala och stannar i headern). **Ingen placeholder-text i sökfältet** (Klas hård regel, upprepad 2026-06-10 — överrider referens-HTML:ens exempel-placeholder; memory `feedback_no_placeholder_example_text`).

**Dokumenterade undantag från civic-utility-reglerna (scoped, ENBART hero-plattan):**
1. **Gradient** — design-principles regel 1 "inga gradients någonstans" + CLAUDE.md §5.2 får ett scoped hero-platta-undantag. Gäller ALDRIG knappar, badges, kort, bakgrunder utanför plattan.
2. **Display-rubrik 44px/800** — över H1-skalan (28px); banner-rubriken är display-klass (jfr landing-display), inte sid-H1-mall.

Dark mode: gradienten oförändrad (redan mörk; verifierad mot v3-dark-canvas `#0B1525`), 1px `--jp-border-soft`-hairline runt plattan, allt platt-innehåll tema-stabilt.

### Beslut 3 — Samma gröna identitet på ALLA ytor nu; F4-komponent-rollout senare

(Klas chat-direktiv: temat gäller alla sidor + landingpage.) `.jp-pagehero` (inre sidor) + `.jp-empty--brand` + landing-hero-ytor byter **färg** navy → samma gröna gradient NU (token-swap via `--jp-hero-gradient` + solid ankare `--jp-hero-bg: #14503A`). Full ombyggnad av övriga sidors banners till **F4-komponenten** (platta + asymmetri + sök-panel) sker senare med samma återanvändbara komponent — "senare" avser kompositionen, "samma färg tills vidare" avser färgen (CTO Beslut 2-upplösning av handoffens skenkonflikt).

### Beslut 4 — Fokus-regelverk

`--jp-focus: var(--jp-accent-700)` (grön `#15603F` light / ljusgrön `#6EE7A8` dark — skiftar själv). **Inne i gradient-ytor: VIT ring** via property-scoping (`.jp-hero__plate, .jp-pagehero, .jp-empty--brand { --jp-focus: #FFFFFF; }` — vit mot ljusaste gradient-stopp 6.4:1 ≥ 3:1). Orange/bärnsten används INTE för fokus (krockar med warning). shadcn `--ring`/`--sidebar-ring` → `var(--jp-focus)` (WCAG-fix — var osynlig mörk ring i dark).

### Beslut 5 — Fas-sekvens: G1 nu, E2b–E2e efter

Identitetsbytet tas som egen fas **"G1"** (egen PR) FÖRE ADR 0067:s återstående sök-splits (E2b kommun-kaskad, E2c facet-count, E2d chips, E2e sortering) — design-fundament före mer funktionell UI; varje E-komponent byggd i blått vore planerad rework (Beck: make the change easy first). "F4 Hybrid" är design-variantens namn; fas-namnet är G1 (F-numreringen är upptagen av HANDOVER-v3-refaktorfaserna; E-serien är paritetsarbete).

## Konsekvenser

**Positiva:** egen identitet (inte Platsbanken-klon); bannern åter (Klas-utvärdering: utan = för tomt); en konsekvent interaktionsfärg; dark-skulden från Skogsgläntan-avvisningen löst by design; alias-flippen gör migrationen mekanisk och Tailwind/shadcn-transparent.

**Negativa/risker:** (1) rendered output avviker medvetet från handoff-PNG:erna i neutral-detaljer (v3-neutraler ≠ referensens stale slate) — korrekt utfall, inte avvikelse; (2) accent-grön ligger nära success-grön → `.jp-matchchip--mid` flyttas till neutral variant så mid/high-distinktionen inte kollapsar (design-reviewer bedömer; Klas kan välja annan mekanik); (3) navy-ramp-rester (logo-substrat) kräver F-städ-disciplin; (4) två scoped regel-undantag (gradient/display) måste bevakas av design-reviewer så de inte sprider sig — undantagen är dokumenterade här + i skills exakt för det.

**Spec-ändringar i samma PR (Klas-mandat):** CLAUDE.md §5.2 (gradient-undantag), `jobbpilot-design-principles` (accentfärg + regel 1-undantag), `jobbpilot-design-tokens` (full grön kanon — **supersedar den pending docs-drift-syncen `#0B5CAD`→navy**: skillen synkas direkt till grön i ett svep, mellansteg via navy vore waste), DESIGN.md §1.2.

## Alternativ som övervägdes

- **Behåll Papperskontoret utan banner** — avvisat av Klas efter rendered-utvärdering (för tomt).
- **Neutral-revert till referensens v2-slate** (CTO Beslut 1 alt B) — avvisat: reverterar Accepted användartest-grundat ADR 0052-beslut utan mandat; pixel-trohet mot stale scaffolding är cargo cult.
- **Accent-migration i etapper** — avvisat: halvbytt accent ser trasig ut (handoff explicit).
- **ADR 0052-amendment istället för ny ADR** — avvisat: hue-byte av hela interaktionssystemet + två principundantag rör tre tidigare ADRs → egen besluts-nod (Nygard 2011, supersedes-länkning).

### Implementerings-notat 2026-06-10 (Fas G2) — banner-konsekvens efter Klas rendered-feedback

**Källa:** Klas rendered-feedback på G1 (tre fynd) + design-reviewer G2-rond. Additivt notat; Beslut 2–3-substansen består men preciseras:

- **H1 /jobb:** "Lediga jobb./I lugn och ro." → **"Sök jobb"** (Klas: AI-aktigt; enkel funktionell rubrik à la GOV.UK "Find a job". Inget utropstecken — §10.3 står över Klas-exemplets "!"). Guest-klonen följer.
- **Innehållsbredd-kanon = 1136px:** `.jp-page`-shorthand skrev över `.jp-container`:s horisontella padding (kaskad-kollision) → innehåll blev 1200 och alignade inte med platta/header. Fix `padding-block`. EN innehållsbredd app-wide (header = platta = kort = 1136), empiriskt Playwright-verifierad.
- **Beslut 3:s F4-rollout TIDIGARELAGD (Klas-direktiv):** `.jp-pagehero` (alla inre sidor) + `.jp-land-hero` (landing) är nu inramade plattor (wrapper = canvas + 24/32-inset; inner = 1136-platta, gradient, `--jp-r-md`, dark-hairline) — kant-till-kant-banden utgår överallt. **Display-undantaget (Beslut 2 p.2) följer platta-KOMPONENTEN var den används:** pagehero-titel 44/800 (32px mobil) = jobb-bannerns skala; konsekvent rubrik-typografi per Klas-krav.
- **Dubbel-grön förbjuden (design-reviewer M2):** `.jp-empty--brand` neutraliserad på pagehero-sidor (/ansokningar, /cv → neutral `.jp-empty`) — dess rationale "när hela sidan annars vore vit" gäller inte när pagehero bär gradienten; två staplade färgband degraderar plattan till dekoration. Modifiern behålls definierad (0 konsumenter). **En-primary-regeln:** pagehero-asidens CTA växlar till ghost när empty-statens CTA är skärmens primära handling (total === 0).
- Fokus-scoping flyttad till gradient-ELEMENTEN (`__inner`-plattorna), inte wrappers (latent vit-ring-på-canvas-fälla eliminerad).

**G3-tillägg (Klas rendered-fynd 2026-06-10, design-reviewer Approved 0 fynd):** (1) `.jp-hero__title` "Lediga jobb./I lugn och ro." → **"Sök jobb"**; `.jp-hero__plate align-items: end → start` (rubrik top-left, konsekvent med pagehero-titlarna). (2) Pagehero-CTA ("Ny ansökan"/"Nytt CV") alltid **vit** (`.jp-pagehero .jp-btn--primary`) — G2:s villkorade ghost läste som grön-genomskinlig på gradienten; banner-kontroller är vita (handoff-doktrinen), grön primär bor i det vita empty-kortet (en-primary bibehållen). (3) **Rotfix app-wide:** global `a`/`a:hover`-färg scopad till `a:not(.jp-btn)` — knapp-`<a>` (`.jp-btn`) ärvde annars länkfärgen i hover (specificitet `a:hover` 0,1,1 > `.jp-btn--primary` 0,1,0; knappens hover satte bara bakgrund) → grön text på grön knapp. Lagar även latent `.jp-btn--secondary`-`<a>`-hover-bug.

### Implementerings-notat 2026-06-11 (E2f) — accent-700:s text-roll preciseras: grönt = interaktion, inte information

**Källa:** Klas rendered-feedback 2026-06-11 (sök-paritets-fasen E2f — samma process som G2/G3-notaten ovan) + design-reviewer (`docs/reviews/2026-06-11-sok-paritet-e2f-design-review.md`). Additivt notat; Beslut 1-tabellens mekanik består men **text-rollen preciseras**:

- **"titlar" och "aktiv nav" utgår ur accent-700:s TEXT-roll för informations-ytor:** jobb-/app-titlar, träffräknar-tal, landing-peek-titlar, översiktens datum-siffra → `--jp-ink-1`; aktiv nav-länk + drawer-item → ink-text där **accent-baren/border-left bär plats-indikationen ensam** (GOV.UK-mönstret; 3 oberoende cues — bar/border + weight + aria-current).
- **Accent-700 som text består för INTERAKTION:** länkar (`a:not(.jp-btn)`), fokus, selektion/aktiv rad (`aria-selected`), kontroll-states (`.jp-save[data-saved]`, checked), semantiska brand-badges/tags/pills, notice-kategorisystemet.
- **Spec-sync-status:** globals.css-tokenkommentaren uppdaterad i E2f-PR:n; **DESIGN.md §-raden + jobbpilot-design-tokens-skillen är spec-edits som väntar Klas `approve-spec-edit.sh`** (lyft i E2f-rapporten — token-tabellen får inte fortsätta säga "titlar = accent-700"). Tre kvarvarande de-grönings-kandidater (`.jp-land-top__link.is-active`, `.jp-land-feature__key`, `.jp-summary__row--highlight`-värdet) = Klas-dom, listade i design-review-rapporten.

## Implementation

G1-PR: token-block + alias-flip + mekanisk rename + F4-banner (/jobb) + pagehero/empty-brand/landing-gradient + fokus-scoping + spec-filer + skills-sync. Referens-facit: `docs/handoff-banner/referens/F4-banner-referens.html` (komposition; neutraler/placeholder per CTO Beslut 1 + Klas-regel). Reviews: design-reviewer (med dessa dokumenterade undantag som granskningsbas), code-reviewer, security-auditor.

## Referenser

- Handoff: `docs/handoff-banner/CC-PROMPT.md` + `README.md` + `referens/` (beslutslogg 2026-06-10)
- Agent-domar: `docs/reviews/2026-06-10-g1-cto.md` (Beslut 1–4 + fas-namn), `docs/reviews/2026-06-10-g1-architect.md` (token-spec, WCAG-beräkningar, fynd)
- ADR 0052 (v3 modern civic — Beslut 6 superseded, 1–5 består), ADR 0037 (dark), ADR 0038 (a11y-golv), ADR 0067 (sök-paritet — pausad E2b–E2e)
- Nygard 2011 (ADR-immutabilitet/supersedes), Martin 2017 kap. 8/13 (OCP/CCP), Fowler 2018 (branch-by-abstraction — alias-flip), Beck (make the change easy)

---

*ADR-index underhålls av docs-keeper. ADR 0068 fastställer JobbPilots gröna accent-identitet (app-wide, EN interaktionsfärg, dark-säker) + F4 Hybrid-bannern (inramad gradient-platta, asymmetrisk, neutrala kontroller) med två scoped regel-undantag (gradient + display-rubrik, enbart hero-plattan), supersedar ADR 0052 Beslut 6 och E1a-Papperskontoret-heron, och pausar ADR 0067 E2b–E2e under G1.*
