# Design-review: Fas E2i — spegel-sökfält (branch `feat/sok-paritet-spegel-sokfalt-e2i`, working tree mot main `5f4e1cc`)

**Status:** ⚠ Changes requested — 0 Blockers, 2 Major, 4 Minor, 3 rendered-flaggor
**Granskat:** 2026-06-12 (uppdrag daterat 2026-06-11)
**Auktoritet:** DESIGN.md via `jobbpilot-design-a11y` (§4, §6, §9), `jobbpilot-design-copy` (ton, destruktiva affordances), `jobbpilot-design-principles` (civic-utility), ADR 0047 (Area 5)

**FAS-DEFERRAL-MANIFEST (kvitterat, bindande):** Rendered = pending Klas lokala test — rendered-fynd flaggas för den iterationen, vetoas inte här. Minus-operatorn out-of-scope. Klas-låsta val ifrågasätts inte: normal spegel-ruta (chips-i-fältet rivet), alla taggar inkl. q-ord i filter-raden, "Rensa alla filter" nollar ALLT. Granskningen gäller utförandet av de låsta valen, inte valen själva.

### Blockers

Inga. Ingen AI-estetik har smugit in (inga gradients, ingen glassmorphism, inga nya skuggor), token-disciplinen är intakt (all ny CSS går via `--jp-*`-tokens med befintliga dark-par), och combobox-mönstret är oförändrat WAI-ARIA-korrekt.

### Major (ska fixas innan merge)

**M1. Chip-×-knappens hit-area är ~16×16 px — under husgolvet 32×32 (a11y-skill §9)**
Fil: `web/jobbpilot-web/src/app/globals.css` rad 1846–1853 (`.jp-filterchip__rm` — `padding: 2px` + 12 px-ikon) renderad i `web/jobbpilot-web/src/components/job-ads/jobb-results-toolbar.tsx` rad 188–199.
CSS:en är pre-existing, men E2i gör ×-knappen till **primär borttagningsaffordance även för sökord** (Klas-spec: allt blir taggar) och diff:en rör exakt detta element (ny aria-branching). §9.6 in-block-default gäller: fixen är trivial CSS. Formellt är WCAG 2.1 AA inte bruten (2.5.5 är AAA), men a11y-skillens golv "in-app minimum 32×32" är husets spec och checklistan kräver det — chipen är 32 px hög, så knappen kan fylla höjden: t.ex. `align-self: stretch; padding: 0 8px; margin-right: -10px;` (≈32×28) utan visuell förändring av ikonen. Touch-bumpen (44 px ≤768px) ska verifieras i rendered-passet.

**M2. "Rensa alla filter" säger mindre än den gör — och motsäger radens egen terminologi**
Fil: `web/jobbpilot-web/src/components/job-ads/jobb-results-toolbar.tsx` rad 202–208 (länktext), rad 174 (`aria-label="Aktiva filter"`), rad 192–196 (aria-copyn).
Beteendet är Klas-låst (nollar ALLT inkl. sökorden) — det granskas inte. Men copyn skapar nu en intern motsägelse: ×-knapparnas aria skiljer korrekt "sökordet X" från "filter X" (bra!), samtidigt som rensa-länken och container-etiketten kallar alltihop "filter". ADR 0047: en handling som även raderar användarens egenskrivna söktext ska kommunicera det före klicket. Krav:
- Länktext: `Rensa alla filter` → **`Rensa sökord och filter`** (alternativt `Rensa hela sökningen`; det förra är mest precist och kortast ärligt)
- Container: `aria-label="Aktiva filter"` → **`Aktiva sökord och filter`** (se även Mi3 om role)
Beteendet i sig är inte irreversibelt (bakåt-navigering återställer URL-staten) — därför Major, inte Blocker.

### Minor (nice-to-fix)

**Mi1. Hjälptexten säger inte VAR taggarna hamnar.**
`jobb-hero-search.tsx` rad 293. "Ord blir taggar när du skriver mellanslag eller komma." — i E2i-modellen syns taggen i filter-raden vid träfflistan, inte i fältet. En förstagångsanvändare vid hero:n ser ingen tagg uppstå. Förslag: **"Ord blir taggar i filterraden vid träffarna när du skriver mellanslag eller komma. Välj förslag med piltangenterna och Tab."** Ton-mässigt är befintlig copy annars korrekt (du-form, inga utropstecken, konkret) — q-max-varianten med "Ta bort en tagg för att lägga till fler ord" är föredömlig.

**Mi2. aria-live-annonsen re-annonserar inte identisk sträng.**
`jobb-hero-search.tsx` rad 122, 163, 299–301. Sekvensen: skriv "volvo " ("Lade till volvo") → ta bort via toolbar-× (hero-annonsen rörs inte) → skriv "volvo " igen → `setAnnouncement("Lade till volvo")` är samma state-värde → ingen DOM-ändring → skärmläsaren tiger. Mildras av att `jp-results-count` (role="status") annonserar nytt träfftal, men livscykel-täckningen har ett hål. Förslag: nollställ `announcement` i extern-divergens-sentinelen (rad 141–156), eller suffix:a med växlande ` `.

**Mi3. Chips-containern är en `div` med `aria-label` utan role — namnet exponeras inte.**
`jobb-results-toolbar.tsx` rad 172–175. `aria-label` på generisk `div` ignoreras av de flesta skärmläsare (ingen role → ingen name-beräkning). Lägg `role="group"` (tillsammans med M2-namnbytet). Belt-and-braces för 1.4.1-frågan: ett `<span className="sr-only">Sökord: </span>`-prefix i q-chipen skulle ge typen även vid ren chip-uppläsning — idag bärs distinktionen enbart av ×-knappens aria-label, vilket är godkänt men inte generöst.

**Mi4. Fokus tappas när en chip tas bort med tangentbord.**
När × aktiveras unmountas chipen och fokus faller till `body` — tangentbordsanvändaren börjar om från sidtopp. Pre-existing mönster (toolbar-× fanns före E2i) men exponeringen växer när sökorden blir chips. Förslag till nextjs-ui-engineer: flytta fokus till nästa chip-×, annars till "Rensa"-länken, annars till sökfältet. Verifieras i rendered-passets keyboard-test.

### Svar på de riktade frågorna

1. **Search-ikonen som särskiljare:** Godkänd för WCAG 1.4.1 — distinktionen bärs av ikon (form), inte färg, och `aria-label`-grenen "Ta bort sökordet X" vs "Ta bort filter X" bär den för skärmläsare. 12 px är subtilt men konsekvent med Briefcase/MapPin-paret. Se Mi3 för frivillig förstärkning.
2. **"Rensa alla filter":** Inte ärlig nog — se M2. Beteendet är låst och ifrågasätts inte; texten ska ikapp beteendet.
3. **Spegel-begriplighet (ADR 0047):** se rendered-flaggorna nedan — modellen är försvarbar men måste verifieras renderat.
4. **Hjälptexten:** behöver platsangivelse — se Mi1.
5. **a11y-täckning:** combobox-mönstret är opåverkat av `suggestQuery` (värdet förblir fältets text; `aria-activedescendant`/`aria-expanded`/listbox oförändrade; caret-ordet som suggest-prefix är samma mönster som adressfält). Tagg-livscykelns aria-live har hålet i Mi2 samt att externa ändringar (toolbar-×/Rensa) endast annonseras indirekt via träfftalet — acceptabelt, dokumenterat.

### Rendered-flaggor (pending Klas lokala test per manifest — vetoas inte nu)

- **R1 — synlig feedback-närhet:** när ett ord blir tagg ändras inget visuellt vid fältet; taggen uppstår i filter-raden vid träfflistan, potentiellt under viewporten vid hero-läget. Norman/system-status-ankaret är granskningens viktigaste rendered-fråga: räcker hero-öns "Visa N annonser"-uppdatering som närfeedback, eller behövs en iteration?
- **R2 — delmängds-spegeln:** popover-valda filter och icke-representabla labels syns aldrig i fältet (invariant I1). Risk att användaren läser fältet som HELA sökningen och blir överraskad av träffmängden. Verifiera att filter-raden upptäcks som total spegel.
- **R3 — enradig återställning:** `.jp-hero__searchfield`-CSS:en löser E2h:s wrap-bugg per konstruktion (enradig input, `overflow: hidden` på searchrowen, inåtvänd fokusring per WCAG 2.4.7-kommentaren) — bekräfta i båda teman att lång spegeltext beter sig (scrollande text, ingen klippt fokusring).

### Bra gjort

- **E2d/E2h-felklassen är löst i grunden:** texten består alltid — ord försvinner aldrig ur fältet vid taggning, och kirurgisk borttagning (`updateTextForStateChange`) bevarar användarens ordordning i stället för att om-sortera under händerna. Detta är rätt civic-utility-instinkt: användarens text är användarens.
- Ingen placeholder — label + hjälptext bär instruktionen (Klas hårda regel hedrad, `aria-describedby`-kopplad).
- "gissa aldrig"-principen i parsern (ambiguösa n-gram → fritext) är exakt rätt för en civic utility — hellre ärlig fritext än fel filter.
- No-JS/pre-hydration-vägen är genomtänkt: rått `q`-fält före hydration, namnlös spegel + hidden inputs efter (dubbel-filtrering förebyggd).
- Token-disciplin rakt igenom: ny CSS använder enbart befintliga `--jp-*`-par med dark-täckning; chip-pillen ligger inom radius-undantaget.
- q-max-copyn är konkret med nästa steg; sr-only-annonsen för tagg-livscykeln är proaktivt tillagd just för att den visuella feedbacken sitter långt från fältet.
- `selectOnTab`-mitigeringarna (endast öppen lista + aktiv markering, aldrig Shift+Tab, `onMouseLeave`-rensningen från E2h-M4) är bevarade.

### Sammanfattning

0 Blockers, 2 Major (M1 hit-area, M2 rensa-copy), 4 Minor, 3 rendered-flaggor. Inget veto/HALT — men Major blockerar merge per severity-tabellen. Delegera M1–M2 (+ gärna Mi1–Mi3, alla är småfixar i samma filer) till nextjs-ui-engineer; Mi4 + R1–R3 går till rendered-passet med Klas. Re-review behövs inte för M1–M2 om fixarna följer förslagen ordagrant — avvikelse triggar ny granskning.
