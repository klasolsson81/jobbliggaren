# Design-review (STOPP 2, rådgivande plan-granskning) — /ansokningar-redesign FAS 3

**Status:** ⚠ Plan-granskning — rådgivande (ingen kod/render finns; VETO ej applicerbar än)
**Granskat:** 2026-05-17
**Auktoritet:** DESIGN.md + `.claude/skills/jobbpilot-design-*` + ADR 0047 (Area 5-mandat)
**Granskat artefakt:** `docs/design/ansokningar-redesign-plan.md` (hela), mot
ADR 0047, alla fem design-skills, och befintlig kod (`application-card.tsx`,
`status-card.tsx`, `[id]/page.tsx`, `page.tsx`, `status.ts`, `status-pill.tsx`).

Fynd nedan är klassade som de **skulle** klassas om byggt enligt planen som
skriven. "Plan-Block" = skulle bli Blocker vid render. "Plan-Major" = skulle
bli Major. "Spec-lucka" = planen är tyst där den måste vara explicit innan
STOPP 3, annars uppstår defekt vid implementation.

---

## Sammanfattande dom

Planen är **strukturellt sund och adresserar Klas 5 ursprungsfynd + de tre
nya** (UUID-identitet, visuell hierarki, status-mönster). Komponentträden §2–§3
är civic-utility-konsekventa i grunden och bygger ovanpå v1:s vinster i stället
för att riva dem. **Inga Plan-Block** mot själva designriktningen.

Däremot finns **fem spec-luckor** som måste stängas i planen (eller medvetet
delegeras till STOPP 3 med design-reviewer-re-review) innan Klas-GO, annars
återupprepas exakt den klass av defekt ADR 0047 formaliserades för. Den
allvarligaste är att split-layoutens läs/redigera-gräns (§3) och radio-groupens
destruktiv-bekräftelse (§5) är **underspecificerade på just de punkter där v1
fällde** (defekt 2 + defekt 4).

Rekommendation: **plan godkänns för STOPP 3 villkorat** att spec-luckorna L1–L5
stängs — antingen i planrevision före Klas-GO, eller som bindande STOPP 3-krav
med obligatorisk design-reviewer-render-VETO (light+dark + interaktionssökväg)
innan FAS 3-stängning.

---

## Area 5 — ADR 0047 flödesbegriplighet (KÄRNAN). Checklista a–e

Detta är ytan som underkändes två gånger. Bedömd mot Boeke (status/action),
GOV.UK (one thing per page, Summary card, Tag), Norman (synlig systemstatus),
Krug (gissa inte), Wroblewski (irreversibla misstag).

### (a) Kan förstagångsanvändare identifiera raden + slutföra status-ändring utan att gissa? — DELVIS LÖST

- **Identitet: löst.** §2 ersätter `application.id.slice(0,8)` (verifierat i
  `application-card.tsx:20` — exakt Klas ursprungsfynd) med `{jobAd.title} —
  {jobAd.company}` som primär rad. Korrekt rotorsaksåtgärd, inte kosmetika.
  Backend-utbyggnaden §1 är den nödvändiga förutsättningen och är rätt
  avgränsad (projektion, ingen migration).
- **Status-ändring: spec-lucka L1.** §5 säger "pil-navigering (Radix), fokus-ring"
  men specar **inte** hur användaren förstår *att en radioknapp + Spara är hur
  man byter status*. v1:s `StatusCard` hade en explicit instruktionsmening
  ("Välj ny status. Nuvarande status är X.", `status-card.tsx:137-143`).
  Planen säger inte att den bevaras. Utan en synlig ledtext ovanför
  radiogruppen måste förstagångsanvändaren gissa relationen radio→Spara
  (Krug-brott). **Måste specas:** instruktionsrad + att StatusRadioGroup har
  synlig, programmatiskt kopplad gruppetikett (`aria-labelledby` mot en
  *synlig* rubrik, inte bara sr-only).

### (b) Systemstatus synlig/förankrad (StatusEditCard persistent)? — LÖST

- §3 + §5: "Nuvarande status: StatusPill (förankrad, alltid synlig)" och
  "Persistent synlig — ingen disclosure" rättar v1:s defekt 1 (status bakom
  `aria-expanded`-toggle, `status-card.tsx:100-109`). Detta är korrekt
  Norman/Boeke-tillämpning: nuvarande state visas alltid, aldrig nästa-state
  som om det vore nuvarande.
- **Mönster-not (ej Block):** §5 säger nuvarande status renderas både som
  förankrad `StatusPill` *och* som "markerad, disabled/locked" radio-alternativ.
  `jobbpilot-design-components` säger "Never both [dot and pill] for the same
  datum". Här är det pill + radio (inte pill + dot), men risken är densamma:
  status visas två gånger med olika visuell vikt → vilket är "sanningen"?
  **Rekommendation:** nuvarande status visas **en gång** som förankrad pill;
  radiogruppen innehåller **endast tillåtna övergångar** (de 0–3 i
  `ALLOWED_TRANSITIONS`, verifierat i `status.ts:31-42`). En låst self-radio
  tillför ingen uppgiftsinformation (regel 3 "inga fyllnadselement") och
  introducerar ett radio-item som inte kan väljas — förvirrande
  affordans. Detta är ett **Plan-Major-mönsterval** om byggt som skrivet.

### (c) Irreversibel/destruktiv status tydligt märkt FÖRE handling? — SPEC-LUCKA L2 (allvarligast)

- §3/§5 säger "destruktiv övergång (Rejected/Withdrawn) vald → konsekvenstext
  inline" och "bekräftelse före `transitionStatusAction` (behåll v1:s
  dialog-mönster **eller** inline-bekräftelse — design-reviewer väger)".
- Detta är **exakt ADR 0047 defekt 2** (irreversibelt utfall utan
  konsekvens-kommunikation före handling) och planen lämnar det **oavgjort**.
  Det får inte vara öppet vid Klas-GO. **Designdom (väger nu, per §5):**
  - v1:s Dialog-mönster (`status-card.tsx:169-214`) är korrekt
    Wroblewski-tillämpning — explicit `DialogTitle` "Markera som Nekad?",
    konsekvenstext, åtgärdsspecifik knapp "Markera som Nekad" (ej "Bekräfta").
    `jobbpilot-design-components` Dialog-sektion kräver dialog för destruktiva
    handlingar: *"Destructive actions require a confirmation dialog before
    executing"*. Det är inte valfritt mot enbart inline-text.
  - **Behåll Dialog-bekräftelsen för Rejected/Withdrawn.** Inline konsekvenstext
    *när alternativet väljs* är ett bra additivt förvarningssteg (Norman:
    forcing function före commit), men ersätter **inte** dialogen. Mönster:
    radio väljs → inline konsekvenstext syns → Spara → Dialog bekräftar →
    utför. "Inline-bekräftelse istället för dialog" för irreversibel handling
    vore ett **Plan-Block** mot components-skillen.

### (d) Blandas separata uppgifter visuellt? (split-layout + full-width listor) — SPEC-LUCKA L3

- §3 split-layout: vänster JobInfoPanel (läs) / höger StatusEditCard (redigera),
  sedan full-width Uppföljningar + Noteringar. Mental modell "läs vänster /
  agera höger" är i sig GOV.UK-Summary-card-förenlig **om gränsen är visuellt
  otvetydig**.
- **Lucka:** planen specar **inte** den visuella avgränsningen mellan vänster
  och höger panel, eller mellan split-blocket och full-width-listorna. Detta
  är **ADR 0047 defekt 4 + 5** (två formulär sammanflätade; ingen sektionering
  — "allt rakt upp och ner"). v1-detaljsidan staplade allt i `gap-6`
  (`[id]/page.tsx:81`) utan kolumn-/områdesgräns — det var defekten. Planen
  får inte ärva tystnaden. **Måste specas (civic-utility-konformt):**
  - Varje sektion = `<section>` med synlig `<h2>` rubrik + `border-strong`
    avskiljare (informationsbärande divider, a11y-skill: `border-strong` ≥3:1,
    inte `border`). Detta är redan v1:s sektionskort-mönster
    (`[id]/page.tsx:125-128`) — bevara och förstärk, riv inte.
  - Split: vänster/höger åtskilda med whitespace (`gap-6`/`gap-8`) **eller** en
    vertikal hairline — INTE floating cards med shadow (regel 1 "papper inte
    glas"). Plan säger §3 "split-layout (≥ md: 2 kол)" men inte hur kolumnerna
    visuellt skiljs. Specificera: kolumn-gap + att panelerna är
    `border border-border-default rounded-md` (v1-kortmönstret), aldrig shadow.
- Risk om ospecat: ny variant av defekt 4/5. **Plan-Major.**

### (e) Sektions-/områdesavgränsning + visuell hierarki (H1 jobtitel, kort-hierarki) — LÖST i princip, L4 på typografi

- §3: `<h1>{jobAd.title}</h1>` + brödsmulor + `<p text-secondary>{company}</p>`.
  Korrekt hierarki-rotåtgärd (Klas: "ingen visuell hierarki"). H1 = jobtiteln
  är rätt — det är entitetens identitet, inte "Ansökan".
- **Lucka L4:** planen anger inte typografi-tokens för H1/H2/H3-nivåerna.
  v1-detaljsidan blandar `text-h1`/`text-h3` (gamla) — `page.tsx` använder
  redan `jp-h1`/`jp-h2`/`jp-lede`. **Måste specas:** detaljsidan använder
  samma typografi-token-system som list-sidan (H1 28px/`jp-h1`-ekvivalent, H2
  20px sektionsrubriker, H3 18px panelrubriker — per tokens-skill scale).
  Inkonsekvent H1-token mellan list och detalj vore Plan-Major (bruten
  hierarki-konsekvens, ADR 0037/0038).

**Area 5-dom:** planen löser (a)-identitet, (b), (e)-hierarki i grunden. Den
lämnar (a)-status-ledtext (L1), (c)-irreversibilitet (L2), (d)-avgränsning (L3)
**underspecificerade på exakt de punkter v1 fälldes på**. Inte ett plan-fel i
riktning — en plan-tystnad på de mest defektkänsliga ställena. Måste stängas.

---

## Area 1 — Civic-utility-estetik

- **Konsekvent.** §2 list = rader med hairline-separation (`border-border-default`,
  `hover:bg-surface-tertiary`), ingen card, ingen shadow — korrekt regel 1/2
  (papper, information är design). Ersätter `ApplicationCard`-namnet med
  `ApplicationRow` — semantiskt rätt riktning (det var aldrig ett kort; det är
  en ledger-rad). Bra att v1-kort-metaforen rensas.
- §3 paneler `border border-border-default rounded-md` — inga floating cards
  spec:as. Bra.
- **Mönster-not (rådgivande):** §2 list-rad använder `StatusBadge` i rad 2.
  `jobbpilot-design-components` säger statuskolumn i **listor/tabeller** ska
  vara `.jp-statusDot` (lägst visuell vikt, ingen fyllning) — `.jp-pill` endast
  som accent vid entitet (detaljhuvud). Planen säger "StatusBadge" generiskt.
  **Rekommendation:** list-rad → StatusDot (dot + text, ingen bg);
  detaljhuvudets förankrade nuvarande-status → StatusPill. Använd inte fylld
  pill i den täta listan (regel 2/3, components "First choice in tables").
  Detta är inte Block men ett konkret mönsterval planen bör låsa innan STOPP 3.
- Inga gradienter/glow/glas/emoji/neon i planen. Token-only §8 uttalat. Bra.

## Area 2 — Design-tokens

- §8 token-disciplin explicit; §2 listar rätt semantiska tokens
  (`text-text-secondary`, `border-border-default`, `hover:bg-surface-tertiary`,
  `font-mono`). Inga hex/inline-px. **Tillräcklig på principnivå.**
- **Spec-lucka L4 (se Area 5e):** typografi-token-nivåer per rubrik ej
  angivna. Lägg till explicit mappning i planen: H1/H2/H3 → token-klasser per
  tokens-skill scale. Annars risk att STOPP 3 ärver v1:s `text-h1`/`text-h3`-
  blandning.
- **Not:** §3 "JobInfoPanel `<dl>`" + collapsed personligt brev via
  `aria-expanded` disclosure — disclosure-tokenstil (border, ej shadow) bör
  nämnas men är låg risk.

## Area 3 — A11y

- **StatusRadioGroup §5:** Radix RadioGroup-primitiv = korrekt
  (`role="radiogroup"`, pil-navigering, roving tabindex får Radix rätt).
  `aria-labelledby` nämnt. Fokus-ring ärvs globalt (`*:focus-visible`, a11y-skill
  §3) — bra att planen inte överrider den.
  - **Spec-krav (L1, a11y-sida):** gruppetiketten som `aria-labelledby` pekar
    på måste vara **synlig** text (instruktionsraden från Area 5a), inte
    sr-only — sighted förstagångsanvändare behöver samma ledtext som
    skärmläsaren.
  - **Spec-krav:** terminalläge (0 övergångar) → planen säger "ingen
    radiogrupp, civic-text 'slutläge'". Bra — men texten "Den här ansökan är i
    ett slutläge." bör vara i en `<p text-secondary>`, inte tom region. Verifiera
    att ingen `role="radiogroup"` renderas tom (Radix tom grupp = a11y-brus).
- **Extern annonslänk §3** "[Visa annonsen ↗] rel=noopener": korrekt mot
  components/a11y. Lägg till: `target="_blank"` kräver
  `rel="noopener noreferrer"` och en skärmläsar-indikation att länken öppnas
  externt (t.ex. `aria-label="Visa annonsen hos {källa} (öppnas i ny flik)"`)
  — pilen `↗` får inte vara enda signalen (ikon-only = a11y-brott;
  `aria-hidden` på glyfen + text-alternativ). **Spec-lucka L5.**
- **Mobil §6:** ordning header → StatusEditCard → JobInfoPanel → listor.
  DOM-ordning måste matcha visuell ordning (a11y-skill §2: tab-ordning =
  läsordning). Plan säger CSS-omordning < md — verifiera att det görs via
  DOM-ordning/`order` utan att bryta tab-sekvens. Spec att DOM-ordning är
  mobil-ordningen och desktop använder grid-placering (inte tvärtom) så
  tangentbordsordningen är vettig i båda.
- **Skip-link / landmarks:** §8 nämner "landmarks" generiskt. Verifiera i
  STOPP 3 att detaljsidan har `<main>`, `<nav aria-label="Brödsmulor">`
  (finns i v1, `[id]/page.tsx:82`), och att skip-link finns på sidnivå
  (a11y-skill §6). Ej plan-Block men checklista-punkt för render-VETO.

## Area 4 — Svensk copy

- **Fallback-texter — civic-ton OK med en justering:**
  - `"Ansökan #{kort-id}"` — godtagbar civic-utility. Saklig, konkret,
    `font-mono` för id är rätt (regel 4 mono som signal). Bra.
  - `"Ingen kopplad annons — manuellt skapad ansökan"` — civic-ton korrekt,
    konstaterande utan ursäkt/peppning. **Mindre förbättring:** copy-skill
    empty-state-mönster = konstatering + ev. kontext. Denna är ren
    konstatering vilket är OK här (det är inte ett tomt tillstånd, det är ett
    legitimt dataläge). Behåll. Ingen utropstecken, ingen emoji — följer
    skill. Godkänd.
  - "Sök senast {sv-SE}" / "Uppdaterad {sv-SE}" — verifiera `date-fns` med
    `sv`-locale (copy-skill locale), inte `toLocaleDateString` ad-hoc.
    v1 använder `toLocaleDateString("sv-SE")` (`application-card.tsx:10`) —
    acceptabelt men copy-skill föredrar `date-fns/sv`. Låg prioritet.
- **"slutläge"-text §5:** "Den här ansökan är i ett slutläge." — saklig,
  du-implicit, ingen peppning. Civic-ton OK. Liten not: överväg konkretare
  "Den här ansökan är avslutad och kan inte ändras." (Krug: säg vad det
  betyder, inte intern term "slutläge"). Mindre förbättring, ej Major.
- Statusetiketter: planen återanvänder `getStatusLabel` (`status.ts:3-14`,
  "Utkast/Skickad/Nekad/Återtagen" etc.) — konsekvent med MEMORY-noten om
  badge-text-synk mot backend SmartEnum. Bra att ingen ny etikett-uppsättning
  införs.

---

## Split-layout §3 + mobil §6 — civic-utility-lämplighet

- **Mental modell "läs vänster / agera höger":** lämplig och GOV.UK-förenlig
  *förutsatt* L3 (visuell avgränsning) stängs. Stripe Dashboard / Mercury gör
  exakt detta (läskontext + handlingspanel). Civic-utility-OK.
- **Mobil StatusEditCard först < md:** motiveringen "status-ändring är primär
  handling på mobil" är försvarbar (utility-först, principles
  decision-framework p.3 "does this help a stressed user"). **Men:** på mobil
  ser användaren då handlingspanelen *före* jobbidentiteten/kontexten. §6 säger
  ordning "header → StatusEditCard → JobInfoPanel" — header (H1 jobtitel +
  brödsmulor) kommer först, så identiteten är ändå överst. Det är korrekt:
  identitet (header) → handling (status) → kontext (jobinfo) → historik
  (listor). Godkänd motivering, ingen ändring krävs. Verifiera bara att H1
  + företag verkligen ligger i header-blocket före StatusEditCard i DOM
  (annars (a)-identitet bruten på mobil).

## Fallback §7 (jobAd == null) — designmässigt hanterat

- Hanterat korrekt och utan att raden/sidan ser trasig ut: list-rad får
  mono-id som primär identitet (inte tom rad), detaljsida får H1-fallback +
  civic-not i stället för JobInfoPanel, soft-deleted = identiskt som null.
  Detta är rätt defensiv design (regel 3: ingen trasig/halv-rad). Backend §1.2
  left join + soft-delete→null är den korrekta förutsättningen. **Godkänd.**
- En not: när jobAd==null utelämnas företag-`<p>` och JobInfoPanel helt — men
  StatusEditCard + listor är oförändrade, så sidan kollapsar inte till en tom
  vänsterkolumn? **Spec-fråga till STOPP 3:** vid jobAd==null, blir split-layout
  en ensam högerkolumn (ful tom vänster) eller faller den tillbaka till
  single-column? Planen säger "JobInfoPanel ersätts av civic-not" — om noten
  tar vänsterkolumnens plats är det OK; om layouten blir 1-kolumn vid null är
  det också OK. Specificera vilket — annars risk för obalanserad tom canvas
  (regel 3). Klassas som **spec-lucka, mindre.**

---

## Mönster-fel-check: radio-group vs alternativ för 0–3 övergångar

Frågan ställd explicit. Bedömning mot `ALLOWED_TRANSITIONS` (verifierad i
`status.ts:31-42`): max 3 övergångar (Submitted/Acknowledged/Interviewing/
OfferReceived), 1 (Draft/Ghosted), 0 (terminala).

- **Radio-group är rätt mönster** för 1–3 ömsesidigt uteslutande mål +
  explicit Spara-commit (Klas målbild: "Spara disabled tills ändring").
  GOV.UK använder radios för ≤ ~5 ömsesidigt uteslutande val — stämmer.
  Wroblewski/Krug: synliga alternativ > dold select (rättar v1:s knapp-rad
  som inte hade commit-punkt). **Korrekt val.**
- **Edge: exakt 1 övergång** (Draft→Submitted, Ghosted→Submitted). En
  radio-grupp med ett enda valbart alternativ + Spara är acceptabelt men
  tungt. **Rådgivande:** överväg att 1-övergångsfallet renderas som en enskild
  primär åtgärdsknapp ("Markera som Skickad") i stället för 1-items radiogrupp
  — mindre kognitiv last (Krug), färre klick. Inte Block; mönsterval för CTO/
  Klas. Om enhetlighet (alltid radio) prioriteras är det också försvarbart —
  men dokumentera valet.
- **Edge: 0 övergångar** — korrekt hanterat (ingen grupp, civic-text). Bra.

Ingen mönster-felklassning. Radio-group godkänd för 1–3; 1-fallet flaggat som
medvetet val att låsa.

---

## Spec-luckor som måste stängas före Klas-GO (eller bindas till STOPP 3 + render-VETO)

| ID | Lucka | Skulle bli | Källa |
|----|-------|-----------|-------|
| **L1** | Status-ändrings-ledtext + synlig kopplad gruppetikett ospecad (förstagångsanvändare måste gissa radio→Spara) | Plan-Major | ADR 0047 (a), Krug, a11y §5 |
| **L2** | Destruktiv bekräftelse "dialog ELLER inline" lämnad oavgjord | Plan-Block om inline ersätter dialog | ADR 0047 (c), components Dialog, Wroblewski |
| **L3** | Visuell avgränsning vänster/höger + split/listor ospecad | Plan-Major (ny defekt 4/5) | ADR 0047 (d/e), GOV.UK Summary card, regel 1 |
| **L4** | Typografi-token-nivåer per rubrik (H1/H2/H3) ej angivna; risk att ärva v1-blandning | Plan-Major | tokens-skill scale, ADR 0037/0038 |
| **L5** | Extern länk: ikon-only `↗` utan text-alternativ + `noreferrer` ospecat | Plan-Block (a11y, ikon-only) | a11y §6, components |
| L6 (mindre) | jobAd==null: split→1-kolumn vs tom vänster ospecat | Plan-Minor | regel 3 |

**Designdom på de två öppna valen planen ber mig väga:**
- **L2 (§5 destruktiv):** behåll v1:s **Dialog**-bekräftelse för Rejected/
  Withdrawn (additiv inline-konsekvenstext OK som förvarning, ersätter inte
  dialogen). Inline-istället-för-dialog vore Block.
- **(b) nuvarande-status-dubbelrendering:** visa nuvarande status **en gång**
  som förankrad pill; radiogruppen = endast tillåtna övergångar. Låst self-radio
  bör utgå.

---

## Bra gjort i planen (förstärk dessa)

- Backend-utbyggnaden angriper **rotorsaken** (UUID-rad) på dataväg, inte
  kosmetiskt — left join + nullable DTO + soft-delete→fallback är rätt.
- Bygger ovanpå v1:s vinster (etiketterad `<dl>`, separerade add-flows,
  konsekvens-bekräftelse, sektionskort) — river inte fungerande mönster.
- Persistent StatusEditCard (ej disclosure) rättar defekt 1 korrekt mot
  Norman/Boeke.
- `ApplicationCard`→`ApplicationRow` namnbyte = rätt civic-utility-semantik
  (ledger-rad, inte kort).
- Token-only §8 + fallback §7 defensivt designat — ingen trasig rad.
- Gates §1.4/§10 inkluderar design-reviewer render-VETO + ADR 0047 Area 5
  explicit. Korrekt gate-disciplin.

---

## Slutsats för Klas-GO + STOPP 3

**Rådgivande dom: planen är design-mässigt godkänd i riktning** — inga
Plan-Block mot estetik, token-princip eller flödesriktning. Den löser de
underkända fynden i grunden.

**Villkor för STOPP 3-GO:** spec-luckorna L1–L5 stängs, antingen genom
planrevision före Klas-GO eller som bindande STOPP 3-implementationskrav med
**obligatorisk design-reviewer render-VETO i light + dark + interaktionssökväg**
(ADR 0046/0047) före FAS 3-stängning. L2 och L5 är de som skulle bli Block om
byggt fel — de får inte lämnas till implementörens tolkning. L6 är mindre.

Detta blir underlag för Klas-GO. Vid STOPP 3 granskar jag rendered output
(light+dark) + interaktionssökväg enligt ADR 0047 Area 5-checklistan a–e —
denna plan-granskning ersätter inte den render-VETO:n.
