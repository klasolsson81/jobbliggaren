# #1918 form round — dotnet-architect (report-only, 2026-09-28)

> Transcribed verbatim from the subagent hand-back (the harness's two-space indentation removed). Brief:
> `docs/sessions/2026-09-28-1918-matchning-form-brief.md`. Worktree `.claude/worktrees/oauth-google-1744-4b3a94` at `cb9f00bb`.

## Arkitektur-analys

### Sammanfattning
Det här behöver åtgärdas. Nuvarande kod har 0 kritiska fynd, 3 viktiga (F1–F3, som alla stängs i #1918:s PR) och 3 nice-to-have, och inget av dem är en §12-klass. Besluten i korthet:
- Skrivformen blir W2 i skärpt form: en `PATCH` med fem del-objekt.
- År inne i Yrken hanteras enligt (b).
- Alla fält i en del är obligatoriska, utom årsöverlägget.
- En tom request ger 400.
- Svaret är 204.
- PUT:en tas bort i nästa PR.
- Kombineringen läggs som `With*` på VO:t.
- Beslutet får en ny ADR 0147.

Alla sökvägar nedan är relativa till `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\oauth-google-1744-4b3a94\` (HEAD `cb9f00bb`). Fynd-rubrikerna har absoluta sökvägar.

### Rekommendation

**R1 — Skrivform: W2, skärpt.** `PATCH /api/v1/me/match-preferences` blir `UpdateMatchPreferencesCommand`. Kommandot bär `IReplayOnConcurrencyConflict`, endpointen `MeWritePolicy`, och det har fem nullbara del-objekt. En frånvarande del lämnas orörd. En närvarande del ersätter hela delen:

    { "occupations":     { "preferredOccupationGroups": [..], "preferredOccupationExperience": [..] },
      "skills":          { "preferredSkills": [..] },
      "locations":       { "preferredRegions": [..], "preferredMunicipalities": [..], "preferredRemote": false },
      "employmentTypes": { "preferredEmploymentTypes": [..] },
      "experience":      { "experienceYears": 5 } }

Medlemmarna inuti delarna har samma namn som VO:ts egenskaper.
- Paritetsvakten är namnbaserad, och namnet är redan kontraktet i jsonb, på tråden och i Zod (`MatchPreferencesContractParityTests.cs:29-34`).
- `GET /me/profile` bär samma nycklar.
- Del-recorden är Application-input (`*Input`, som `OccupationExperienceInput`) och aldrig Domain-typer.
- En handler som sätter 1–5 delar av ett VO gör fortfarande en sak (§2.3).

**Inte W1:**
- Railen sparar fyra delar i ett klick. Med W1 blir det fyra skrivningar som inte är atomära: ett delfel lämnar en halvsparad förstagångsuppsättning, och klicket kostar fyra MeWrite-tokens.
- Notiser har en endpoint per kontroll (ADR 0145 D4). Det bärs av att varje kontroll har eget ändamål och egen audit-händelse, och av att ingen anropare skriver två kontroller samtidigt. Här skriver railen fyra.

**Inte W0:** den lämnar F1 och F2 öppna och är bara en stoppkloss.

**Förkastat också:**
- Elementoperationer (`remove x`): de stänger racet inom samma del, men ger två skrivgrammatiker för ett VO.
- JSON Merge Patch: den arbetar på fältnivå, bryter delatomiciteten (region utan kommun, NOTE-1) och kräver otypad bindning.
- If-Match per del: ADR 0146 strök ETag ur BUILD.md §6.1.

**R2 — År inne i Yrken: (b).** I `occupations` är `preferredOccupationExperience` den enda valfria medlemmen:
- Frånvarande: lagrade år behålls för yrken som fortfarande är valda, filtrerat mot den normaliserade grupplistan.
- `[]`: rensar.
- Närvarande: ersätter, och subset-regeln (`MatchPreferences.cs:183-191`) prövar mot de nya grupperna.

Regeln bakom är att en yta bara skriver det den renderar.
- Dialogen och railen renderar år per yrke och skickar dem.
- Kortet renderar inga år och utelämnar dem.
- Med (a) skulle chip-borttagningen i en inaktuell flik skriva om år som användaren inte ser.

Här underkänner jag design-passets `WithOccupations(groups, overlay?)`. Domain får två overloads (R6), inte null som betyder "behåll", så trådens tri-state stannar i handlern.

**R3 — Obligatoriska fält och tom request.**
- **`[property: JsonRequired]` på alla medlemmar i en närvarande del**, utom årsöverlägget. Det är husets form: `UpdateNotificationConsentCommand.cs:20-22,35`, pinnad i `NotificationConsentEndpointTests.cs:178-181`.
- **`NotNull` på listorna också**, eftersom `JsonRequired` släpper igenom en explicit `null`. `[]` är det enda sättet att rensa.
- **`preferredRemote` måste ha `JsonRequired`.** En saknad bool binds till `false`, alltså "vill inte ha distans". Dialogen varnar själv för detta (`match-preferences-dialog.tsx:49-51`).
- **`experience`-delen:** `experience: {}` ger 400, medan `{"experienceYears": null}` rensar.
- **Ingen del närvarande ger 400**, via en validatorregel efter `UpdateSavedSearchCommandValidator.cs:24-28`. Den regeln är den uttalade; `UpdateMyProfile` har bara ett fält och bevisar inget. Skälen:
  - Ingen produktanropare skickar en tom request.
  - En no-op bumpar ändå `UpdatedAt` (`JobSeeker.cs:347-351`). Det blir ett UPDATE med xmin som kan tvinga fram en replay hos en samtidig skrivare, och det kostar en MeWrite-token.
  - En platt, PUT-formad body mot PATCH:en binder noll delar, eftersom okända medlemmar ignoreras. Den ska bli 400, inte ett tyst 204.

**R4 — 204, inte de lagrade delarna.**
- **Husparitet:** varje `/me`-skrivning och båda PATCH-precedenten svarar 204 (`MeEndpoints.cs:55,71,84,104`, `CompanyWatchCriteriaEndpoints.cs:258`, `SavedSearchesEndpoints.cs:99`).
- **CQRS (§2.3):** läsmodellen är `GET /me/profile` och är paritetspinnad. Ett andra läsformat i skrivsvaret skulle behöva en egen vakt.
- **Klienten vet redan vad den skrev.** Utöver normaliseringen är den enda ändring servern härleder årsbeskärningen i (b), och den räknar FE redan ut (`match-preferences-shared.ts:114-130`).
- **Ett helt dokument i svaret är farligt.** Adopterar kortet ett helt dokument ur en dels svar skriver det över en annan dels optimistiska state i flykten. Det händer så fort Next slutar serialisera Server Actions, vilket Next kallar "an implementation detail and may change" (next@16.3.5 `07-mutating-data.md:207`).

**R5 — PUT:en tas bort i nästa PR (expand–contract).** Inte i denna PR, och inte "aldrig".

Skälet: reconcilen på `latest` kan installera en blandad uppsättning, med web från en commit och api från en annan. Att stänga det är ett åtagande som inte är levererat (`deploy/systemd/jobbliggaren-reconcile.timer:10-28`, #1238). Det är just en borttagning som får halvan "gammal web mot ny api" att misslyckas med sparningar.

Kontrakt-PR:en öppnas när web-imagen med PATCH-anroparna är uppmätt live (via dess `sha-<short>`-digest). Den gör följande:
- Tar bort endpointen, `SetMatchPreferences{Command,Handler,Validator}` och deras enhetstester.
- Flyttar setup i de sju integrationstesterna till PATCH: `GetCvSectionSuggestionsEndpointTests`, `RelatedSurfacingEndToEndTests`, `MatchTagBatchEndpointsTests`, `JobAdMatchDetailEndpointTests`, `CriterionMatchingAdCountApiTests`, `CompanyWatchesMatchCountApiTests` och `CompanyWatchesMatchCountCrossUserIsolationTests`.
- Tar bort paritetsvaktens PUT-fakta.
- Raderar meningarna som motiverar fält med full-replace-PUT:en: `JobSeekerProfileDto.cs:29-37,42-46,48-52,55-58`, `MatchPreferencesContractParityTests.cs:20-26,87-88` och `SetMatchPreferencesCommand.cs:24-26`.

**R6 — Kombineringen bor i Domain, på VO:t.** Varje metod anropar `Create(...)` med instansens egna värden för de övriga dimensionerna. Då har normaliseringen och alla invarianter en enda källa:

    public Result<MatchPreferences> WithOccupations(IEnumerable<string> groups); // behåller år för kvarvarande yrken
    public Result<MatchPreferences> WithOccupations(IEnumerable<string> groups, IEnumerable<OccupationExperience> experience);
    public Result<MatchPreferences> WithSkills(IEnumerable<string> skills);
    public Result<MatchPreferences> WithLocations(IEnumerable<string> regions, IEnumerable<string> municipalities, bool remote);
    public Result<MatchPreferences> WithEmploymentTypes(IEnumerable<string> types);
    public Result<MatchPreferences> WithExperienceYears(int? years);

Handlern bara sekvenserar, enligt husidiomet i `UpdateCompanyWatchCriterionCommandHandler.cs:10-14`:
- Den läser seekern tracked och applicerar de närvarande delarna.
- Den returnerar första `IsFailure`, och annars anropar den `UpdateMatchPreferences(next, clock)` en gång.

**Inte i handlern:** beskärningen i (b) följer av VO:ts subset-invariant, och en merge i handlern vore en anemisk domän (§2.2).

**Inte fem nya metoder på `JobSeeker`:** aggregate rooten behöver inte känna till delarna, och `UpdateMatchPreferences` förblir den enda mutatorn som IL-svepet ser.

Delarna är disjunkta, och den enda tvärfältsinvarianten (år ⊆ yrken) ligger inom en del, så ordningen spelar ingen roll. ADR 0147 ska skriva in regeln att en framtida invariant över två delar flyttar delgränsen. Annars kan en skrivning av en del fällas av en del som användaren inte rörde.

**R7 — ADR 0146:s replay per del: markörkontraktet håller och blir bärande.** Kontraktet säger att handlern härleder allt ur det den läser och inte har någon sidoeffekt före commit (`IReplayOnConcurrencyConflict.cs:10-12`).

I dag når den färska läsningen aldrig `match_preferences` (se F1). Med W2 härleds de orörda delarna ur läsningen. Efter `ClearTracking()` (`UnitOfWorkBehavior.cs:44`) läser replayen den konkurrerande commiten och lägger bara denna requests delar ovanpå. Utfallen:
- **Olika delar i två flikar:** båda landar.
- **Samma del:** den sista vinner för den delen. Det accepteras och skrivs in i 0147.
- **Yrken utan år, enligt (b):** åren beskärs mot det färska överlägget.

Övrigt:
- En replay kan inte falla på validering som första försöket klarade, eftersom den enda korsinvarianten ligger inom en del.
- Efter taket på 3 försök blir det 409, som mappas till `stateConflict` (`_action-error.ts:38-40`).
- Portarna finns i allowlistan (`JobSeekerWriterReplayGuardTests.cs:39-46`). Svepet (`:56-72`) plockar upp handlern eftersom den anropar `JobSeeker.UpdateMatchPreferences`.
- Skrivfrekvensen ändras inte, så D6 står.

**R8 — ADR-hem: en ny ADR 0147, främjad med `git add -f`, plus en pekarrad i 0146.**
- **Numret:** 0147 är nästa nummer ur `docs/decisions/` vid `cb9f00bb`. README.md:93 och :111 räknar ur katalogen, inte ur tabellen, och huvudkopians lokala serie slutar på 0142. Räkna om när ADR:en skrivs, eftersom parallella sessioner tar nummer ur samma serie.
- **Främjas med `git add -f`:** 0071–0199 är lokala som standard (`.gitignore:144-163`), och 0143–0146 är främjade på samma sätt.
- **Inte en amendment till 0076:** den finns inte i det publika indexet. Ett publikt API-kontrakt skulle då beslutas på ett ställe som PR-diffen och andra lanes inte når (§6.5).
- **Inte i 0146:** den äger mekanismen, och detta är ett kontrakt som använder den.
- **0146 får en daterad pekarrad vid D3.** Formuleringen "Twelve command types" blir fel med det nya markerade kommandot.
- **0147 innehåller:**
  - R1–R7 och regeln för samma del.
  - Villkoren för att ta bort PUT:en.
  - Att ADR 0079 STEG 3 (c), "zero backend merge" (`0079:366-367`), fortfarande står, eftersom kompetensdelen alltid ersätts helt. Det enda som servern behåller inom en del är (b).
  - Att "antal års erfarenhet" är en levande del enligt Klas-direktiv 2026-09-28. Det motsäger ADR 0079 Fork B:s "deprecated-in-doc". 0079 är lokal, så 0147 blir den publika posten.

**R9 — Railen och defekterna.** Railen skickar en PATCH med fyra delar och ingen `experience`.
- **(a)** stängs då av konstruktionen. Den hanteras i blocket, med en pin (F2).
- **(d)** hanteras i blocket, eftersom raderna skrivs om ändå (F3).
- **(b)** filas som issue (F4).
- **(c)** är design-reviewers fråga. Den stängs av per-del-statusen, som visar den mappade texten.

**R10 — Testlista (krav).**
- **Domain** (`MatchPreferencesTests`):
  - Per `With*`, som en theory över de fem delarna, byts bara den delen och resten är sekvenslika.
  - `WithOccupations(groups)` behåller åren för kvarvarande yrken och släpper borttagna.
  - `[]` rensar överlägget.
  - År för ett yrke som inte är valt ger `OrphanOccupationExperience`.
  - `WithExperienceYears(null)` rensar, och 71 ger `ExperienceYearsOutOfRange`.
  - Normalisering och tak går via `Create`.
- **Application, handler:**
  - Seeda alla åtta fält; bara den skickade delen ändras.
  - Fyra delar utan `experience` behåller `ExperienceYears` (pin för F2).
  - En ogiltig del bland två ger failure och sätter ingenting.
  - Oautentiserad användare och saknad seeker.
- **Application, validator:**
  - Ingen del närvarande.
  - Varje del som saknar ett obligatoriskt fält.
  - `occupations` utan år är giltig.
  - Tak och regex per fält.
- **Integration** (`Me/MatchPreferencesTests`, i `[Collection("Api")]`; `OwnContainerPerTestTests.cs:30-32` kräver exakt matchning):
  - PATCH per del ger 204, och profilen visar bara den delen ändrad.
  - `{}` ger 400.
  - `locations` utan `preferredRemote` ger 400.
  - `experience: {}` ger 400, och `experienceYears: null` rensar.
  - Yrken utan år behåller åren.
  - Railens fyra delar lämnar `experienceYears` orört.
  - 401 utan auth.
- **Race-rad** (`JobSeekerWriteRaceTests`, `JobSeekerSaveRace`): en `skills`-PATCH hålls vid SaveChanges medan en `occupations`-PATCH från den andra fliken committar. Förväntat:
  - Båda svarar 204.
  - `Fired == 1`.
  - Lagrat är konkurrentens yrken plus den hållna kompetensmängden.
  - Övriga delar har sina seedade värden.
- **Arkitektur:**
  - `MatchPreferencesContractParityTests` får en partitionsfakta: varje VO-dimension ska finnas i exakt en del (täckning plus disjunkthet).
  - PUT-faktan lever kvar till kontrakt-PR:en.
  - `JobSeekerWriterReplayGuardTests` är oförändrad. Mutationskontroll: tar man bort markören ska svepet bli rött.
- **Rate-limit:** ett wiring-test i samma form som `CompanyWatchCriteriaRateLimitWiringTests`. PATCH-routen ska ha `MeWritePolicy`, läst ur `EndpointDataSource`. Helst täcker det hela `/me`-gruppen med ett räknat antal routes (F6).
- **FE:**
  - Del-scheman utan `.default()` och `.optional()`: `match-preferences-schemas.ts:29,52,61-67` är fällan.
  - Kortet skickar bara sin del.
  - I (d)-sekvenserna fel/ok och fel/fel ska UI:t och den sista payloaden vara lika.
  - Railen skickar fyra delar utan `experience`.
  - 409 ger den mappade texten.

### Fynd

**[Viktigt]** F1 `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\oauth-google-1744-4b3a94\web\jobbliggaren-web\src\components\settings\match-preferences-card.tsx:242-258` · `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\oauth-google-1744-4b3a94\src\Jobbliggaren.Application\JobSeekers\Commands\SetMatchPreferences\SetMatchPreferencesCommandHandler.cs:48-60`
**Vad:** Varje chip-borttagning skickar alla åtta fält ur flikens egen kopia, och handlern bygger hela VO:t ur kommandot. En annan fliks commit till vilken annan dimension som helst skrivs då över. ADR 0146:s replay kan inte stoppa det: de inaktuella värdena ligger i request-bodyn, och den färska läsningen når aldrig kolumnen.
**Varför:** Markörens garanti (`IReplayOnConcurrencyConflict.cs:10-12`) når inte `match_preferences` så länge handlern inte läser den kolumn den skriver. Replayen blir då på API-nivå den "client wins"-form som `UnitOfWorkBehavior.cs:41-43` avvisar. Det följer av full-replace-kontraktet (`SetMatchPreferencesCommand.cs:24-26`) och är ingen defekt i ADR 0146.
**Föreslagen åtgärd:** W2 plus `With*` (R1, R6), i blocket. Race-raden i R10 pinnar det.

**[Viktigt]** F2 `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\oauth-google-1744-4b3a94\web\jobbliggaren-web\src\components\settings\match-setup-rail-modal.tsx:397-405`
**Vad:** Defekt (a).
- Railen skickar inte `experienceYears`, och schemat har `.optional()` utan default (`match-preferences-schemas.ts:61-67`).
- Nyckeln försvinner därför i JSON, kommandot binder `null` (`SetMatchPreferencesCommand.cs:44`) och ett angivet värde rensas.
- Det är nåbart: ta bort sista yrket på kortet och öppna sedan railen via `?matchsetup=1` (`oversikt/page.tsx:173-177`).

**Varför:** Det är den page-wipe-klass som DTO:n själv varnar för (`JobSeekerProfileDto.cs:48-52`).
**Föreslagen åtgärd:** Railens PATCH med fyra delar och utan `experience` stänger den av konstruktionen. Pinna den på handler-, integrations- och FE-nivå, i blocket.

**[Viktigt]** F3 `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\oauth-google-1744-4b3a94\web\jobbliggaren-web\src\components\settings\match-preferences-card.tsx:298, 332, 348-353`
**Vad:** Defekt (d). Både payload och revert-snapshot låses när användaren klickar.
- Inom en flik serialiseras Server Actions (next@16.3.5 `server-actions.md:28`), så det krävs ett misslyckat anrop, inte ett race.
- Borttagning 1 misslyckas och 2 lyckas: UI:t visar båda chipsen, men servern har inget av dem.
- Båda misslyckas: UI:t tappar A, men servern har kvar A.
- MeWrite-gränsen (30 per 60 s, delad över `/me`, `RateLimitingOptions.cs:552-556`) gör det nåbart.

**Varför:** Nästa skrivning av delen skickar UI:ts felaktiga bild och ångrar tyst en borttagning som lyckades.
**Föreslagen åtgärd:** I blocket. W2 stänger fallet över delar. Inom en del gäller två regler:
- Payloaden räknas ut när skrivningen skickas, ur senast kvitterat state minus väntande borttagningar.
- Ett fel återställer bara sina egna member-id:n (inversen), aldrig ett snapshot från klicket.

Med `disabled` på chipsen under skrivningen tappas tangentbordsfokus (WCAG 2.4.3). Mekanismen är design-reviewers.

**[Nice-to-have]** F4 `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\oauth-google-1744-4b3a94\web\jobbliggaren-web\src\components\onboarding\match-setup-launcher.tsx:80-94`
**Vad:** Defekt (b). Launchern skickar inga `persistedSkillGroups`, så sparade kompetenser syns som råa concept-id i railen. /oversikt löser aldrig etiketterna (`oversikt/page.tsx:203-216`), medan matchningssidan gör det (`(matchning)/page.tsx:44-48`).
**Varför:** En rå token på en läsyta (ADR 0047, som `match-preferences-card.tsx:81-88` citerar). Det är läsvägen, ett eget ändringsskäl.
**Föreslagen åtgärd:** Fila som issue med lane FE och `mvp`. Fixen är att anropa `resolveSkillLabels` i setup-grenen och skicka resultatet genom launchern. Användargraden är design-reviewers.

**[Nice-to-have]** F5 `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\oauth-google-1744-4b3a94\src\Jobbliggaren.Domain\JobSeekers\MatchPreferences.cs:88-90`
**Vad:** Kommentaren säger "the scalar is retained inert for back-compat". Men dialogen redigerar skalären (`match-preferences-dialog.tsx:323-330`), kortet renderar den (`match-preferences-card.tsx:496-514`), och direktivet gör den till en egen del. Samma sorts fel finns på tre ställen:
- `MeEndpoints.cs:44-45` säger "three collections".
- `match-preferences-schemas.ts:15-18` säger "tre arrayer".
- `lib/actions/match-preferences.ts:22-23,29-30` säger "fyra".

**Varför:** Enligt AGENTS.md §5 Comments är en faktiskt fel kommentar en defekt.
**Föreslagen åtgärd:** Radera satserna, formulera inte om dem (§9.6), i de filer PR:en ändå redigerar. Resten försvinner med PUT:en (R5).

**[Nice-to-have]** F6 `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\oauth-google-1744-4b3a94\src\Jobbliggaren.Api\Endpoints\MeEndpoints.cs:34-41`
**Vad:** `PATCH /me/profile` saknar rate-limit-policy; kommentaren på `:47-48` kallar den "unrated". Ingen `/me`-route har en wiring-pin. Sedan ADR 0146 är routen en replayande skrivare av samma rad.
**Varför:** En skrivare utan gräns gör det billigare att nå ADR 0146:s tak för radens andra skrivare, men bara på det egna kontot (D6).
**Föreslagen åtgärd:** Det räknade `/me`-wiring-testet tvingar fram ett namngivet beslut. Valet av policy är security-auditors (`RateLimitingOptions.cs:548-550`).

### Referenser
- AGENTS.md §2.1–2.3, §5 (Tests, Comments), §7; CLAUDE.md §6.5, §9.6
- ADR 0146 D2/D3/D6 och ETag-alternativet; ADR 0145 D4; ADR 0079 STEG 3 (c) och Amendment 2026-06-23 Fork B/E (lokal); ADR 0009
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\oauth-google-1744-4b3a94\docs\decisions\README.md:93,111`; `...\.gitignore:144-163`; `...\deploy\systemd\jobbliggaren-reconcile.timer:10-28` (#1238)
- `...\web\jobbliggaren-web\node_modules\next\dist\docs\01-app\02-guides\server-actions.md:28` och `...\01-getting-started\07-mutating-data.md:207` (next@16.3.5)
