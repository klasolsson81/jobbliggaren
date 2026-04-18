# JobbPilot — Extended Microcopy Library

Extended copy for surfaces not covered by the main SKILL.md tables.
All copy follows the core tone: du-tilltal, direkt, konkret, opretentiös.

---

## Tooltips

Tooltips förklarar ett UI-element — inte hur systemet fungerar i stort.
Max 2 meningar. Ingen peppning.

| Element | Tooltip-text |
|---|---|
| AI-matchningspoäng | "Beräknad matchning mot din profil och detta jobbs kravprofil." |
| BYOK-toggle | "Använd din egen Anthropic API-nyckel. Du faktureras direkt av Anthropic." |
| Ghosted-status | "Markerar ansökan som obesvarad sedan mer än 30 dagar." |
| Ansökan stänger (datum) | "Sista dag att ansöka enligt jobbannonsen." |
| CV-fingerprint | "Hashade identifieraren för att verifiera att rätt CV används. Inte läsbar." |
| Påminnelse-toggle | "Skickar ett e-postmeddelande om ingen aktivitet registrerats inom den angivna perioden." |

---

## Onboarding

### Välkomst-skärm (efter registrering)

```
Välkommen. Nästa steg: ladda upp ditt CV.
```

### Steg 1 — CV-uppladdning

```
Ladda upp ditt CV (PDF eller Word) för att komma igång.
Vi analyserar det för att matcha dig mot jobbannonser.
```

### Steg 2 — Profil (tom)

```
Komplettera din profil för bättre matchning.
Du kan också hoppa över detta och göra det senare.
```

### Steg 3 — Första jobbsökning

```
Sök efter jobb eller bläddra bland förslag baserade på ditt CV.
```

### Onboarding klar

```
Du är redo. Spara intressanta jobb och följ upp ansökningar härifrån.
```

---

## Inställningar — beskrivningar

### Kontoinställningar

| Inställning | Beskrivning |
|---|---|
| E-post | "Din inloggningsadress. Ändra kräver verifiering av den nya adressen." |
| Lösenord | "Minst 12 tecken. Vi lagrar aldrig lösenord i klartext." |
| Avsluta konto | "Raderar all din data permanent inom 30 dagar. Kan inte ångras." |

### Notifikationer

| Inställning | Beskrivning |
|---|---|
| Påminnelser om uppföljning | "Meddelande om du inte följt upp en ansökan inom X dagar." |
| Deadlines | "Varning när en ansökan stänger om 2 dagar eller mindre." |
| Ghostade ansökningar | "Notis om ingen respons inkommit efter 30 dagar." |
| Veckosummering | "E-post varje måndag med en sammanfattning av dina aktiva ansökningar." |

### AI-inställningar

| Inställning | Beskrivning |
|---|---|
| AI-assistans | "Aktiverar AI-genererade förslag för personliga brev och matchningsanalys." |
| Egen API-nyckel (BYOK) | "Använd din Anthropic-nyckel istället för JobbPilots. Du faktureras direkt." |
| Dataanvändning | "Ditt CV och jobbannonsdata skickas till AI för analys. Data stannar inom EU." |

### Integrationer

| Inställning | Beskrivning |
|---|---|
| Gmail | "Importerar jobbrelaterade e-postmeddelanden automatiskt (läsbehörighet, inte skrivbehörighet)." |
| Google Kalender | "Synkroniserar intervjutider till din kalender." |

---

## Tomma tillstånd — ytterligare

Utöver de i SKILL.md:

| Yta | Titel | Beskrivning + åtgärd |
|---|---|---|
| Notifikationslista | "Inga notifikationer" | "Du är uppdaterad." |
| Aktivitetslogg | "Ingen aktivitet" | "Händelser som statusändringar och skickade mejl visas här." |
| Sökresultat (0 träffar) | "Inga träffar" | "Inga jobbannonser matchar '{query}'. Prova ett annat sökord." |
| Intervjuer (tom) | "Inga bokade intervjuer" | "Schemalagda intervjuer visas här. Lägg till en under ansökan." |
| Inkorgen (Gmail ej kopplad) | "Gmail inte kopplat" | "Koppla Gmail för att importera jobbrelaterade mejl. Koppla Gmail" |

---

## Bekräftelsedialoger — ytterligare

Utöver de i SKILL.md:

| Aktion | Titel | Brödtext | Bekräfta-knapp |
|---|---|---|---|
| Radera ansökan | "Radera ansökan?" | "All data för ansökan till {company} raderas. Detta kan inte ångras." | "Radera ansökan" |
| Koppla bort Google Kalender | "Koppla bort Google Kalender?" | "Intervjutider synkroniseras inte längre automatiskt." | "Koppla bort" |
| Rensa alla notifikationer | "Rensa alla notifikationer?" | "Alla {count} notifikationer markeras som lästa och tas bort." | "Rensa alla" |

---

## Datum- och tidsetiketter

Används i listor och kortvy där fullständigt datum ej ryms.

| Situation | Format | Exempel |
|---|---|---|
| Idag | "idag kl HH:mm" | "idag kl 09:14" |
| Igår | "igår kl HH:mm" | "igår kl 14:32" |
| Denna vecka | "veckodagnamn" | "måndag" |
| Äldre | "D MMM" | "3 apr" |
| Gammalt (>1 år) | "D MMM YYYY" | "3 apr 2025" |

---

## Statusetiketter (Badge-text)

Exakta svenska texter för Application.Status → Badge.

| Status | Label |
|---|---|
| Draft | "Utkast" |
| Submitted | "Skickad" |
| Acknowledged | "Bekräftad" |
| InterviewScheduled | "Intervju bokad" |
| Interviewing | "Intervjufas" |
| OfferReceived | "Erbjudande mottaget" |
| Accepted | "Accepterad" |
| Rejected | "Avvisad" |
| Ghosted | "Ghostad" |
| Withdrawn | "Återtagen" |
