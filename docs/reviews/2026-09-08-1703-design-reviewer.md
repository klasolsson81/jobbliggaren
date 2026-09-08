# 2026-09-08 — #1703 / PR #1714 — design-reviewer

## Design-review: `/foretag/branschbevakningar` — katalograden med annonssiffror (PR #1714, #1703)

**Status:** ⚠ Changes requested
**Auktoritet:** DESIGN.md §5 (4px-rutnät) · §8 (copy) · §9 (WCAG AA-golv) · AGENTS.md §10 · §5
`Frontend:` · ADR 0047 · ADR 0120 · `jobbpilot-design-a11y` §8 (68ch) · egna bindningar B-3 (#1706),
Major 2–3 + B2 (#1681 p3 / #1707)
**Egen mätning 2026-09-08:** åtta efter-renderingar + åtta före
(`C:/tmp/jobbliggaren-visual/1703/{before,after}/`), `readings.json` per rad. Nollkontroll
bekräftad: före-setet ger 6/6 rader med `matchlines=[]` och 0 länkar; efter-setet ger varje rad sitt
tillstånd. **Dark mode:** `web/jobbliggaren-web/src/components/theme-provider.tsx:37`
`DARK_MODE_ENABLED = false` → `[data-theme="dark"]` är onåbart i appen, så ingen mörk avläsning tas.
Den nya regeln deklarerar **ingen färg** och ärver `.jp-matchline`s `--jp-ink-1`, tokendefinierad i
båda teman — tema-säker av konstruktion, inte av tur. Det är ett mätt skäl, inte en skippad kontroll.

### Blockers

Inga.

### Major

**1. Blockrådets syftning går inte att lösa ut där det renderas** — Fil:
`web/jobbliggaren-web/messages/sv/pages.json:479` (+ `messages/en/pages.json:478`)

Nuvarande: *"**Därför** visas inga annonssiffror för **de bevakningarna**. Färre branscher eller
kommuner ger färre företag."*

Mätt (`after/twenty-1280.png`, `listHeight` 3 008 px): raden står 3 008 px under listans början, och
dess **närmaste föregående rad bär "10 000+ aktiva annonser"** — ett tal. En konsekutiv konnektiv
(*"Därför"*) plus ett bestämt pronomen (*"de bevakningarna"*) kräver ett angränsande led; här
motsäger det angränsande ledet meningen. I `mixed-1280` är referenterna 2 av 6 rader,
icke-angränsande och interfolierade med rader som har tal. Detta är samma klass som min egen Major 3
i #1681 part 3 (*"från dessa företag"* — en syftning utan led), en nivå upp.

Andra halvan: grinden är `items.some(i => i.ads.tooBroad || i.matching.tooBroad)`. Om en
**matchnings-ensam** vägran någonsin är nåbar påstår den deiktiska meningen *"inga annonssiffror"* om
en rad som visar sitt annonstal. En restriktiv formulering kan inte bli falsk på det sättet — den
beskriver bara de bevakningar den namnger.

Krävs: en mening som identifierar sin mängd **på egenskap i stället för genom att peka**, så den är
sann var i listan den än landar och vid varje N:

- sv: *"Bevakningar vi inte kan räkna annonser för visas utan annonssiffror. Färre branscher eller
  kommuner ger färre företag."*
- en: *"Watches we cannot count ads for are shown without ad numbers. Fewer industries or
  municipalities mean fewer companies."*

Andra meningen är min B4-kanoniska mekanismmening, ordagrant oförändrad (konstaterande, aldrig
imperativ). Första meningen bär konsekvensen — exakt den halva `tooBroadShort` släpper — och
identifierarledet (sex ord) är en **referens**, inte den vägran-mening raden bär; CTO:ns D2-golv
förbjuder att *restatera klausulen*, vilket #1707-formen var (hela meningen två gånger). Bedömer
sessionen ändå att identifierarledet bryter golvet, är reservformen *"Annonssiffror visas inte för
alla bevakningar ovan. Färre branscher eller kommuner ger färre företag."* — och vid genuin tvist går
den till Klas, inte till en kompromiss mellan oss.

### Minor

**1. Punkt saknas i granngraden till en mening som har en** — Fil: `criterion-ad-lines.tsx:285`
(sträng: `messages/sv/jobads.json:39`)

Mätt i `mixed-1280`, rad 5: *"Inga aktiva annonser just nu."* direkt över *"Inga matchande annonser
just nu"*. Båda är fullständiga meningar, båda olänkade, samma klass, 6 px isär. **Ja, den är min att
gradera här** — paret renderas på den här ytan — men fixen får inte röra den delade strängen:
`jobads.companyWatches.matchingAds` går också in som `label` i `adsLinkAria` (*"{label} hos
{company}"*) och in i länkad text på `/foretag/bevakade`, där en punkt hamnar mitt i ett aria-namn.
Krävs i stället en egen nyckel i katalogens namnrymd för den olänkade noll-armen, i paritet med
`ads.noneStandalone`: `pages.foretag.criteria.ads.matchingNoneStandalone` = *"Inga matchande annonser
just nu."*. ⚠ Den ändrar `/oversikt`s renderade copy med ett tecken; måste någon assertion i
`criteria-summary.test.tsx` flyttas gäller CTO:ns in-block 8 (stanna och lyft), och då är rätt hem en
issue i stället.

**2. Länknamnen är identiska mellan rader — men mönstervalet är komponentens, inte deltats** — Fil:
`criterion-ad-lines.tsx:224–235, 277–283`

Mätt i `twenty-1280`: 12 count-links, **3 distinkta namn**, varje namn buret av 4 olika `href`.
Syskonkatalogen löser det med explicit `aria-label` (`company-watch-row.tsx:174, 224` →
`jobads.companyWatches.adsLinkAria`); `/oversikt` valde medvetet `<li>`-kontexten och skriver ut
skälet i sin egen kommentar (`criteria-summary.tsx:157–160`). **WCAG 2.4.4 (A) är uppfyllt här** —
`<li>` bär `<h3>`-namnet, och radens tre kontroller bär dessutom bevakningsnamnet i sina aria-labels
direkt efter länkarna, så AA-golvet håller och detta är ingen Blocker. Men katalogen har inte en
tvetydighet `/oversikt` saknar: båda når N=20. Krävs därför **inget i det här deltat** — en
aria-label på bara den tredje konsumenten forkar en komponent vars första yta är bunden oförändrad.
Fixen är komponentbred (alla tre konsumenter, `adsLinkAria`-formen) och hör hemma i en issue, inte
här.

**3. Efter-redigering-tillståndet lovar en uppdatering sidan inte gör** — Fil:
`messages/sv/pages.json:468` (renderat i `edited-1280.png`)

Rendering: direkt efter en sparad ändring faller raden på *"Annonssiffrorna för bevakningen är inte
framräknade än. De visas **här** automatiskt när de är klara."* Tillståndet är ärligt och ADR
0120-korrekt — men *"här … automatiskt"* läses lätt som att den öppna sidan fyller i sig själv,
vilket den inte gör. Ingen blockerare: användarens uppgift är slutförbar, och den verkliga
reparationen (materialisering vid redigering, ADR 0139 *(ii)*) har redan ett namngivet hem i ADR:ns
Implementationsstatus — inget andra hem ska öppnas för den.

### Bra gjort

- Länklöst blockråd + radens egen "Ändra" håller ADR 0047: mätt i `single-toobroad-1280.png` bär
  raden hela vägran (vägran + konsekvens + råd) och åtgärden ligger 53 px upp i **samma kort**, utan
  scroll — en självlänk byttes mot en synlig kontroll, inte mot en återvändsgränd.
- `.jp-criteria-advice` är rätt klass och rätt avstånd: `.jp-matchline` + 68ch i paritet med det
  levererade `.jp-appsummary__advice`, `margin: 0` så sektionens `gap-4` (16 px) äger avståndet — 2×
  listans egen 8px-rytm (`.jp-jobs`), på 4px-rutnätet, inga nya tokens, ingen färg.
- Definition → utfall är pinnad på DOM-ordning i stället för på text
  (`criteria-section.test.tsx:323`), och nollkontrollen visar att ytan nu renderar det rutten redan
  betalar för.

### Sammanfattning

0 blockers, 1 major, 3 minor. **Täthet godkänd** (mitt veto, uttryckligen utövat): 93 → 122/152 px
per rad och 2 014 → 3 008 px vid N=20 är innehåll ytan betalar för, korten behåller sin rytm och ADR
0139:s vägran att välja ett visningstak står — inget alternativ finns som inte antingen döljer ett
tal eller uppfinner ett. **Punkt 3 i din lista är inget fynd**: 15 px utfall över 14 px definition är
två levererade steg, ärvd grammatik från syskonkatalogen, och hierarkin (titel → utfall → definition)
är försvarbar. Punkt 6: självlänken var rätt att ta bort. Delegera Major 1 + Minor 1 till
nextjs-ui-engineer; Minor 2 och 3 filas eller namnges som skippar per §9.6. Re-review efter fix:
samma agent, report-only, scopad till fix-deltat (CLAUDE.md §9.6).

---

*Transcribed by the driving session: the agent is report-only and left HEAD `12d825a5` untouched
(CLAUDE.md §9.2 — the invoking session transcribes).*
