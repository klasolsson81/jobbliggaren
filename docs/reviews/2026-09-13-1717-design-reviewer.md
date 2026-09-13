# 2026-09-13 — #1717 (radgrammatiken på /oversikt) — design-reviewer

> ⚠ **Transkriberad av sessionen ur agentens svar.** Hon har inget `Write`-verktyg i den här
> konfigurationen och returnerade rapporten för transkribering. Innehållet är hennes.
> Kapad till hennes charters Output format (§9.2): inget Q&A-avsnitt, `Bra gjort` ≤ 3 rader.

## Design-review: `/oversikt` — radgrammatik i Företagsbevakning (issue #1717)

**Status:** ⚠ Bindning — form fastställd, inget delta granskat
**Auktoritet:** DESIGN.md §5 (4px-rutnät, typografisk hierarki) · §6 (N staplade block av samma typ)
· AGENTS.md §10 · `jobbpilot-design-a11y` §4/§8 · CLAUDE.md §9.6
**Routing-dom:** detta är **ingen** sanningsfråga. Vilken NIVÅ talen står på (block vs rad) är det,
och den är redan bunden och korrekt — företagssummor är exakta via unikindexet, kriteriesummor är det
inte, och den asymmetrin ska överleva. Det som återstår, namngivning och register, är presentationellt
och hennes. **Routa inte till `senior-cto-advisor`.**

### Bindningar

**B1. Blocket namnges av en overline-rubrik (h3), inte av en etikettkolumn** — Fil:
`src/components/oversikt/company-summary.tsx`, `criteria-summary.tsx`

Nuvarande: sektionen `Företagsbevakning` (h2) rymmer **två olika innehållstyper**; h2:n namnger bara
den ena. Den andra namnges enbart av räknemeningen "1 branschbevakning".

Krävs: båda blocken i companies-sektionen får en h3 som första barn i blockets rot.

```tsx
readonly heading: string | null;  // obligatorisk, ingen default — samma regel och skäl som linkHref
{heading !== null && <h3 className="jp-appsummary__heading">{heading}</h3>}
```

Motivering — en **mätning, inte en tolkning**: repot har redan graderat exakt det här felet, men bara
för en modalitet. `criteria-summary.tsx:21-24` bär `design-reviewer Minor 5`, som lagade *"a bare list
under a section headed Företagsbevakning"* med `aria-labelledby`. Skärmläsaren reparerades; den seende
läsaren fick ingen motsvarighet. Klas rapporterar den olagade halvan av ett fynd repot självt redan
kallat ett fel. Regeln, tillämpbar: *varje innehållsblock namnges av närmaste rubrik ovanför sig.* I
sektioner med ett block gör h2:n redan det. Bara companies-sektionen bryter regeln.

**B2. Registret är husets overline — samma device som notisetiketten** — Fil:
`src/app/globals.css` (ny regel intill `.jp-appsummary__watchname`, ~5160)

```css
.jp-appsummary__heading {
  margin: 0;
  font-family: var(--jp-font-mono);
  font-size: var(--text-overline);      /* 11px */
  font-weight: var(--jp-fw-bold);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--jp-ink-1);
}
```

Motivering: overline är redan husets namn-på-block — sidfotens kolumnhuvuden, hero-kickern,
header-stats och `.jp-notice__label` (5226–5232). **Skillnaden mot notisetiketten är scope, inte
register:** etiketten namnger en RAD, i radens egen kolumn; den här namnger ett BLOCK, över blocket.
Det är precis den enhetliga grammatik Klas efterlyser — utan att någon etikettkolumn införs och utan
att någon etikett tas bort. Inga nya tokens. `--jp-ink-1`, aldrig ink-2: ink-2 klarar kontrast, men
11px grått är exakt den grå småtext Klas stående avvisat, och ett strukturnamn ska inte spendera
kontrastmarginal. Uppercase sätts i CSS medan DOM-texten är normalskriven, så AT läser den som ord —
samma konstruktion som `.jp-notice__label` redan använder.

**B3. Exakta strängar — ingen ny vokabulär** — Fil: `messages/sv/oversikt.json`

```
"companySummary.heading":  "Bevakade företag"
"criteriaSummary.heading": "Branschbevakningar"
```

Båda substantiven finns redan på ytan (`link`-strängarna, och routen `/foretag/branschbevakningar`).
Länktexterna **förkortas inte** till "Visa alla": två identiska "Visa alla" på en sida är precis det
WCAG 2.4.4-fall `company-summary.tsx:150-155` redan betalat för att undvika. Upprepningen av
substantivet är priset för tillgänglig länktext och är medvetet accepterad.

**B4. Omfattning, och vad som INTE ändras**

- **Omfattas:** `CompanySummary` + `CriteriaSummary` **som de anropas från `oversikt-page.tsx:455/464`**.
- **Omfattas inte:** `ApplicationSummary` får `heading={null}`, gäst-`CompanySummary`
  (`guest-oversikt-page.tsx:264`) får `heading={null}`. Båda renderar **byte-identiskt** — de uppfyller
  redan regeln i B1, eftersom deras sektioner har en enda innehållstyp.
- **h2 "Företagsbevakning" behålls.** Den är paraplyet: båda barnen är bevakningar på företag. Ett byte
  vore en produktvokabulär-ändring Klas inte bett om, och den namnger dessutom notis-source:n
  `companies`. Vill han byta ordet är det hans beslut, inte mitt.
- **Min egen B3 från #1707 öppnas INTE.** Den avslog *etiketten i ankaret*, på premissen att blocket då
  tappar sitt substantiv på vänsterkanten. Jag lägger en rubrik **ovanför** ankaret och rör inte
  `t("anchor", {count})`. Premissen står, ankaret står, ingen omprövning.

### Avslag — båda riktningarna sessionen erbjöd, med mätning

**A1. Etikett TILL sammanfattningarna — avslås.** En 130px-kolumn kostar 148px innehållsbredd vid 1280
på block vars prosa redan kapas vid 68ch, och lämnar en död kolumn i `--empty` (titel+brödtext+CTA) och
`--unavailable` (ensam `<p>`), som inte har någon rad att etikettera. Som fristående overline ovanför
blocket vore den dessutom en tautologi där h2:n redan namnger typen ("Mina ansökningar" →
"ANSÖKNINGAR"). **Sessionens mätning att alla tre blocken har `etikett=null` är riktig — men den regel
som löser Klas fråga är rubrik-per-innehållstyp, inte etikett-per-block, och under den regeln är
ansökningsblocket redan korrekt.** Harmoniseringen omfattar därför **två** block, inte tre; det är inte
en motsägelse av avläsningen utan en annan regel över samma mätning.

**A2. Etiketten BORT från notisraderna — avslås, den bär funktion.** Mätt: `oversikt-page.tsx:386-398`
bygger kryssrutornas etiketter ur `prefLabels[id]`, nycklat på samma notistyp som radens `notice.label`.
Etiketten är det **synliga namnet på den typ kugghjulets kryssruta stänger av** — tas den bort kan en
användare som bockar ur "Matchning" inte se vilka rader som försvann. Etiketten är dessutom enda
bäraren av kind-färgen (`globals.css:5226-5229`). Ett avlägsnande harmoniserar genom att platta ut, och
raderar den enda axel som i dag skiljer HÄNDELSER (tidsstämplade, avfärdbara) från STÅENDE TILLSTÅND —
en skillnad båda docblocken uttryckligen bär.

### Tomt-, ohämtbar- och laddningsläge

Rubriken renderas i **alla tre lägena, ovillkorligt** — den namnger blocket, och tomt-läget är precis
när läsaren mest behöver veta *vilket* block som är tomt. `__emptytitle` behålls oförändrad;
"BRANSCHBEVAKNINGAR" över "Du har inga branschbevakningar än" är normal GOV.UK-form och "än" bär det
meningsbärande. `--unavailable` är i dag en ensam
`<p className="jp-appsummary jp-appsummary--unavailable">`. Med rubrik måste den grenen bli `<div>`
som omsluter h3 + `<p style margin:0>`. **Behåll `<p>`-formen när `heading === null`** — då förblir
`ApplicationSummary` och gästytan orörda. Selektorn `.jp-appsummary:has(+ .jp-appsummary)` (5175)
fortsätter matcha eftersom klassen sitter kvar på rotelementet. `loading.tsx:94-101`: lägg **en** extra
reserverad rad (`jp-skeleton block h-3 w-32`) över vardera ankaret — **bara** i companies-sektionen,
inte i ansökningssektionen.

### Viewports

Ingenting viewport-betingat. Overline är en kort enradstext; `.jp-container` kapar bredden vid 3440, så
1280/1920/3440 får identisk form. Inga media queries tillkommer.

### Bra gjort

- `CriteriaSummary`-docblocket härleder varför blocket inte speglar `CompanySummary` — bunden sanning.
- Ankarets `t.rich` med `lnk`-chunk ger synligt namn = tillgängligt namn i en nyckel; 2.5.3 håller.
- `--unavailable` skilt från `--empty` är rätt: "omätt" och "noll" är olika påståenden.

### Krav på efter-avläsningen

1. Companies-sektionen: exakt **2** `h3.jp-appsummary__heading`, texter "Bevakade företag" /
   "Branschbevakningar", båda med samma `x` som ankaret under sig (**72** vid 1280).
2. **0** `h3.jp-appsummary__heading` i ansöknings- och jobbannonssektionen.
3. Ansökningssektionens renderade geometri **identisk** med `before/readings.json`.
4. `.jp-notice__label`-antalet oförändrat (**3** i n1) — A2 avslogs, etiketterna står kvar.
5. Computed style på h3: `font-family` mono, `font-size` 11px, `text-transform: uppercase`, `color` =
   resolverad ink-1 — **i båda teman**.
6. Kontrast h3 mot canvas: förväntat **16,1:1** ljust (`#0C1A2E` på `#F4F6FA`) och **17,0:1** mörkt
   (`#F4F7FC` på `#0B1525`). **Mät, ärv inte mina tal.**
7. Rubriken finns i **alla tre lägena**: n1, empty-criteria + empty-watches, **och en
   `--unavailable`-avläsning som saknas i dag** — bindningen ändrar den grenens DOM, så AGENTS.md §8
   punkt 4 kräver den renderad före verdict. Lägg till den i stub-fixturen.
8. Rubriknivåer i a11y-trädet: h1 → h2 → h3, inget hoppat steg; `aria-labelledby={TOTALS_ID}` på
   `ul.jp-appsummary__watches` resolverar fortfarande.
9. 1280/1920/3440: ingen radbrytning av rubriken, ingen horisontell overflow.
10. Skelett→laddad: layoutskiftet i companies-sektionen växer inte jämfört med `before`.

### Utanför scope (Minor, fila inte som blockerare)

`CriterionAdLines`-radernas kvalificerare ("…just nu") mot företagsradens ("…hos dina bevakade
företag") är den sista kvarvarande asymmetrin i Klas led 2. Rörs **inte** här: komponenten delas av
fyra ytor (`/oversikt`, katalogen, två detaljsidor), så en copy-harmonisering ändrar tre ytor för ett
klagomål rest om en. Egen issue om den ska göras.

### Sammanfattning

4 bindningar (B1–B4), 2 avslag med mätning (A1, A2), 1 Minor utanför scope. **Ingen Blocker — det
finns inget delta.** Implementationen går till `nextjs-ui-engineer`. Re-review efter fix: samma agent,
report-only, scopad till fix-deltat (CLAUDE.md §9.6) — och den kräver en `--unavailable`-avläsning som
inte finns i `before/`.

---

## SKOPAD OMKONTROLL (rapport-läge) — fix-deltat `44806e0d..d7a40753`

**Status:** ✓ Approved — 0 Blockers, 0 Major, 1 ny Minor
**Skop:** endast fix-deltat. Detta var hennes enda skopade omkontroll (CLAUDE.md §9.6).

### Stängda punkter — mätta av henne, inte ärvda

**Minor 1 — STÄNGD.** Verifierad som en **ren flytt**: 23 rader ut, 23 in, regelkroppen byte-identisk.
`.jp-appsummary__heading` ligger nu direkt efter `.jp-appsummary--unavailable` och före
`#1558`-matchningsraden — i block-klustret, före rad-klustret. `__watch` → `__watchname` är
angränsande igen, vilket var hela poängen.

**(b) — STÄNGD.** Båda inline-stilarna borta, **ingen** CSS-regel tillagd, de tre syskonreglerna
orörda. Den stängande mätningen diskriminerar och hon tog om den själv: `unavailable`-blocken mäter
**63px utan inline-stilen, identiskt med 63px med den** — beviset att preflight bar `margin: 0` hela
tiden. 95 hade motbevisat det. Hon läste `preflight.css:7-16` och grepade bort bar `p`-regel,
`.jp-appsummary p`-descendant och typography-plugin innan hon svarade.

**(a) — STÄNGD MEKANISKT.** Kört av henne: `git diff f987c62d..d7a40753 -- .../application-summary.tsx`
→ **0 rader**. Komponenten är byte-identisk med basen; *"renderar byte-identiskt"* är nu ett mätvärde.
Fencen hållen — `CompanySummary` och `CriteriaSummary` bär båda `readonly heading: string | null`.
Båda pinnarna överlevde. Minor 3 lämnad som beslutat.

**Renderat:** `after2/` jämfört mot `after/` fält för fält i alla åtta avläsningarna. Företagsblockens
`blockBox` identiska i alla åtta, rubrikerna identiska, `h3`-antalet 2 överallt med 0 i ansöknings-
och jobbannonssektionen, notisetiketterna oförändrade, `docWidth == viewportWidth` överallt.
**Ingen renderad regression.**

### Ny Minor i fix-deltat

**1. Föråldrad räkning i docblocket efter (a)** — Fil: `criteria-summary.tsx:44`

Nuvarande: *"The `null` arm exists so the **three** summaries share one contract"* · Krävs: **radera
meningen** · Motivering: (a) tog bort propen ur `ApplicationSummary`, så kontraktet delas nu av **två**
komponenter. Fel tal i en kommentar är per AGENTS.md §5 `Comments:` en defekt som lagas; §12 undantar
just det blocket från STOPP, så den blockerar inte.

⚠ **Radera, skriv inte om till "the two".** En omskriven påståendemening är ett *tillagt* påstående,
och §9.6 stänger ett fynd genom att radera text eller ändra kod — aldrig genom en ny påståendemening.
Raderingen stänger **mekaniskt**, **ingen ytterligare omkontroll skyldig**.

### Sammanfattning

**Inget nytt Blocker/Major i fix-deltat.** Alla tre punkter stängda med mätning, två av dem mekaniskt.
En ny Minor, stängd genom radering. Hennes enda skopade omkontroll är förbrukad; efter raderingen är
designgranskningen av #1717 avslutad.
