---
name: jobbpilot-design-copy
description: >
  Canonical reference for JobbPilot's Swedish copy tone, microcopy patterns,
  and locale formatting. Use whenever user-facing text is written, error
  messages are composed, empty states are designed, or when translating/
  localizing UI strings. Triggers on: copy, text, svenska, swedish, microcopy,
  message, error, empty state, tooltip, placeholder, label, button text,
  notification, toast, confirm, locale, datum, tid, valuta, i18n,
  messages/sv/.
---

# JobbPilot Design Copy

> Canonical Swedish copy patterns for civic-utility tone.
> - Design philosophy behind tone choices → `jobbpilot-design-principles`
> - Component-level label and button text context → `jobbpilot-design-components`
> - Accessibility copy (aria-labels, screen reader text) → `jobbpilot-design-a11y`

---

## Core tone

JobbPilot är ett verktyg för stressade jobbsökande. Språket signalerar tillit
och kompetens — inte peppning, inte förminskning.

**Du-tilltal.** Alltid "du", aldrig "Du" eller "ni". Platsbanken och Digg
använder samma form.

**Direkt.** 10 ord där möjligt. Ingen "resan mot ditt drömjobb". Ingen
"tillsammans skapar vi framtiden".

**Konkret.** Siffror, datum, namn. "Intervjun är 14 apr kl 10:00" slår
"Du har en kommande intervju".

**Opretentiös.** Ingen spänning som inte finns. Tomt tillstånd är tomt —
inte "en möjlighet väntar".

---

## Forbidden patterns

Never use:

| Kategori | Exempel att undvika |
|---|---|
| Emojis i copy | ✨ 🚀 🎉 ⚡ 😊 (alla emojis) |
| Em-dash (—) i copy | "Du behåller kontrollen — inget ändras" (—, U+2014 = AI-kliché) |
| Utropstecken i info/success | "Sparat!" "Klart!" "Perfekt!" |
| Informella utrop | "Hoppsan!", "Oj!", "Aj då!", "Kör hårt!" |
| Engelska i svensk copy | "Let's go", "Let's do this", "Good job!" |
| Peppning | "Vi håller tummarna", "Lycka till på resan" |
| Versal Du | "Du kan hitta jobb här" (archaiserande) |
| Vag feedback | "Något gick fel", "Försök igen", "Okänt fel" |
| Stats-kort-rubriker | "Du har 3 aktiva ansökningar" som inramad kort-rubrik |

Utropstecken är acceptabelt i error-meddelanden när de förstärker brådska,
men sparsamt och aldrig i success/info-copy.

**Inga em-dash (—) i UI-copy.** Em-dash (`—`, U+2014) är en stark AI-kliché
(Klas hård regel 2026-06-20) och får aldrig stå i användarvänd svensk copy:
JSX-text, renderade strängar, hjälptexter, knappar, `title`/`alt`/`aria-label`.
Ersätt efter relation: punkt (två fullständiga satser), kolon (etikett: värde),
komma (apposition) eller parentes (inskjutning). En-dash (`–`, U+2013) i
intervall/mått är legitim ("2021–2024", "10–12 pt") och rörs inte; för "saknat
värde" i tabeller/summeringar används en-dash, inte em-dash. Em-dash är OK i
kod-kommentarer, ADR/doc-prosa och test-`describe()`-labels (ej user-facing).
En ESLint-regel (`no-restricted-syntax`, U+2014 i `JSXText`/`Literal`/
`TemplateElement`, exkl. tester) spärrar återinförande i `web/jobbliggaren-web`.

**Inga literala trepunkter (`...`) i copy.** Trepunkt skrivs alltid med
ellipsis-tecknet `…` (U+2026, `…`), aldrig tre separata punkter `...`
(typografiskt fel + radbryts felaktigt). Gäller laddnings-/vänte-copy och all
annan användarvänd text ("Loggar in…", "Sparar…", "Skapar konto…"). En ESLint-
regel (`no-restricted-syntax`, literala `...` i `JSXText`/`Literal`/
`TemplateElement`, exkl. tester) spärrar återinförande i `web/jobbliggaren-web`;
den träffar bara copy-strängar, aldrig spread/rest (`...props`, `[...arr]`) som
inte är sträng-/text-noder. Se §4 (Loading).

**Inga stats-kort-rubriker.** Skriv inte en mening som inramad rubrik ovanför
en lista bara för att räkna något ("Du har X aktiva ansökningar"). Visa siffran
direkt i raden eller tabellhuvudet ovanför listan, i mono — t.ex.
`3 391 träffar · uppdaterad 14:32`. Detta är en copy-konsekvens av
civic-utility-regeln "information är design" (se `jobbpilot-design-principles`).

---

## Swedish locale conventions

| Kategori | Korrekt | Fel |
|---|---|---|
| Datum kort | 14 apr 2026 | 14/4/26, 4/14/2026 |
| Datum kort utan år | 13 apr | 13/4, Apr 13 |
| Datum lång månad | 18 april | 18:e april, April 18 |
| Månadsetikett | maj 2026 | Maj 2026, 2026-05 |
| Datum ISO | 2026-04-14 | 14-04-2026 |
| Tid | 14:32 | 2:32 PM, 14.32 |
| Valuta | 33 456 kr | 33,456 SEK, 33456 kr |
| Decimaler | 4,5 km | 4.5 km |
| Tusental | 12 345 | 12,345 eller 12.345 |
| Relativ tid | 3 dagar sedan | 3 dagar sen, for 3 days, 3 days ago |
| Företagsnamn | Volvo Cars Sverige AB | Volvo AB (förkortat utan grund) |

Implementation:
- Datum/tid/tal: `@/lib/i18n/format` — next-intl är formaterings-auktoriteten och
  löser zon och locale deterministiskt över SSR och klient. **Formateraren är
  första argumentet** (`useFormatter()`, eller `await getFormatter()` i en async
  Server Component).
- Relativa tider: `@/lib/i18n/relative-time` — ordvalet resolvas via `messages/sv/`,
  inte i hjälparen.
- Valuta: ingen hjälpare, för ingen produktyta formaterar valuta i dag.

`date-fns` är INTE installerat. Skriv inte zon-literalen — `no-restricted-syntax`
fäller den (#1148).

Konventioner och var formaterarna bor → `references/locale-formatting.md`

---

## Microcopy patterns

### 1. Empty states

Struktur: konstatering + konkret nästa steg. Aldrig bara konstatering.

| Situation | ✅ Ja | ❌ Nej |
|---|---|---|
| Inga ansökningar | "Du har inga aktiva ansökningar. Hitta jobb som passar din profil under Jobb." | "Inget här ännu." |
| Inga jobb matchar filter | "Inga jobbannonser matchar dina filter. Prova att bredda sökningen eller rensa filter." | "Oj, vi hittade inget!" |
| Inget CV uppladdat | "Ladda upp ett CV för att komma igång. Vi stödjer PDF och Word." | "Ladda upp ditt CV! 📄" |
| Inga sparade sökningar | "Du har inga sparade sökningar. Skapa en för att få nya jobb mejlade till dig." | "Tomt här." |

### 2. Success-feedback

Konkret, fakta. Ingen peppning. Tid och datum om relevant.

| Situation | ✅ Ja | ❌ Nej |
|---|---|---|
| Ansökan skickad | "Ansökan skickad 14:32 den 18 apr." | "Kör hårt! Vi håller tummarna! 💪" |
| CV sparat | "CV sparat som 'Klas-CV-v3'." | "Perfekt! Ditt CV är nu sparat ✅" |
| Profil uppdaterad | "Profil uppdaterad." | "Klart! Ser bra ut!" |
| After registration | "Välkommen. Nästa steg: ladda upp ditt CV." | "Yay! Välkommen ombord! 🎉 Nu börjar resan!" |

### 3. Error-meddelanden

Vad gick fel + vad ska göras. Aldrig vag.

| Situation | ✅ Ja | ❌ Nej |
|---|---|---|
| Inloggning misslyckas | "Inloggningen misslyckades. Kontrollera e-post och lösenord." | "Hoppsan! Det blev fel." |
| Nätverksfel | "Ingen anslutning. Kontrollera din nätverksanslutning." | "Något gick fel. Försök igen." |
| Serverfel | "Ett fel uppstod. Försök igen om en stund eller kontakta support om problemet kvarstår." | "Error 500" |
| Valideringsfel format | "E-postadressen har fel format." | "Ogiltigt värde" |
| Valideringsfel krav | "Lösenordet måste vara minst 12 tecken." | "Lösenordet uppfyller inte kraven." |

Aldrig:
- Visa stacktrace för användare
- Exponera interna felkoder utan översättning
- "Unknown error" — ange alltid request-ID om felet är okänt

Alla felkoder → `references/error-messages.md`

### 4. Loading

Kortfattad. Trepunkt (…) — Unicode `\u2026`, inte tre separata punkter `...`

| Situation | ✅ Ja | ❌ Nej |
|---|---|---|
| Hämtar listor | "Hämtar jobbannonser…" | "Letar efter ditt drömjobb ✨" |
| Sparar | "Sparar…" | "Sparar dina fantastiska ändringar!" |
| Renderar | "Förhandsvisningen renderas…" | "Magin händer! 🪄" |
| Laddar upp | "Laddar upp CV…" | "Bearbetar…" (vad bearbetar?) |

Skeppade former att återanvända: "CV:t läses in…", "Jobbannonsen läses in…",
"Ansökningarna läses in…", "Hämtar förslag…".

### 5. Matchning och omdömen

Produkten innehåller **ingen AI/LLM** (ADR 0071 — ADR 0051 superseded). CV- och
matchningsmotorerna är deterministiska. Det finns alltså ingen AI-copy att skriva:
inga samtyckesrutor för modellbearbetning, ingen BYOK-nyckel, ingen leverantör att
namnge. Skriv aldrig copy som tillskriver produkten ett omdöme den inte fäller.

**Aldrig ett tal.** Matchning visas som **kategori först** — ett omdöme plus vad som
matchar och vad som saknas per dimension. Aldrig "92 % matchning", aldrig en mätare,
aldrig en procent-ring (ADR 0053 Beslut 5 + Amendment 2026-06-19; ADR 0076 Decision 4,
Goodhart-vakten; CLAUDE.md §5 — "a match score as an opaque number" är en namngiven
anti-pattern, arkitekturtestad).

| Situation | ✅ Ja | ❌ Nej |
|---|---|---|
| Matchningsgrad | "Stark match" | "89 % matchning mot din profil." |
| Varför graden | "Du uppfyller alla ska-krav i annonsen." | "Vår analys ger dig 4 av 5 stjärnor." |
| Annons utan ska-krav | "Annonsen anger inga särskilda ska-krav." | "Du uppfyller alla ska-krav" (falskt: inget krävdes) |
| Dimension utan underlag | "Ej bedömt" | En gissad grad, eller en dold nolla |
| CV-omdöme, citerbart | "Delvis" + citat ur CV:t + åtgärd: *"Driven och engagerad person som gillar utmaningar."* / "Profiltexten är vag. Lägg till vad du faktiskt gör och vad du har åstadkommit." | "Ditt CV känns lite tunt." |
| CV-omdöme, frånvaro | "Underkänt" + observation: "Ingen e-postadress hittades i CV:t." | Ett omdöme som påstår ett citat men citerar inget |

**Allt nedan är skeppat — återanvänd, skriv inte nytt.**

- **Matchningsgrad** (`jobads.ui.match.grade`): **Toppmatch · Stark match · Bra
  match · Grundmatch**, plus **Relaterat yrke** (en märkning, inte en av de fyra
  gröna graderna).
- **Per dimension** (`jobads.ui.match.verdict`): **Matchar · Delvis · Saknas ·
  Ej bedömt · Inga angivna**. Femmedlemsmängden är arkitekturpinnad
  (`MatchDimensionVerdict_is_the_locked_five_member_set`) — utelämna ingen.
  "Uppfyllt"/"Ej uppfyllt" är **något annat**: `requirements`-radens etiketter per
  enskilt krav, inte dimensionens omdöme.
- **Ska-krav-raden** (`jobads.ui.match.mustHaveSummary`): fyra grenar, en per
  utfall, inklusive den vakuösa. ADR 0076 Amendment 2026-06-20 §2(b) förbjuder
  uttryckligen den affirmativa raden när annonsen inte angav några krav.
- **CV-granskning** (`resumes.enums`): omdöme **Godkänt · Delvis · Underkänt · Ej
  bedömt**; nivå per kategori **Ej redo · Behöver omarbetning · Konkurrenskraftigt ·
  Toppskikt**.

Ett CV-fynd har **tre skeppade former** — auktoriteten är `citedEvidenceDtoSchema`
(`src/lib/dto/parsed-resume.ts`) och `cv-criterion-verdict.tsx`; `content-cv-granskning.json`
visar dem:

1. `kind: "TextSpan"` — `quote` ur CV:t, renderad som blockquote. `note` med åtgärden
   är **valfri**: ett "Godkänt" med enbart citat är en skeppad form.
2. `kind: "Structural"` — `observation` när det som saknas inte går att citera
   ("Ingen e-postadress hittades i CV:t").
3. `verdict: "NotAssessed"` — `notAssessedReason` på **kriterienivå**, varken `note`
   eller `observation`. Det är formen som gör "Ej bedömt" ärligt i stället för gissat
   (ADR 0071 OQ3, CLAUDE.md §5 "not assessed v1").

Slå inte ihop formerna till en sträng, och påstå aldrig ett citat du inte har.

**Stavning: `ska-krav`.** Sex förekomster i `messages/sv/`: fyra gemena i meningar
(`jobads.json` mustHaveSummary) och två versala som rubrik-etiketter
(`content-matchning.json:43`, `jobads.json:216`). "skallkrav" finns i noll
skeppade strängar. ADR 0076:s prosa skriver "skallkrav" — följ inte den
stavningen i UI.

Två ytor säger regeln till användaren med produktens egna ord, och copy får inte
motsäga dem: *"Du får ingen svart låda som säger att du är en ”92-procentig
matchning”"* (`content-matchning.json`) och *"Du får inget poäng mellan 0 och 100,
ingen mätare och ingen ring"* (`content-cv-granskning.json`).

Aldrig:
- Ett procenttal, en mätare, en ring eller något annat opakt aggregat
- "Ej bedömt" maskerat som en låg grad — frånvaro rapporteras som frånvaro
- Ett CV-omdöme utan citerat textställe (CLAUDE.md §5)
- Formuleringar som antyder att något resonerar åt användaren

### 6. Destruktiva bekräftelser

Specifik knapp-text. Konkret konsekvens.

| Situation | ✅ Ja | ❌ Nej |
|---|---|---|
| Radera CV-knapp | "Radera CV" | "Bekräfta" eller "OK" |
| Dialog-text | "Radera Klas-CV-v3? Detta kan inte ångras efter 30 dagar." | "Är du säker?" |
| Frånkoppla Gmail | "Koppla bort Gmail? JobbPilot kommer inte längre läsa inkorgen." | "Vill du verkligen?" |
| Avsluta konto | "Avsluta konto? All data raderas permanent inom 30 dagar." | "Är du säker på att du vill fortsätta?" |

(Gmail-raden är ett **mönsterexempel, inte skeppad copy** — Gmail-synk är uppskjuten,
inte borttagen: BUILD.md §6.2 listar fem endpoints, §9.2/§9.3:s gemensamma not säger
"skjuts upp … specarna ovan **bevaras som framtida referens**", och §16:s
"Ej byggt"-tabell listar **`SyncGmailJob`** som "Ej byggt (Fas 5, #321)".
Radnummer står medvetet inte här, och skälet är mätt: de två som stod här hade drivit
PÅ OLIKA SÄTT. `:1489` var sann när den skrevs (`8d08f631`) och hade redan drivit fyra
rader vid #1173:s bas — av SYSKONTRAFIK i en fil **#1109 aldrig öppnade** (den rörde
sex filer, ingen av dem BUILD.md; flyttaren var `64fa2e58`/#1154). `(:1006-1013)`
spände inte ens över sitt eget citat, som låg på 1014. Sedan sköt #1173 båda ytterligare.
Lärdomen är alltså inte "dina egna inskott fäller pekare" utan det bredare: ett radnummer
ruttnar av allt som landar ovanför det. Citaten har en träff var.)

### 7. Påminnelser

Konkret anledning + fråga eller handling.

| Situation | ✅ Ja |
|---|---|
| Follow-up missad | "Du har inte följt upp med Ericsson sedan 5 apr. Skicka ett mejl?" |
| Intervju imorgon | "Intervjun med Klarna är i morgon kl 10:00." |
| Ansökan på utgång | "Ansökan till Volvo stänger om 2 dagar (20 apr)." |
| Ghostad ansökan | "Ingen svar från Spotify sedan 18 mar (28 dagar). Markera som Ghostad?" |

---

## Button text patterns

Verb + objekt där möjligt — kontexten ska vara omöjlig att missförstå.

| ✅ Specifik | ❌ Generisk |
|---|---|
| "Spara CV" | "Spara" |
| "Skicka ansökan" | "Skicka" |
| "Koppla bort Gmail" | "Koppla bort" |
| "Radera konto" | "Ta bort" |
| "Ladda upp CV" | "Ladda upp" |

Acceptabla generiska (när kontexten är otvetydig):
- "Avbryt" (i dialog)
- "Stäng" (modal)
- "Tillbaka" (breadcrumb eller nav)

---

## Placeholder text — rena fält (Platsbanken-regel)

**Klas hård designregel 2026-05-17 (förstärker ADR 0038):** Inga input-fält
har exempel-/instruktions-text i `placeholder`. Fälten är rena à la
Platsbanken. Exempel/format flyttas till **hjälptext (hint) under fältet** i
`text-text-secondary`, kopplad via `aria-describedby`. Label kvarstår ovanför.

```tsx
// ✅ Korrekt — rent fält, exempel som hint under
<label htmlFor="email">E-post</label>
<Input id="email" aria-describedby="email-hint" />
<p id="email-hint" className="text-body-sm text-text-secondary">
  Formatet är namn@domän.se
</p>

// ❌ Fel — exempeltext i rutan
<Input placeholder="du@exempel.se" />
// ❌ Fel — instruktion i rutan, ingen label
<Input placeholder="Ange din e-post" />
```

Full regel + a11y + undantag (shadcn `SelectValue`, chrome-sök `aria-label`):
se `jobbpilot-design-components` → Input/Textarea/Select → "Rena input-fält".

---

## When this skill is not enough

- All backend error codes with Swedish translations → `references/error-messages.md`
- Extended microcopy (tooltips, onboarding, settings) → `references/microcopy-library.md`
- Locale conventions, and where the formatters live → `references/locale-formatting.md`
- Accessibility copy (aria-labels, screen reader) → `jobbpilot-design-a11y`
- Full design philosophy → `jobbpilot-design-principles`
- Component label context → `jobbpilot-design-components`
