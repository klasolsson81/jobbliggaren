# design-reviewer — PR #1442 (#1349, HEM C + HEM D)

- **Agent:** `design-reviewer` (§9.2, copy i två locales + e-postcopy)
- **Datum:** 2026-08-22 · **Worktree:** `c:/tmp/jbl-orphan` · **Bas:** `6a03fee3`
- **Runda 1:** ⚠ Changes requested — **0 Blocker, 1 Major, 6 Minor**
- **Auktoritet:** `jobbpilot-design-copy` · AGENTS.md §10 · CTO-bindet Tillägg (2), som uttryckligen
  delegerar *"exakt prosa"* hit.

Ingen FAS-DEFERRAL behövs. Mätt: `EmailTemplates.cs` U+2014 = 39, **samtliga i `///`-kommentarer**;
`pages.json` sv/en U+2014 = 0, U+2026 = 29 vardera. Inga utropstecken, inga literala `...`.

## Major

**1. "Om det var du:" är en stump, och den faller på samma dom som `intro` i #1438.**
Botten är dock **inte** strykning här, för stycket måste introducera en knapp. Tre mätpunkter:
(i) den är det enda satsfragmentet av **tre parallella länkstycken** i samma mejl — de andra två är
fullständig sats + kolon; (ii) **i plaintext finns ingen knapp som fyller i satsen** — renderingen blir
`Om det var du:` följt av en rå URL, ingen verbfras säger vad länken gör; (iii) husformen finns redan
skeppad **ordagrant** i samma fil: *"Om det var du, bekräfta att adressen är din…"*
**Krävs:** `Om det var du, logga in här:` — villkorsram + komma + imperativ.
**Bindet hålls:** imperativen **erbjuder**, den förutsäger inte. Det struket var modalverbet
*"kan du logga in"*.
⚠ **Noll tester pinnar någon av PR:ens tre nya meningar** — bara subject. En halv strykning fångas
fortfarande inte av något.

## Minor

**2. `än` är repots enda förekomst i den betydelsen.** Mätt: `ännu` = 24 i `messages/sv/`, adverbiellt
`" än."` = **1**, och det är den nya strängen. Systerraden i samma objekt säger *"inte öppen **ännu**"*.
→ `ännu`. Tre hem. (`en` rörs inte — *"not confirmed yet"* är rätt.)

**3. Subject:et tappar deixis i en inkorgslista.** *"Adressen"* är bestämd form utan antecedent.
→ `Din e-postadress är redan registrerad hos Jobbliggaren`. Husformen är skeppad på systermejlet.
`Din` **påstår inget nytt** — adressen är mottagarens per konstruktion.

**4. `registrera … registrerad` upprepar stammen. Överkorrigering.** Det var **andra** satsen som fällde
egenskapen, inte första. *"Någon har försökt skapa ett konto"* beskriver en **tredje parts handling**,
som duplicate-grenen fastställer direkt — den påstår inte att ett konto finns. Samma drag som #1438
gjorde och bindet berömde.

**5. Plaintext smälter ihop försäkran och kontaktväg; HTML separerar dem.** HTML renderar två element,
texten ett. Samma mejl har alltså olika styckestruktur i sina två renderingar — i en fil vars kommentar
säger att delarna hålls lika för hand. → blankrad.

**6. `Kontrollera din inkorg.` utelämnar skräpposten.** Ordagrant återbruk av `resendConfirmation.sent`,
inte en tredje variant. HEM D är enligt bindet den **dominerande** ytan, och skräppostfiltret är den
vanligaste orsaken till "jag fick aldrig mejlet". **Avvägning namngiven:** matchar i gengäld inte
`pendingTitle` ordagrant; jag väger fullständig instruktion tyngre.

## Bra gjort
- **`Ingenting har ändrats` håller försäkran och är husform** — `AuthErrorCodes` bär den ordagrant i
  samma lugnande roll. Den är dessutom **obegränsad** där *"Ditt konto är oförändrat"* var begränsad
  till kontot: en verklig ägare får ett **bredare** svar, inte ett smalare. Krympt om ämnet,
  oförsvagad som försäkran.
- **HEM D:s tvåstegsform är rätt vid en misslyckad inloggning**, och `en` är idiomatisk. Att strykningen
  tar bort kopplingen till försöket är inget tapp: `<p role="alert">` sitter i formulärets felposition.
- **Den självvalda villkorssatsen fungerar för båda populationerna.** Vagheten (*"stämmer något inte"*)
  är avsiktlig — populationen kan inte namnges utan att exponera svepet (Beslut 4.3). R1 är delvis
  stängd, precis som PR-bodyn skriver, och inte mer.

## Observation, routad ingenstans
`AuthErrorCodes.DuplicateAccountMessage` bär det strukna predikatet ordagrant, men är **mätt onåbar**
för en verklig användare: den kräver `RegistrationsOpen=true && RequireEmailConfirmation=false`, en
kombination `AuthOptionsValidator` vägrar boota på utanför Development/Test. **Vidga inte PR:en.**

## Eskaleringar
Inga.
