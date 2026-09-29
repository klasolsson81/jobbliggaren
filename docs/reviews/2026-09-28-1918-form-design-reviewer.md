# #1918 form round — design-reviewer (report-only, 2026-09-28)

> Transcribed verbatim from the subagent hand-back (the harness's two-space indentation removed). Brief:
> `docs/sessions/2026-09-28-1918-matchning-form-brief.md`. Worktree `.claude/worktrees/oauth-google-1744-4b3a94` at `cb9f00bb`.

## Design-review: Matchning-kortet, en del i taget (issue #1918, formrunda före kod)
**Status:** ⚠ Changes requested. Formen är avgjord under Bindningar. B1–B3 blockerar varje bygge av kortet som inte stänger dem.
**Auktoritet:** DESIGN.md §§1.2, 4, 5, 6, 8, 9, 12 · ADR 0047, 0144 (§8), 0145 D4/D5 · jobbpilot-design-{components,copy,a11y}

Mätt 2026-09-28 mot :3916 och stubben. HEAD är cb9f00bb, trädet är rent och stubben är återställd till `profile=ok&prefs=filled&taxonomy=ok&put=ok`. Stubben delas med andra sessioner, som kan ha sett mina skrivningar medan mätningarna pågick. Mina renderingar och mätvärden ligger i `C:\tmp\jobbliggaren-visual\1918\dr\` (`facts-dr.json`). Källfilerna nedan ligger under `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\oauth-google-1744-4b3a94\web\jobbliggaren-web\src\`.

### Blockers
1. **Enter eller Space på en chips ⨯ tappar fokus till `<body>`** — Fil: `...\src\components\settings\match-preferences-card.tsx:292-322`, `...\src\components\settings\preference-chip.tsx:44-50`
   Nuvarande: `restoreFocus` körs bara när `keyboard` är sant, och bara Delete/Backspace sätter det. Enter och Space är en knapps vanliga aktivering, men de går via `onClick` med `keyboard=false`. Mätt:
   - Enter → BODY, Space → BODY.
   - Delete → "Ta bort Projektledning" (grannen).
   - Sista chipen i en del via Delete → "Lägg till".
   Chromium låter nästa Tab landa rätt, men elementet som hade fokus är borta, så skärmläsaren tappar sin plats.
   Krävs: varje borttagning där ⨯ hade fokus flyttar fokus till nästa chips ⨯, annars föregående, annars delens egen knapp, oavsett tangent. Delens knapp behåller sin DOM-identitet när texten byts. Fixen följer med omskrivningen av `removeChip` i #1918, med ett test per väg: Enter, Space, Delete och sista chipen.
   Motivering: WCAG 2.4.3, och kortets egen regel (card:281-282 och 305-307: "aldrig till body").

2. **Railen visar sparade kompetenser som råa concept-id** — Fil: `...\src\components\onboarding\match-setup-launcher.tsx:80-94`, `...\src\app\(app)\oversikt\page.tsx:213`
   Nuvarande: kortet visar C#, SQL och Projektledning. Tar man bort båda yrkena och går till `/oversikt?matchsetup=1` visar railen "Sk1l_CsH_arp · Sk1l_SqL_001 · Sk1l_SqL_002 · Sk1l_PrJ_Led" och "Valda kompetenser (4)", och Granska visar samma sak (`dr\rail-kompetenser-1280.png`, `dr\rail-granska-1280.png`). Railen tar emot `persistedSkillGroups` (rail-modal:112), men /oversikt skickar bara id:n. Matchning-sidan anropar `resolveSkillLabels`, /oversikt gör det inte.
   Railens spara skickar dessutom ingen `experienceYears`. Efteråt visar kortet "Inget angivet" i stället för "5 år", och Granska säger inget om det. Mätt mot stubben, som lagrar full replace; det är defekt (a), och den graderar dotnet-architect.
   Krävs: /oversikt löser upp grupperna som Matchning-sidan gör och skickar dem genom launchern. Railen ska visa namn och ett chip per tvillingpar. Med U1 ligger railen två klick från kortet, och #1918:s kriterium "förstagångsflödet fortsätter fungera" bedöms mot den här renderingen. senior-cto-advisor avgör om fixen tas i samma PR eller i en uppföljande.
   Motivering: ADR 0047 punkt 1–2 (rå token på en läsyta). Kortets egen seed-kommentar, card:81-88, säger samma sak.

3. **Överflödet vid ≤390 px (#1914): vad det ombyggda kortet inte får göra** — Fil: `...\src\app\globals.css:3504-3527, 3584-3591`
   Nuvarande: sidan scrollar i sidled +170/+115/+100 px vid 320/375/390. Det långa yrkets ⨯ ligger på x 453–485 i en 320 px bred viewport.
   Krävs, oavsett om senior-cto-advisor väljer A eller B:
   - ingen horisontell sidscroll vid 320–390 px;
   - varje ⨯ ligger inom kortet och syns;
   - en etikett som inte ryms radbryts inne i chipen. Ingen ellipsis i kortet: resten av texten finns då bara i `title`, som varken tangentbord eller touch når, och det är användarens egna val;
   - knappen i rubrikraden bryts ned under h3:an i stället för att spilla över.
   Alternativ A (`min-width: 0`) ger ellipsis med dagens `.jp-chip__label` och behöver därför den här radbrytningen i kortet.
   Motivering: WCAG 1.4.10 och ADR 0047 punkt 2.

### Major
4. **En "Lägg till" öppnar alla fyra väljare, erfarenheten ligger gömd under Kompetenser och delrubrikerna är versaler i sans** — Fil: `...\match-preferences-card.tsx:440-529`, `...\match-preferences-dialog.tsx:286-364`
   Nuvarande: dialogen "Lägg till i matchning" har en kropp på 1125 px i en låda på 636 px vid 1280, och 1360 i 584 vid 390. "YRKEN" och de andra rubrikerna är `<p class="jp-popover__title">` (13 px, versaler, sans).
   Krävs: U1 enligt Bindningar 1–5.
   Motivering: Klas-direktivet 2026-09-28; components-skillen (Dialog är till för "brief focused tasks"); DESIGN.md §4 ("aldrig all caps i sans"); ADR 0145 D5.

5. **Kvitto och fel ligger på kortnivå, långt från delen, och felet är generiskt** — Fil: `...\match-preferences-card.tsx:517-550`, `...\src\lib\actions\match-preferences.ts:64-69`
   Nuvarande: "Ändringen kunde inte sparas. Försök igen." står högerställt under hela kortet vid varje fel (`C:\tmp\jobbliggaren-visual\1918\r0\remove-error-1280.png`). Det gäller även 429, där "Försök igen" är fel råd. Dialogen och railen visar actionens mappade text; kortet gör det inte (defekt c). Reservtexten `matchPrefs.errors.saveFailed` ("Kunde inte spara dina matchningsönskemål.") saknar åtgärd.
   Krävs:
   - Ett `Outcome` per del, i Notisers form (#1391), under delens innehåll. Det visar antingen status "Sparat 13:34" (`settings.savedAt`) eller en alert med den mappade texten.
   - Ett fel vid spara i dialogen visas i dialogfoten; dialogen förblir öppen och utkastet ligger kvar.
   - `saveFailed`: sv "Ändringen kunde inte sparas. Försök igen om en stund." · en "The change could not be saved. Try again in a moment."
   - Raden på kortnivå och `matchPrefs.saveError` tas bort.
   Motivering: #1391; copy-skillen §3 (orsak och åtgärd); ADR 0047 punkt 2.

6. **En misslyckad borttagning kan visa ett tillstånd som servern inte har (defekt d)** — Fil: `...\match-preferences-card.tsx:298, 332, 353`
   Nuvarande: jag har läst det i koden men inte renderat det, eftersom kapplöpningen inte går att framkalla mot stubben. Återställningen går tillbaka till ögonblicksbilden från klicket, och nästa borttagnings payload låses vid klicket. Misslyckas borttagning 1 och lyckas 2 syns båda chipsen fast servern har ingen av dem. Misslyckas båda saknas A i gränssnittet men finns kvar på servern.
   Krävs: efter varje utfall visar delen exakt det servern har. Mekanismen väljer dotnet-architect eller nextjs-ui-engineer. Enhetstest för båda paren inom en del.
   Motivering: ADR 0047 punkt 2.

### Minor
7. **Ett ensamt "?" på egen rad i Yrken-redigeraren** — Fil: `...\src\components\settings\occupation-section.tsx:575-584` (r0 `dialog-1280.png`)
   Nuvarande: "Om år i yrket" står till vänster under raderna, långt från årsfälten den förklarar. Krävs: ?:et i `.jp-labelhelp`-formen direkt vid etiketten "År i yrket". Motivering: components-skillen, "Help behind a ?".
8. **409 visar "Resursen är i ett otillåtet tillstånd. Ladda om sidan och försök igen."** — Fil: `...\src\lib\actions\_action-error.ts:38-40`
   Nuvarande: nåbart per del när replay enligt ADR 0146 tar slut. Krävs: matchningsactionen mappar 409 själv: sv "En annan ändring sparades samtidigt. Ladda om sidan och försök igen." · en "Another change was saved at the same time. Reload the page and try again." Motivering: copy-skillen §3 (orsaken i användarens ord).
9. **en: `matchPrefs.experience.reviewValue` "{years} years" ger "1 years"** — Fil: `...\messages\en\settings.json` (under `web\jobbliggaren-web\`, inte `src\`)
   Krävs: `{years, plural, one {# year} other {# years}}`, och samma sak för `occupation.reviewYearsValue`. Motivering: copy-skillen (locale).

### Bra gjort
- Borttagning med Delete/Backspace, sista chip → knappen och dialogens fokusretur fungerar redan (mätt). De följer med in i U1.
- Notisers `Outcome` och Kontos h3-grupper finns redan som förlagor, så U1 behöver ingen ny token och ingen ny sorts komponent.
- Tomlägenas meningar säger vad tomt betyder ("Hela landet (ingen ort vald)") och står kvar oförändrade.

### Sammanfattning
3 blockers, 3 major, 3 minor. Bindningarna nedan gäller för bygget. Delegera till nextjs-ui-engineer när dotnet-architect har valt hur skrivningen ska se ut. Re-review efter fix: samma agent, report-only, scopad till fix-deltat (CLAUDE.md §9.6).

### Bindningar
1. **U1.** Kortet består av h2 "Matchning" och fem `jp-settings-group` i ordningen Yrken · Kompetenser · Orter · Anställningsformer · Antal års erfarenhet. Varje del har:
   - en h3 (`.jp-settings-group__title`, som i Konto);
   - chips, eller värdet/tomtexten;
   - ett `Outcome`.
   U2 avvisas: fem formulär i ett kort (ADR 0047 punkt 4). U3 avvisas: ett inline-fält med egen spara-knapp bland läsytor blir en andra interaktionsmodell, och frågan "sparas det av sig självt?" blir en gissning.
2. **Knappen.** En per del, till höger i h3-raden (GOV.UK summary list, samma placering som Notisers kontroller). Vid smal bredd bryts den ned under h3:an.
   - Text: "Ändra" när delen har värden, "Lägg till" när den är tom (en "Change"/"Add").
   - Tillgängligt namn: synligt verb + delens namn, t.ex. "Ändra Yrken" eller "Lägg till Antal års erfarenhet", via `aria-labelledby="{knapp} {h3}"` (WCAG 2.5.3), plus `aria-haspopup="dialog"`.
   - Form: shadcn `Button variant="link"` som `START_OVER_LINK` i `change-email-setting.tsx`: accent-700, understruken i vila, 16 px, 40/44 px träffyta. Inte `.jp-wizard__editlink` (13 px), och aldrig `--primary`.
3. **Dialogen.** En instans som parametriseras per del, i en lazy chunk (#748).
   - Titel = delens namn: Yrken · Kompetenser · Orter · Anställningsformer · Antal års erfarenhet (en Occupations · Skills · Places · Employment types · Years of experience).
   - Kroppen innehåller bara delens redigerare: ingen rubrik under titeln (§8 regel 1) och ingen ledtext (regel 2).
   - Knappar: "Spara yrken / kompetenser / orter / anställningsformer / erfarenhet" (en "Save occupations / skills / places / employment types / experience"), "Sparar…" och "Avbryt".
   - Är delen tom öppnas väljaren direkt, så det behövs inget andra klick på "Lägg till …".
   - Bredd 840 för Yrken och Orter, `min(640px, 100%)` för övriga, som modifier på `.jp-matchdialog`.
   - "Lägg till i matchning" och "Spara matchning" försvinner från inställningssidan.
4. **Erfarenheten** är en egen del med egen dialog. Fältet får sitt namn från dialogtiteln (`aria-labelledby`, ingen dubblerad etikett) och har ingen hint (§8 regel 3). Tomt fält + spara ger "Inget angivet". Åren per yrke stannar i Yrken.
5. **Fokus.**
   - Vid öppning: dialogtiteln (tabIndex −1) för de fyra listdelarna, fältet för erfarenheten. Aldrig "Rensa", som är där fokus hamnar i dag (mätt).
   - Vid stängning (Spara, Avbryt, Esc, ×): knappen på den del som öppnade dialogen.
   - Vid borttagning: enligt B1.
6. **Kvitton och fel:** enligt M5. **Tomlägen:** texterna är oförändrade och "Lägg till" är nästa steg. Den enda "Lägg till" och statusen på kortnivå tas bort; de fem knapparna ersätter dem.
7. **Skrivningen måste göra etiketterna sanna.** "Spara orter" och en borttagning i Orter ska bara skriva orter, alltså W1 eller W2 (dotnet-architect väljer). W0 skickar om flikens kopia av de andra delarna under en knapp som säger "orter".
8. **Den renderade rundan** (DESIGN.md §12, axe 0 violations) ska visa:
   - kortet fyllt vid 320/375/390/768/1280/1920/3440 utan sidscroll i sidled, plus mörkt tema påtvingat vid 390 och 1280;
   - alla delar tomma, och en blandning där bytet mellan "Ändra" och "Lägg till" syns;
   - var och en av de fem dialogerna vid 1280 och 390, med både tom och fylld del;
   - kvitto per del efter spara och efter borttagning, där skrivningen bara bär den delen;
   - `put=error`, `rateLimited` och `conflict`, både i dialogfoten och under delen;
   - fokusmätning: Enter/Space/Delete på ⨯, sista chipen, Spara/Avbryt/Esc och initialfokus;
   - det degraderade läget på en nystartad server (r0-bilden av det läget är bara det ifyllda kortet, eftersom taxonomin cachas);
   - railen nådd från kortet, med namngivna kompetenser och erfarenheten kvar efter railens spara.
