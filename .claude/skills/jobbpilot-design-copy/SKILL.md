---
name: jobbpilot-design-copy
description: >
  Canonical reference for JobbPilot's Swedish copy tone, microcopy patterns,
  and locale formatting. Use whenever user-facing text is written, error
  messages are composed, empty states are designed, or when translating/
  localizing UI strings. Triggers on: copy, text, svenska, swedish, microcopy,
  message, error, empty state, tooltip, placeholder, label, button text,
  notification, toast, confirm, locale, datum, tid, valuta, i18n,
  messages/sv.json.
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
| Utropstecken i info/success | "Sparat!" "Klart!" "Perfekt!" |
| Informella utrop | "Hoppsan!", "Oj!", "Aj då!", "Kör hårt!" |
| Engelska i svensk copy | "Let's go", "Let's do this", "Good job!" |
| Peppning | "Vi håller tummarna", "Lycka till på resan" |
| Versal Du | "Du kan hitta jobb här" (archaiserande) |
| Vag feedback | "Något gick fel", "Försök igen", "Okänt fel" |

Utropstecken är acceptabelt i error-meddelanden när de förstärker brådska —
men sparsamt och aldrig i success/info-copy.

---

## Swedish locale conventions

| Kategori | Korrekt | Fel |
|---|---|---|
| Datum kort | 14 apr 2026 | 14/4/26, 4/14/2026 |
| Datum långt | 14 april 2026 | April 14, 2026 |
| Datum ISO | 2026-04-14 | 14-04-2026 |
| Tid | 14:32 | 2:32 PM, 14.32 |
| Valuta | 33 456 kr | 33,456 SEK, 33456 kr |
| Decimaler | 4,5 stjärnor | 4.5 stjärnor |
| Tusental | 12 345 | 12,345 eller 12.345 |
| Relativ tid | 3 dagar sen | for 3 days, 3 days ago |
| Företagsnamn | Volvo Cars Sverige AB | Volvo AB (förkortat utan grund) |

Implementation:
- Datum/tid: `date-fns` med `import { sv } from 'date-fns/locale'`
- Valuta: `new Intl.NumberFormat('sv-SE', { style: 'currency', currency: 'SEK' })`
- Relativa tider: `formatDistanceToNow(date, { locale: sv, addSuffix: true })`

Kod-exempel → `references/locale-formatting.md`

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
| AI-generering | "Genererar utkast…" | "Magin händer! 🪄" |
| Laddar upp | "Laddar upp CV…" | "Bearbetar…" (vad bearbetar?) |

### 5. AI-samtycken

Explicit om vad som skickas vart. Juridiskt krav (GDPR Art. 7).

| Situation | ✅ Ja |
|---|---|
| Genererat utkast | "Utkast genererat av AI. Läs igenom och redigera innan du skickar." |
| Matchningspoäng | "89 % matchning mot din profil." |
| Första AI-användning | "Denna åtgärd skickar ditt CV till AI för bearbetning. Data stannar inom EU och används inte för modellträning. Läs integritetspolicyn." |
| BYOK informerande | "Du använder din egen API-nyckel. Anthropic fakturerar dig direkt." |

Aldrig:
- "Vår AI har analyserat ditt CV och tycker att…"
- "Powered by Claude ✨"
- Persuasivt språk kring AI-kapacitet

### 6. Destruktiva bekräftelser

Specifik knapp-text. Konkret konsekvens.

| Situation | ✅ Ja | ❌ Nej |
|---|---|---|
| Radera CV-knapp | "Radera CV" | "Bekräfta" eller "OK" |
| Dialog-text | "Radera Klas-CV-v3? Detta kan inte ångras efter 30 dagar." | "Är du säker?" |
| Frånkoppla Gmail | "Koppla bort Gmail? JobbPilot kommer inte längre läsa inkorgen." | "Vill du verkligen?" |
| Avsluta konto | "Avsluta konto? All data raderas permanent inom 30 dagar." | "Är du säker på att du vill fortsätta?" |

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

## Placeholder text

Placeholder ersätter inte en label. Alltid: label + placeholder.

```tsx
// ✅ Korrekt
<FormLabel>E-post</FormLabel>
<Input placeholder="du@exempel.se" />

// ❌ Fel
<Input placeholder="Ange din e-post" />  // ingen label
```

Placeholder ska vara ett exempel, inte en instruktion.

---

## When this skill is not enough

- All backend error codes with Swedish translations → `references/error-messages.md`
- Extended microcopy (tooltips, onboarding, settings) → `references/microcopy-library.md`
- date-fns and Intl.NumberFormat code examples → `references/locale-formatting.md`
- Accessibility copy (aria-labels, screen reader) → `jobbpilot-design-a11y`
- Full design philosophy → `jobbpilot-design-principles`
- Component label context → `jobbpilot-design-components`
