# Design-review: `/oversikt` bento-grid (PR #1724)

**Agent:** design-reviewer · **Datum:** 2026-09-13 · **Head:** `59ff7f76` · **Issue:** #1723
**Status:** ⚠ Changes requested
**Auktoritet:** DESIGN.md §1.2 (rad 112 touch-targets) · §3 (tokens/skuggor) · §5 · §6 · ADR 0140 Beslut 1–3 · `jobbpilot-design-tokens` · AGENTS.md §8 p.4

Renderat mot korpusen i `C:/tmp/jobbliggaren-visual/bento/` plus fem tillstånd som saknades där och som agenten renderade själv (`C:/tmp/jobbliggaren-visual/bento-dr/`): `crit-toobroad-one`, `crit-zero-one`, `watch-notlinkable`, `watch-filtered`, `popover-1280/400`. Arbetsträdet lämnat orört (HEAD `59ff7f76`, `git status` tomt). Den mörka "N"-cirkeln i vänsterkanten på alla skärmbilder är Next dev-indikatorn, inte deltat.

### Blockers

1. **"N nya"-pillen är 24px hög även under 768px — husets golv är 44** — Fil: `web/jobbliggaren-web/src/app/(app)/app.css:2196-2212` (`.jp-ov-card__head .jp-ov-card__pill`)
   Nuvarande: `height: 24px` utan media-override. Mätt 57×24 vid 1280, 768 **och** 400 — medan `.jp-notice__dismiss` fyra rader ned i samma `@media (max-width: 768px)`-block korrekt bumpas till 44×44 (mätt).
   Krävs: lägg pillen i 768-blocket, t.ex. `.jp-ov-card__head .jp-ov-card__pill { height: 44px; padding: 0 14px; }` — huvudraden bär redan en 40px ikonruta, så radhöjden växer 4px.
   Motivering: DESIGN.md rad 112 "touch (≤768px) bumpar hit-targets till 44px" är en skriven regel utan undantag här; ADR 0140 Beslut 3 räknar upp `.jp-btn` 44 / `--sm` 36 / `.jp-notice__dismiss` 32→44 och **nämner inte pillen** — alltså en odokumenterad avvikelse, inte ett beslut. Pillen är dessutom kortets enda väg till `/foretag/bevakade/nya`, och §1.1-målanvändaren (55-åriga jobbsökare) är just det skäl höjderna bumpades en gång (DESIGN.md rad 143).

### Major

2. **`.jp-transparency-note` renderas utan luft i Bevakade företag-kortet** — Fil: `src/components/oversikt/companies-card.tsx:105-116`
   Nuvarande: mätt `marginTop: "0px"`, `gapFromPrev: 0` i båda nåbara lägena (`watch-notlinkable.png`, `watch-filtered.png`). `.jp-appsummary` gav raden `gap: 6px`; `.jp-ov-card` sätter ingen `gap` alls utan låter barnen bära egen marginal — och `.jp-transparency-note` har ingen.
   Krävs: `.jp-ov-card .jp-transparency-note { margin-top: 12px; font-size: var(--text-body-sm); }` — 12px är kortets egen sekundärrads-marginal, och 14px hindrar att en 16px medium-fet upplysning väger tyngre än `.jp-ov-sub` (14px) den kvalificerar.
   Motivering: DESIGN.md §5 4px-rutnät + separation av likartade block; i dag läser "Antalen ovan saknar länk." som en fortsättning på ankarraden i stället för som kortets upplysning.

3. **Rådet i det breda branschkortet får 6px i stället för 18px — specificiteten äter regeln** — Fil: `src/app/(app)/app.css:2489` (`.jp-ov-criteria__advice`) mot rad 2484 (`.jp-ov-card .jp-matchline`)
   Nuvarande: elementet bär båda klasserna; `.jp-ov-card .jp-matchline` (0,2,0) slår `.jp-ov-criteria__advice` (0,1,0). Mätt `marginTop: "6px"`, `adviceGapFromLastRow: 6` i `criteria-many-1280.png`.
   Krävs: höj selektorns specificitet, `.jp-ov-card .jp-ov-criteria__advice { margin-top: 18px; }`.
   Motivering: exakt det fynd en tidigare design-reviewer-runda stängde på den gamla ytan — den raderade `.jp-appsummary__advice`-kommentaren bär domen ordagrant ("ett BLOCK-påstående låg tätare mot sista bevakningen än ankaret mot listan, så det läste som en fortsättning på just den raden", omkontroll 2026-09-07). Regressionen är tyst.

4. **ADR 0140 + tokens-skillen påstår en mätning som inte håller i dark** — Fil: `docs/decisions/0140-...md:61`, `.claude/skills/jobbpilot-design-tokens/SKILL.md:147-149`
   Nuvarande: "Alla textpar ≥ 4,5:1, alla UI-komponentpar ≥ 3:1, i båda teman." Paret PR:en **inför** — solid CTA-fyllning mot sitt eget korts tint — saknas i tabellen och mäter i dark: accent-800 på `#0E2A1E` **2,03:1**, follow-fill på `#153338` **2,31:1**, info-fill på `#1B3358` **1,64:1** (light: 6,62 / 4,92 / 6,28).
   Krävs: stryk generaliseringen "alla UI-komponentpar ≥ 3:1, i båda teman" och lägg in raden med sina verkliga tal + villkoret att dark ligger vilande (`theme-provider.tsx:37` `DARK_MODE_ENABLED = false`), så skulden står skriven inför flippen.
   Motivering: DESIGN.md §3 och skillen är kanon för tokens; en falsk siffra i en ratificerad ADR citeras vidare av nästa agent. Jag vetar **inte** dark-renderingen (Klas-direktiv: dark lägst prio, ytan är onåbar) — bara påståendet om den.

5. **Tre identiska "Visa matchande annonser" i rad, utan särskiljande accessible name** — Fil: `matching-card.tsx:86`, `companies-card.tsx:124`, `criteria-card.tsx:142`
   Nuvarande: mätt tre länkar med samma text, `aria-label: null`, tre olika `href` (`/jobb?occupationGroup…`, `/jobb?employer…`, `/foretag/branschbevakningar/c1/annonser…`). I en länklista hörs samma post tre gånger.
   Krävs: ge var och en ett suffix via `aria-label`, t.ex. "Visa matchande annonser från bevakade företag" / "… i din branschbevakning", med den synliga texten först (2.5.3 Label in Name) — samma form som `cards.newPillAria` redan använder.
   Motivering: husets egen skrivna regel i `company-summary.tsx` ("There is exactly one such link on the page … The watch card is the opposite case and does carry one"). WCAG 2.4.4 klaras formellt via `aria-labelledby` på sektionen, så detta är komposition, inte AA-fall.

6. **Matchning vid 0 behåller en solid primär till en lista som är tom per konstruktion** — Fil: `matching-card.tsx:78-88`
   Nuvarande: `empties-1280.png` visar "**0** annonser matchar dina val" + grön solid "Visa matchande annonser". `matchHref` bär enligt komponentens egen docblock "EXACTLY the facets the count hard-filters on" — alltså är destinationen 0 träffar per konstruktion. Systerkortet vid samma nolla gör tvärtom: `crit-zero-one.png` visar "**0** matchande annonser" **utan** CTA.
   Krävs: vid `matchCount === 0`, byt CTA till `--emphasis`-nivån och till den handling som hjälper — `cards.matchingCtaZero` "Justera dina val" → `/oversikt?matchsetup=1` (destinationen setup-läget redan äger), alternativt "Sök bland alla annonser" → `/jobb` utan facetterna. Och samordna nollan mellan de två korten.
   Motivering: DESIGN.md §6 (tomläge ger ett konkret nästa steg) + ADR 0047-flödet: den starkaste affordansen på sidan pekar mot ingenting, och två syskonkort besvarar samma nolla på motsatt sätt. Handoffens rad 89 ("CTA kvar") är en ritningsregel, inte ett Klas-beslut, och ADR 0140 bär den inte.

### Minor

7. **Skelettet är osynligt på de tre tintade korten** — Fil: `src/app/(app)/oversikt/loading.tsx:72-82`
   `.jp-skeleton` = `--jp-surface-3` mäter **1,03 / 1,01 / 1,04:1** mot accent-50 / follow-bg / info-bg (husets baslinje mot vitt kort: 1,18:1). `loading-1280.png` visar tre tomma färgade rektanglar.
   Krävs: `.jp-ov-card--follow .jp-skeleton { background: var(--jp-follow-border); }` (1,20 light / 1,51 dark) och `--info` → `var(--jp-info-border)` (1,18 / 1,53). För accent-kortet duger inte `--jp-accent-100` — det kollapsar på `--jp-accent-50` i dark (1,00:1) — välj `--jp-border-soft`.

8. **Vägrat branschtal ger ett kort utan tal där syskonen har ett** — Fil: `criteria-card.tsx:118-123` (`crit-toobroad-one.png`)
   Vid `tooBroad`/`notMaterialised` utgår `OversiktNumber` helt, så raden läser 245 / 9 / prosa och tappar sin baslinje. Överväg en-dash i talplatsen (ADR 0140 Beslut 1:s "aldrig en blank cell") så de tre korten behåller rutnätets grammatik; vägrans-texten står kvar som skäl.

9. **Radlinjen mellan bevakningar i det breda kortet är svagare än husets liggarlinje** — Fil: `app.css:2465` — `--jp-info-border` mäter 1,18:1 mot tinten där den gamla ytan använde `--jp-border` (1,52:1) för samma roll. 24px luft bär i dag skillnaden; överväg en egen radlinje-token om liggaren växer mot `MaxPerUser` 20.

10. **Popover-raderna är 38px även under 768** — Fil: `globals.css` `.jp-notice-prefs__row` (befintlig regel, ej införd här). Deltat tredubblar dock raderna i **en** popover (9 typer, mätt 300×494 vid 400px), så det är tillfället att bumpa till 44.

### Bra gjort
- HeaderStats-doktrinen håller på alla fyra tal: `errors-1280.png` visar en-dash utan enhet, `unavailable`-copy och **noll** CTA:er — inga påhittade nollor.
- D1 håller: ingen summa vid ≥2 bevakningar, span-reflowen 4→6/12 mätt vid 1280 och 3440, och behållaren sträcks aldrig (x=1152, w=1136 vid 3440).
- Fokusring 2px/2px offset mätt på varje ny kontroll, `tabindex>0` = 0, DOM-ordning = visuell ordning, ett kugghjul, popovern grupperad och oklippt vid 400px.

### Sammanfattning
1 blocker, 5 major, 4 minor. Blockern och major 2/3 är rena CSS-fixar; major 4 stängs mekaniskt genom att stryka meningen; major 5/6 kräver i18n-nycklar. Delegera till nextjs-ui-engineer. Re-review efter fix: samma agent, report-only, scopad till fix-deltat (CLAUDE.md §9.6).

---

## Omkontroll (rapport-only, skopad till fix-deltat `59ff7f76..8a8edfce`)

Mätt på head `8a8edfce`, renderad lokalt (`localhost:3014` + stub på `59994`, agentens egna fixtures för de tillstånd standardstubben saknar). Korpus: `C:/tmp/jobbliggaren-visual/bento-rc/`.

**Status:** ✓ Approved — 0 blockers, 0 major kvar
**Auktoritet:** DESIGN.md §1.2 (rad 112) · §3 · §5 · §6 · ADR 0140 Beslut 2–3 · `jobbpilot-design-tokens` · CLAUDE.md §9.6
**Scope:** enbart fix-deltat mot de tio fynden. Inga nya fynd i deltat.

### Fynd-status

1. **Blocker 1 — pillen 24px under 768** — **STÄNGT** · Mätt: 768 → 65×44 (`height "44px"`, `padding "0px 14px"`), 400 → 65×44, 1280 oförändrat 57×24. Vit text på `rgb(62,108,116)` bevarad. Renderat: pillen balanserar mot 40px-ikonrutan. ADR 0140 Beslut 3 namnger nu golvet.
2. **Major 2 — `.jp-transparency-note` utan luft** — **STÄNGT** · Mätt i båda nåbara lägena: `marginTop "12px"`, `gapPrev 12`, `fontSize "14px"` (var 0 / 0 / 16px).
3. **Major 3 — rådets 18px åts av specificiteten** — **STÄNGT** · Mätt vid `criteria=many`: `marginTop "18px"`, gap från sista raden 18 (var 6/6). Samma (0,2,0) som `.jp-ov-card .jp-matchline` och senare i källordning — rätt mekanism, ingen `!important`.
4. **Major 4 — falskt dark-påstående i ADR + skill** — **STÄNGT mekaniskt** · Meningen struken; tillagd tabellrad bär de verkliga talen (6,62 / 4,92 / 6,28 light; 2,03 / 2,31 / 1,64 dark) plus villkoret `DARK_MODE_ENABLED = false`. Skulden står skriven, inte bortskriven.
5. **Major 5 — tre identiska CTA-namn** — **STÄNGT** · Mätt accessible names: "Visa matchande annonser för dina val" / "… från bevakade företag" / "… i din branschbevakning" — tre distinkta, var och en inledd med den synliga strängen (2.5.3).
6. **Major 6 — räknad nolla behöll solid primär** — **STÄNGT** · Mätt vid `match=0`: klass `jp-btn jp-btn--emphasis jp-ov-cta`, "Sök bland alla annonser", bg `rgb(233,242,237)`, href `/jobb` utan facetter. ADR Beslut 3 skriver nu ut regeln för båda kortens nolla. Copy godkänd.
7. **Minor 7 — skelettet på tintade kort** — **DELVIS STÄNGT** · follow 1,20:1, info 1,18:1 når baslinjen; accent-kortet med `--jp-border-soft` mäter 1,08:1 (var 1,03) och ligger kvar under. Dark klarar alla tre. Kandidat som klarar båda teman: `--jp-border` (1,33 light / 2,22 dark). Minor — blockerar inte.
8. **Minor 8 — en-dash för ett vägrat branschtal** — **SKIP ACCEPTERAT, men inte på pränt** · Skälet håller (en-dashen är reserverad för en omätt läsning). Mätt på PR-kroppen vid `8a8edfce`: skippen finns inte där. Stängs av den enda PR-kropps-editen (§9.2).
9. **Minor 9 — radlinjen i det breda kortet** — **SKIP ACCEPTERAT, men inte på pränt** · samma mätning som 8.
10. **Minor 10 — popover-raderna under 768** — **STÄNGT** · Mätt vid 400: nio rader × 44px, popovern 300×545, inte klippt. `b0e98b75`:s omskrivning till `.jp-notice-prefs__heading + .jp-notice-prefs__grouptitle` mätt korrekt: första grupprubriken `border-top 0px` / `margin-top 0px`, de två följande `1px` / `4px`.

### Bra gjort
- Varje fix bär sin mätning i en CSS-kommentar med fyndnumret; ingen `!important`, ingen ny hex, inga nya tokens.
- ADR 0140 bär nu både pill-golvet och nollregeln som beslut i stället för som repareringar i koden.
- Ingen regression i deltat: sex kort, ingen horisontell scroll 400–3440, pillens vita text och de tre axelfärgerna oförändrade.

### Sammanfattning
0 blockers, 0 major. Fynd 1–6 och 10 stängda mot mätning; 7 delvis stängt (Minor); 8 och 9 skip accepterade i sak, spåret ska med i den avslutande PR-kropps-editen. Inget hindrar `agents-done`. Detta var agentens enda skopade omkontroll (§9.6-cap).

**Sessionens disposition av Minor 7 efter omkontrollen (kodändring, stängd mot fyndets egen mätning):** `.jp-ov-card--accent .jp-skeleton` byter `--jp-border-soft` → `--jp-border`; WCAG-formeln över `globals.css`-värdena ger 1,33:1 light (`#C9D2E0` på `#E9F2ED`) och 2,22:1 dark (`#44598A` på `#0E2A1E`) — hennes egna kandidattal.
