# Auth epic #1732 — part 0 pre-review (design-reviewer, 2026-09-17)

> Transcribed verbatim by the driving session from the agent's returned report. Brief the agent
> answered: the session's scratch files `1733-brief-context.md` + `1732-epic.md` (Target flow, D7),
> the security report's Major 8 / Minor 3 / D4 point 2, DESIGN.md and the delivered `(auth)` pages.
> Worktree `C:/tmp/jbl-1733` at HEAD `081e4c67`. The session re-ran the load-bearing measurements
> before transcribing: `RegisterForm.tsx:86-87` (`role="status" aria-live="polite"` panel),
> `ResendConfirmationButton.tsx:76/85/100` (disabled during cooldown, countdown outside the live
> region), `app-shell.tsx:76/140/143/146/156/160/346` (`initials()`, `.jp-avatar`,
> `aria-haspopup="true"`, `jp-usermenu__name`, two `/installningar` links), `confirm-account.tsx:31/93`
> (`useTransition` + `onClick`), `pages.json:600` (`termsLabel` carries both terms and privacy),
> `content-legal.json:330` ("Håll mig inloggad" copy), `HardDeleteAccountsJob.cs:28-33` (restore via
> support, 30 days), `globals.css:1023` (`.jp-avatar`) — all confirmed. The escalation (provider
> mark colours) was put to Klas in plain text the same turn, not transcribed as settled.

## Design-review: auth-sidformen, epik #1732 (del 0)
**Status:** ⛔ Blockerad (3 Blocker, 6 Major, 5 Minor) — allt är **formegenskaper ADR 0142 ska binda**, ingen kod finns.
**Auktoritet:** DESIGN.md §1.2, §4, §5, §6, §7, §8, §9, §12 · skills `jobbpilot-design-{principles,components,copy,a11y}` · AGENTS.md §5 (Frontend), §8 p.4, §10 · ADR 0038/0047/0052/0137
**FAS-DEFERRAL-MANIFEST:** fynd märkta `[6a]` gäller del 6a (OAuth live) och är **deferrals, inte veto nu** — de binds i ADR 0142 men mäts först i 6a:s egen granskning. Allt annat är del 2/3b.
**Mätt mot:** worktree `C:/tmp/jbl-1733` @ `081e4c67`.

---

### Blockers

**B1. Enhetligt verify-svar + en enda felmening ⇒ användaren fastnar i en loop utan väg framåt** — Fil: D3 (ingen kod), precedens `web/jobbliggaren-web/src/components/forms/RegisterForm.tsx:109-127`
Nuvarande: D3 binder *"missing, expired, burned and wrong answer one body/status"*. FE kan då inte skilja fel kod (rätta i fältet) från bränd/utgången (begär ny kod). En användare som bränt sina tre försök och sedan skriver **rätt** kod får en mening som skyller på hens inmatning, och har ingen signal att byta remedium.
Krävs — ADR 0142 väljer **en** av två, uttryckligen:
(a) **Föredragen:** verify skiljer `wrong` / `expired` / `burned` / `missing` **för cookie-innehavaren** (som själv mintade utmaningen; det avslöjar inget om kontoexistens — den grenen förblir dold till efter bevisad inkorg). Då gäller copyn i M4.
(b) Behålls full uniformitet: **en** mening som namnger **båda** orsakerna och **båda** åtgärderna, aldrig bara den ena — *"Koden stämmer inte, eller så har den gått ut. Kontrollera siffrorna, eller begär en ny kod."* — och då får varken försöksräknare eller förvarning före bränning finnas, eftersom de inte går att skriva sant.
Motivering: DESIGN.md §12 + ADR 0047 — "kan uppgiften slutföras utan att gissa". Ett felmeddelande som namnger fel orsak är gissning. Copy-skillen §3: "Vad gick fel + vad ska göras. Aldrig vag."
Delegera till: adr-keeper (del 0) + security-auditor (val av gren)

**B2. Kodstegets vilotext påstår en kod som tre grenar aldrig skickar** — Fil: `1732-epic.md:34` (*"Vi har skickat en kod till {email}"*)
Nuvarande: strängen är **falsk** för (a) `registrationClosed` (mejlet bär *"vi öppnar snart"*, **ingen kod**), (b) `pendingDeletion` (mejlet bär återställningsvägen), (c) budget-/cooldown-slut (uniform 202, **inget mejl alls**). För (a) är sidan dessutom en återvändsgränd: ett fält som aldrig kan fyllas, och vägran finns bara i mejlet.
Krävs: vilotexten flyttar auktoriteten till mejlet och villkorar koden på **användarens egen** kännedom, aldrig på kontoexistens:
> "Vi har skickat ett mejl till {email}. Följ instruktionerna i mejlet. Innehåller det en sexsiffrig kod skriver du in den här. Koden gäller i 15 minuter."
> Hjälptext under fältet: "Har du redan begärt en kod nyss kan det vara den som gäller. Kontrollera skräpposten om du inte ser mejlet inom några minuter."
Och: ADR 0142 skriver att formuleringen **förutsätter** D2:s "konsumenten skickar alltid ett mejl" — faller den premissen (eller byter budgetgrenen beteende) är copyn falsk och ska räknas som en lapse-trigger.
Motivering: DESIGN.md §8 (konkret, aldrig vag), AGENTS.md §10 (informativa, icke-skuldbeläggande fel), ADR 0047 (systemstatus förankrad i **faktiskt** tillstånd).
Delegera till: adr-keeper (del 0), nextjs-ui-engineer (del 2)

**B3. 180-dagarsupplysningen saknas där sessionen faktiskt skapas för befintliga konton** — Fil: säkerhetsrapportens D4-analys punkt 2 (`docs/reviews/2026-09-17-auth-epic-security.md:51`), `messages/sv/content-legal.json:330`
Nuvarande: epikens flöde låter `/logga-in/villkor` bära villkorstexten. Men **villkorssteget renderas aldrig för ett befintligt konto** — deras 180-dagarskaka sätts på `/logga-in/kod`. Läggs upplysningen bara på villkorssteget saknar varje återvändande inloggning den upplysning D4:s egen rättsgrund vilar på ("uppges där handlingen görs"), och den levererade `rememberMe`-kryssrutan som bar valet försvinner samtidigt.
Krävs: **samma två meningar på BÅDA stegen**, direkt ovanför primärknappen (inte i sidfot, inte bakom en länk), `text-body-sm text-text-primary` (aldrig `text-secondary` — ingen grå text):
> "Du förblir inloggad på den här enheten i upp till 180 dagar. Du kan logga ut när du vill, Logga ut finns på varje inloggad sida."
ADR 0142 binder placeringen som en **egenskap hos formen**, inte som copy-detalj, och `content-legal.json:330` skrivs om i samma PR som `setSessionCookie(id, true)`.
Motivering: DESIGN.md §12 + ADR 0047 — konsekvensen av en handling kommuniceras **före** handlingen; en delad dator är den namngivna residualen.
Delegera till: adr-keeper (del 0), nextjs-ui-engineer (del 2)

---

### Major

**M1. Tre inaktiva knappar ovanför det enda fungerande fältet är fel ordning — och "Eller" förutsätter ett första alternativ som inte finns** — Fil: `1732-epic.md:33`
Nuvarande: provider-knappar → divider *"Eller fortsätt med e-post"* → e-postfält. Klas låste **synlighet och inaktivitet**, inte placeringen. Det första en förstagångsbesökare möter blir tre saker som inte går att använda, och dividern påstår ett val som inte existerar.
Krävs: ADR 0142 binder **två ordningar**, växlade på `GET /auth/oauth/providers`:
- **Tom lista (nu):** h1 → e-postfält + hjälptexter → **Fortsätt** (primär) → hairline → rubrik `h2` *"Andra sätt att logga in"* → de tre inaktiva raderna. Ingen "Eller"-divider.
- **Minst en provider live [6a]:** provider-knappar → divider *"Eller fortsätt med e-post"* → fält → Fortsätt.
Motivering: DESIGN.md §1.1 (bygg för att fungera, inte för att sälja), §12/ADR 0047 (uppgiften ska kunna slutföras utan att gissa; status och handling blandas inte).
Delegera till: adr-keeper (del 0), nextjs-ui-engineer (del 2)

**M2. Provider-knapparnas form — en primär, aldrig tre fyllningar, aldrig `opacity` som inaktiv-signal** — Fil: `src/components/ui/button.tsx:10-38`, DESIGN.md §6
Nuvarande: oskrivet vilken knappfamilj och vilken variant som gäller.
Krävs, ordagrant i ADR 0142:
- (auth)-gruppen stannar på **shadcn `Button`** (40px, `lg` 44) som idag — aldrig `.jp-btn` i samma vy (#1095-driften).
- **"Fortsätt" är skärmens enda `variant="default"`** (solid accent-800). Provider-knapparna är `variant="outline"` (vit + `border-border`). Aldrig tre solida fyllningar, **aldrig providerns egen märkesfärg som fyllning** (Google-blå/LinkedIn-blå/GitHub-svart är fyra nya färgytor utanför tokensystemet).
- Inaktiv = `aria-disabled="true"` + **kvar i tabbordningen** + `onClick` no-op. **Inte** `disabled` — då försvinner "Kommer snart" ur a11y-trädet och skärmläsaren får ingen förklaring alls.
- Dimningen görs **aldrig** med `opacity`; texten behåller ≥4,5:1 i **båda** teman (en `opacity-50` på `--jp-ink-1` faller i dark).
- `type="button"`, inte submit (annars skickar Enter e-postformuläret från en "inaktiv" knapp).
Motivering: DESIGN.md §6 ("Max EN `--primary` per skärm"), §9 (a11y-brist = Blocker), dark-mode-stance §3.
Delegera till: nextjs-ui-engineer (del 2)

**M3. Kodfältet: ETT fält, aldrig sex rutor — och bränningen måste förvarnas** — Fil: D3, precedens `src/components/auth/ResendConfirmationButton.tsx:37-47`
Krävs:
- **Ett** `<input>` med synlig `<label>` ovanför ("Sexsiffrig kod"), `autocomplete="one-time-code"`, `inputmode="numeric"`, `maxLength={6}`, `pattern="[0-9]*"`, `aria-describedby` → hjälptext under fältet, `aria-required`, `aria-invalid` vid fel. **Ingen placeholder** (Platsbanken-regeln, components-skillen §Input).
- **Sex separata rutor är förbjudet:** en label kan inte paras med sex fält, klistra-in går sönder, skärmläsaren läser sex namnlösa textrutor och DOM-ordningen blir sex tabbstopp för ett värde. Det vore ett `Blocker`-fynd om det byggdes.
- Om B1(a) väljs: **förvarning före sista försöket** — "Ett försök kvar. Sedan behöver du begära en ny kod." Bränningen kostar användaren 1/10 av dygnets mint-budget och efter del 5 finns ingen annan väg in.
- "Skicka ny kod" återanvänder den levererade nedräkningsformen **exakt**: knappen `disabled` under 60 s, nedräkningstexten **utanför** live-regionen (`ResendConfirmationButton.tsx:94-101`), meddelandet i `role="status" aria-live="polite"`.
Motivering: DESIGN.md §9 (label alltid kopplad), a11y-skillen §1/§2, §6 (loading: byt label, behåll bredd, sätt disabled).
Delegera till: nextjs-ui-engineer (del 2)

**M4. De sju tillstånden — kanal, plats, fokus och åtgärd binds i ADR 0142, inte i koden** — Fil: D2/D3, precedens `RegisterForm.tsx:77-127`
Kanaldisciplinen är levererad och ska följas: **fel som användaren kan rätta i fältet** → `role="alert"` + `text-danger-600` + `aria-invalid` + fokus till fältet. **Tillstånd som inte är användarens fel men har väg framåt här** → `role="status" aria-live="polite"` + `tabIndex={-1}` + fokusflytt, `h2` i panelen (aldrig en andra `h1`). **Ingen väg framåt här** → formuläret **ersätts** av statuspanelen; ett levande fält bakom en vägran som aldrig kan lyckas inbjuder till en omöjlig retry (`RegisterForm.tsx:108-113` säger just detta).

| # | Tillstånd | Kanal / plats | Fokus | Copy (förslag) | Åtgärd |
|---|---|---|---|---|---|
| i | Fel kod | alert, under fältet | fältet | "Koden stämmer inte. Kontrollera siffrorna och försök igen." (+ "Ett försök kvar…" vid 3:e) | fältet kvar |
| ii | Utgången | status, ersätter fältet | panelen | "Koden har gått ut. Den gäller i 15 minuter." | "Skicka ny kod" blir primär |
| iii | Bränd | status, ersätter fältet | panelen | "Du har skrivit fel kod tre gånger. Av säkerhetsskäl behöver du en ny kod." | "Skicka ny kod" blir primär |
| iv | Använd på annan enhet | **status, aldrig alert** | panelen | "Inloggningen är redan klar i ett annat fönster. Vill du logga in även här behöver du en ny kod." | "Skicka ny kod" |
| v | Registrering stängd | status, ersätter formuläret | panelen | "Registreringen är inte öppen ännu. Vi hör av oss till din adress när den öppnar." | "Till startsidan" (länk) |
| vi | Konto under radering | status, ersätter formuläret | panelen | "Ditt konto raderas permanent {14 apr 2026}. Fram till dess kan du få det återställt genom att mejla kontakt@jobbliggaren.se." | e-postlänk |
| vii | Vilo/skickad | sidans grundrendering | h1 | se B2 | fält + "Skicka ny kod" + "Byt e-postadress" |

Mätt för (vi): återställning är **inte** självbetjäning — `HardDeleteAccountsJob.cs:28-33` ger 30 dagar via support. Erbjud därför **ingen** "Ångra"-knapp som inte finns; datumet formateras via `@/lib/i18n/format` ("14 apr 2026"). Tillstånd (iv) och (v) får aldrig röd danger-färg: inget fel har inträffat.
Delegera till: adr-keeper (del 0), nextjs-ui-engineer (del 2)

**M5. `/logga-in/lank` måste fungera med JS av — den levererade precedensen gör det inte** — Fil: `src/components/auth/confirm-account.tsx:39-47`
Nuvarande: dagens länklandning är en klient-ö med `onClick` + `useTransition`. Kopieras formen till `/logga-in/lank` är knappen **död utan JS** — och länken öppnas i en inbäddad mejlklient-webbvy där det inte är garanterat.
Krävs: `<form action={consumeLinkAction}>` med token i ett `<input type="hidden">` och en `<button type="submit">` — Server Action i form-position degraderar. Ingen `useEffect`-konsumtion (scanners GET:ar). h1 *"Logga in på Jobbliggaren"*, primär **"Fortsätt till Jobbliggaren"**. Feltillstånden ärver `confirmAccount.invalid*`-formen: egen `h1` + kropp + länk till `/logga-in` — utgången och använd får **samma** mening ("Länken går inte att använda. Begär en ny kod på inloggningssidan."), eftersom skillnaden inte hjälper användaren. `robots: {index:false}` + `referrer:"no-referrer"` som `bekrafta-konto/page.tsx:20-26`.
Delegera till: nextjs-ui-engineer (del 2)

**M6. "Mina sidor" — initialerna, pseudo-namnet och `.jp-avatar` faller tillsammans** — Fil: `src/components/shell/app-shell.tsx:76-84,137-147,152-158`, `src/app/globals.css:1023-1036`
Nuvarande: `initials(email)` i en `.jp-avatar`-pill (accent-100-fyllning, pill-radie), och menyns huvud visar `local` = e-postens lokaldel som **namn** (`app-shell.tsx:133,156`) — exakt det namn D7 säger att systemet inte har.
Krävs i del 3b: (a) triggern blir en **`.jp-icon-btn`** — samma form som `NotificationsBell` (`app-shell.tsx:96-104`) — med `<UserRound size={18} aria-hidden="true" />`, Lucide stroke/outline, `currentColor` (DESIGN.md §7). Ingen tintad cirkel: en färgad rund platta ÄR en avatarplaceholder. (b) `aria-label` = **"Mina sidor"**. (c) `jp-usermenu__name` (lokaldelen) **tas bort**; huvudet bär bara adressen. (d) `initials()` raderas, inte lämnas död. (e) `.jp-avatar`-regeln tas bort **efter mätt konsumentmängd** (`grep -rn "jp-avatar"`) — annars kvarlämnad död CSS. (f) `aria-haspopup="true"` på en `role="group"`-popup är en semantisk miss; anpassa till klockans mönster (`aria-haspopup="dialog"` + `role="dialog"` + `aria-labelledby`) eller ta bort attributet. (g) drawern (`app-shell.tsx:345-351`) och menyraden pekar om `/installningar` → `/mina-sidor` med samma etikett i **båda** ytorna.
Delegera till: nextjs-ui-engineer (del 3b)

---

### Minor

1. **Art. 13-raden på steg 1 — exakt form.** Under e-postfältet, som en andra hjälptextrad, kopplad via `aria-describedby="email-hint email-privacy"`: *"Så behandlar vi din e-postadress: <privacy>integritetspolicyn</privacy>."* Och villkorsrutans etikett blir **"Jag godkänner <terms>användarvillkoren</terms>."** — punkt; integritetspolicyn länkas i en **syskonmening** under rutan (*"Vi behandlar dina uppgifter enligt integritetspolicyn."*), aldrig inne i acceptansen. Dagens sträng `messages/sv/pages.json` `auth.register.termsLabel` bär båda och ska **inte** kopieras. (Utför security Major 8; graderingen är dess, formen är min.)
2. **Sidform och bredd.** De fyra rutterna stannar i `(auth)`-gruppen: SiteHeader/SiteFooter, centrerad `max-w-sm`, h1 i flödet. **Ingen `jp-pagehero`, ingen hero-gradient** — pagehero-mönstret är `(app)`-scopat och kräver appskalet. **Egen `h1` per rutt** (`/logga-in` "Logga in eller skapa konto" · `/kod` "Ange koden" · `/villkor` "Skapa ditt konto" · `/lank` "Logga in på Jobbliggaren") — {email} står i brödtexten, aldrig i `h1` eller `<title>`. `robots:{index:false}` på kod/villkor/lank.
3. **i18n-parity och döda nycklar.** Varje ny sträng i **både** `messages/sv/` och `messages/en/` i samma PR (ADR 0137; `en/pages.json` har full auth-paritet i dag). Retirerade nycklar (`auth.login.passwordLabel`, `rememberMeLabel`, `forgotPassword`, `noAccount`, `createAccount`, hela `auth.register.*`) **raderas i del 5** — inte lämnade föräldralösa. Em-dash och literala `...` är ESLint-spärrade; nedräkning återanvänder `auth.resendConfirmation.cooldownHint`-formen.
4. **`landing.auth.free`/`fine` tappar sitt hem när `/registrera` 308:ar** (`registrera/page.tsx:44-45`, `messages/sv/landing.json`): "Jobbliggaren är helt gratis att använda." / "Jobbliggaren säljer aldrig din data." står bara där och på landningskortet (#1493). Del 2 flyttar dem under `/logga-in`:s primär **eller** släpper dem med skälet namngivet — aldrig tyst.
5. **Touch-golvet och "Byt e-postadress".** ≤768px bumpas primär + provider-knappar till `size="lg"` (44px, DESIGN.md §5). "Byt e-postadress" är en **länk** till `/logga-in` (inte en knapp) och placeras sist, under "Skicka ny kod", så layoutens "Till startsidan" och stegets bakåtväg inte konkurrerar i samma synfält.

### Bra gjort
- `ResendConfirmationButton` löser redan 60 s-nedräkningen rätt: nedräkningen **utanför** live-regionen, tillståndet buret av `disabled` — återanvänd den exakt.
- `RegisterForm`:s tredelade kanaldisciplin (alert / status / ersatt formulär) är precis den form de sju kodstegstillstånden behöver; den är levererad och mätbar.
- Att kryssrutan flyttas till ett **eget steg efter koden** ger en skärm, en uppgift, en primär — det är GOV.UK-formen och det är rätt.

### Sammanfattning
3 Blocker, 6 Major, 5 Minor — samtliga formegenskaper ADR 0142 ska bära före kod. B1 kräver ett namngivet val mellan två grenar (säkerhet mot slutförbarhet), B2 en omskriven vilotext, B3 en placering. `[6a]`-märkta delar är FAS-DEFERRAL och mäts i 6a. **Ingen rendering finns**, så detta verdikt gäller formen, inte ytan: del 2:s PR måste rendera alla sju tillstånd plus villkors- och länkstegen i **båda** teman innan designverdikt (DESIGN.md §12, AGENTS.md §8 p.4). Delegera fixar till adr-keeper (del 0) och nextjs-ui-engineer (del 2/3b). Re-review efter fix: samma agent, report-only, scopad till fix-deltat (CLAUDE.md §9.6). Inga filer redigerade, inga issues filade, HEAD orörd (`081e4c67`).

**Eskalering till Klas:** ja — **provider-märkenas färgsättning saknar token och kan inte lösas inom DESIGN.md.** §7 säger "stroke/outline only, inga filled variants" och "färg ärvs via `currentColor` — aldrig hårdkodad ikonfärg", men Googles, LinkedIns och GitHubs varumärkesvillkor kräver deras egna märken i egen färgsättning på en "Logga in med"-knapp. Mitt förslag: **monokroma `currentColor`-märken i `--jp-ink-1` så länge knapparna är inaktiva** (inget varumärkeskrav utlöses av en knapp som inte loggar in någon), och att frågan om färgmärken avgörs i 6a som ett **scopat undantag skrivet in i DESIGN.md §3** (samma slag som `CvPalette` och `/oversikt`-korten), aldrig som tre lösa hex-literaler i en komponent. Jag behöver ditt besked om du vill (a) monokromt hela vägen och avstå de officiella märkena, eller (b) ett scopat DESIGN.md-undantag för de tre officiella märkena i 6a.
