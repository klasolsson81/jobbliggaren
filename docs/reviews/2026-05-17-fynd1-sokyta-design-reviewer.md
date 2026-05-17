# Design-review: Fynd 1 — sök-yta sort-separation + sort-etiketter

**Status:** ✓ Approved (inga blockers, inga major)
**Granskat:** 2026-05-17
**Granskare:** design-reviewer (Opus 4.7)
**Auktoritet:** DESIGN.md §1, §5, §6, §8, §9 + jobbpilot-design-principles
(regel 3/6/7) + jobbpilot-design-copy (core tone, button/label-text)
**Scope:** 5 filer (git diff HEAD). Kod/diff-granskning — visual-verify körs
live efter Klas-GO + Vercel-deploy (Batch 6-mönstret).

---

## Granskat scope

- `job-ad-filters.tsx` — Sortering flyttad ut ur Filter-disclosuren till egen
  alltid-synlig sektion (`border-t border-border-default pt-4`).
- `lib/job-ads/status.ts` — `JOB_AD_SORT_LABELS` ExpiresAt-etiketter omskrivna.
- `(app)/jobb/page.tsx` — `activeFilterCount` tappar sort-termen.
- `job-ad-filters.test.tsx` + `status.test.ts` — uppdaterade + IA-regressionstest.

---

## 1. Civic-utility-ton i nya sort-etiketterna

**Bedömning: godkänd — en mätbar förbättring mot civic-utility-tonen.**

| Enum | Förr | Nu |
|---|---|---|
| `PublishedAtDesc` | Nyast först | Nyast först (oförändrad) |
| `PublishedAtAsc` | Äldst först | Äldst först (oförändrad) |
| `ExpiresAtDesc` | Sist sista ansökningsdag | **Stänger senare** |
| `ExpiresAtAsc` | Tidigast sista ansökningsdag | **Stänger snart** |
| `Relevance` | Mest relevant | Mest relevant (oförändrad) |

- **Begriplighet:** "Stänger snart/senare" beskriver vad användaren faktiskt
  bryr sig om (hinna söka innan annonsen stänger) — användar-avsikt, inte
  datafält-riktning. Linjerar med jobbpilot-design-copy "Konkret: visa
  avsikten". Den gamla "Sist sista ansökningsdag" hade dubbel-"sista" som var
  parse-tung för §1.1-målanvändaren (55-åringen i Alingsås).
- **Ingen AI-klyscha:** ingen peppning, inga utropstecken, inga emojis, ingen
  marknadsföringston. Saklig — passerar jobbpilot-design-principles regel 6
  ("Tydlig, inte cute") och copy-skillens forbidden-patterns-tabell.
- **Parallellitet:** "Stänger snart/senare" är ordnat som antonympar med
  konsekvent verb-först-struktur, vilket speglar "Nyast/Äldst först"-parets
  parallellitet. Listan läses nu som fyra jämbördiga ordnings-val + Relevance.
  Klar förbättring mot den gamla asymmetrin ("Sist…/Tidigast…").
- **Enum oförändrad:** `JobAdSortBy`-nycklarna är orörda (CTO Fråga 2 Approach
  A = copy-only). Verifierat i diff — endast `Record`-värdena ändrade,
  backend-kontrakt intakt. Korrekt avgränsning.

Minor språk-notis (ej blocker, ej åtgärdskrav): "Stänger snart" är subjektivt
relativt till "Stänger senare" snarare än absolut. I sort-kontext är detta
korrekt — en sortering är per definition en relativ ordning, inte ett filter
med tröskel. Etiketterna är därför semantiskt rätt för en *sort*-kontroll. Hade
detta varit ett *filter* ("visa annonser som stänger snart") vore det tvetydigt
— men separationen i denna diff gör just den distinktionen explicit. Konsekvent.

---

## 2. Sort-separationen mot jobbpilot-design-principles regel 3/7

**Bedömning: godkänd — separationen stärker resultat-först, bryter inte tätheten.**

- **Regel 3 (resultat-först / inga fyllnadselement):** Sökfältet (q +
  typeahead) är fortsatt den alltid-synliga primära ytan högst upp. Sortering
  läggs som en andra alltid-synlig kontroll under sökordet, *före* Filter-
  disclosuren. Resultatet renderas fortsatt av Server Component utan att
  trängas undan av kontroll-chrome. Ingen ny dekoration, ingen ikon-pynt,
  ingen låda — separationen är en enkel `border-t`-hairline (regel 1, papper
  inte glas). Ingen regel 3-konflikt.
- **Regel 7 (densitet med respekt):** Disclosuren finns kvar för det som
  faktiskt är power-tool-täthet (taxonomi-multi-select med concept-id-hint).
  Att lyfta ut *en* `<select>` med fem alternativ ökar inte den synliga
  tätheten nämnvärt — en sorterings-dropdown är låg kognitiv last och en
  förväntad kontroll på en sökresultat-yta (Platsbanken-, Indeed-,
  Arbetsförmedlingen-precedens). Detta är IA-förfining inom regel 7:s andemening,
  inte ett brott mot den.
- **Konceptuell korrekthet:** JSDoc-motiveringen är skarp och korrekt —
  sortering *ordnar* resultatet, filter *smalnar av* det. Att blanda dem bakom
  samma disclosure var en kategorifel-glidning i Batch 6-implementationen.
  Separationen är designmässigt mer korrekt, inte bara en preferens.
- **ADR 0042-konsistens:** JSDoc dokumenterar uttryckligen att Beslut A låser
  endast *filter* bakom disclosure och att sort-placeringen var ett Batch 6-
  implementationsval, inte ADR-brödtext. Detta är rätt nivå av spårbarhet för
  en IA-förfining inom Beslut A:s intention — ingen DESIGN.md- eller ADR-
  avvikelse som kräver eskalering. (Den djupare 5→3 sort-modell-frågan är
  korrekt hänvisad till senior-cto-advisor-underlaget och berörs inte här.)

---

## 3. Token-disciplin

**Bedömning: godkänd — inga nya tokens, inga hårdkodade värden.**

Den utlyfta sort-blocket återanvänder exakt samma klasser som den hade inne i
disclosuren plus container-klasser som redan används på syskon-sektionen
(Filter-disclosuren rad 201 har identisk `border-t border-border-default
pt-4`):

- `border-t border-border-default` — befintlig token (DESIGN.md §3
  border-default `#E2E8F0`), används redan på Filter-sektionen. Konsekvent.
- `pt-4` (16px) — 4px-grid (DESIGN.md §5). Matchar syskon-sektionen.
- `gap-1.5` (6px label↔kontroll) — matchar Sökord-blocket (rad 122). Konsekvent
  fält-rytm.
- `h-11` (44px) — input/select-golv enligt DESIGN.md §6 + ADR 0038. Korrekt.
- `rounded-md` (4px) — DESIGN.md §5 radius-skala (md = default). Inom 6px-gräns.
- `bg-surface-primary`, `text-text-primary`, `text-text-secondary`,
  `text-body`, `text-body-sm`, `text-label`, `border-border-default`,
  `text-danger-700`, `focus:outline-ring` — alla befintliga `--jp-*`-mappade
  tokens. Inga Tailwind-defaults (`gray-`/`slate-`/`zinc-`), inga hex.

Ingen ny CSS-variabel, ingen one-off. Token-systemet driver inte.

---

## 4. A11y

**Bedömning: godkänd — a11y oförändrad och korrekt; faktiskt en liten förbättring.**

- **Label-koppling intakt:** `<label htmlFor="filter-sort">` ↔
  `<select id="filter-sort">`. Label ovanför kontrollen (DESIGN.md §5/§6).
- **aria-describedby intakt:** växlar korrekt mellan `filter-sort-error`
  (role="alert") och `filter-sort-hint` beroende på `errors.sortBy`. ID:n
  matchar de renderade `<p>`-elementen. Oförändrat mot före.
- **Fokusring bevarad:** `focus:outline-2 focus:outline-offset-2
  focus:outline-ring` — synlig fokusring, ingen `outline: none` utan ersättning
  (DESIGN.md §9). Oförändrad.
- **Beslut D disabled-logik intakt:** `disabled={opt === "Relevance" &&
  !qReady}` + hint-texten förklarar villkoret i text (inte bara visuellt).
  Korrekt — användaren får en text-förklaring, inte bara en gråad option.
- **Förbättring:** Sortering är nu nåbar utan att först öppna en disclosure
  (förr krävdes klick på Filter-knappen → en extra tab-stopp + state-toggle
  för en vanlig kontroll). Färre interaktionssteg till en primär kontroll är
  en a11y-vinst för tangentbords- och skärmläsaranvändare. Testet
  `disables the Relevance sort option…` uppdaterades korrekt för att spegla
  detta (ingen disclosure-öppning behövs längre).
- **Disclosure-semantik intakt:** `aria-expanded`/`aria-controls` på Filter-
  knappen kvar och korrekt; IA-regressionstestet asserterar
  `aria-expanded="false"` när sort är synlig och taxonomi gömd. Bra täckning.
- **Inga icon-only-knappar utan label** introducerade. `ChevronDown` är
  `aria-hidden="true"` med text bredvid. Oförändrat.

axe/Lighthouse-verifiering sker live efter deploy (Batch 6-mönstret) — inget i
diffen indikerar någon ny violation.

---

## 5. DESIGN.md-avvikelser

**Inga.** Diffen rör sig helt inom befintliga tokens, befintliga patterns och
ADR 0042 Beslut A:s intention. Sort-separationen är en IA-förfining, inte en
ny komponent-art (jobbpilot-design-principles checklista p.2) — den
återanvänder Sökord-blockets exakta struktur. Ingen eskalering till Klas krävs
ur design-perspektiv.

JSDoc-spårbarheten (Klas produktägar-direktiv 2026-05-17 + hänvisning till
CTO-underlaget) är tillräcklig dokumentation för avvikelsen från Batch 6-
implementationsvalet. Detta är inte en deviation från en DESIGN.md-*regel* —
DESIGN.md säger ingenting om sort-placering; det är ADR 0042 Beslut A som
äger filter-disclosure-mönstret, och JSDoc gör korrekt distinktionen att
Beslut A låser *filter*, inte *sort*.

---

## Medvetet utanför scope (ej granskat som blocker mot denna diff)

Concept-id-jargongen i taxonomi-hint ("JobTech-yrkeskod (concept-id), t.ex.
MVqp_eS8_kDZ" rad 224/229) är fortsatt civic-utility-tveksam copy, men är per
uppdragsdirektiv **medvetet orörd** här — den tillhör Fynd 2 (CTO Fråga 1,
Klas-GO-väntande). Flaggas **inte** som blocker mot Fynd 1-diffen. Noteras
endast för spårbarhet inför Fynd 2.

---

## Bra gjort

- Sort-etiketterna är en genuin civic-utility-förbättring: användar-avsikt
  ("Stänger snart") över datafält-riktning ("Tidigast sista ansökningsdag"),
  parallell struktur, dubbel-"sista" eliminerad.
- Konceptuellt skarp separation av *ordna* vs *smalna av* — designmässigt
  korrekt, inte bara en layout-preferens. JSDoc-motiveringen är tydlig och
  spårbar mot rätt auktoritet (ADR 0042 Beslut A vs Batch 6-implementationsval).
- Noll nya tokens — exakt återanvändning av Sökord-blockets struktur och
  Filter-sektionens container-klasser. Token-disciplin föredömlig.
- `activeFilterCount`-städningen är korrekt och välkommenterad: sort är inte
  längre gömd → ska inte driva räknare/auto-expand. Kommentaren förklarar
  *varför*, inte bara *vad*.
- IA-regressionstestet asserterar rätt saker (disclosure stängd, Sortering
  synlig, taxonomi gömd, nya etiketter närvarande) — skyddar mot framtida
  regression av just denna IA-förfining.
- Svensk copy genomgående: du-implicit, sakligt, inga emojis, inga
  utropstecken.

---

## Sammanfattning

**0 blockers, 0 major, 0 minor som kräver åtgärd.** Diffen är en disciplinerad,
väl avgränsad och designmässigt korrekt IA/copy-förfining inom civic-utility-
ramen. Inga DESIGN.md-avvikelser, ingen token-drift, a11y bevarad och något
förbättrad, sort-etiketter mätbart bättre mot civic-utility-tonen.

**Mergeklar ur design-perspektiv** (efter Klas-GO + live visual-verify enligt
Batch 6-mönstret). design-reviewer åberopar inget veto.
