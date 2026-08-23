# dotnet-architect — PR #1458 (#1448)

> Transkriberad ordagrant av ägande session. Agentens charter är read-only, så han kunde
> inte skriva filen själv. Ingen eskalering till Klas.
>
> **Runda 1:** 1 Kritiskt · 3 Viktigt · 5 Nice-to-have.

**PR #1458 · issue #1448 · branch `fix/written-form-channels-1448` · HEAD `bbf855ff` · base `0e32f68a`**
**Läge:** full review (ej scoped re-check) · **Datum:** 2026-08-23
**Alla radnummer avser HEAD-bloben, inte arbetsträdet (se V3).**

## Sammanfattning

Behöver åtgärdas — 1 kritiskt, 3 viktiga, 5 nice-to-have fynd. D1–D4 är implementerade troget
och Clean Architecture håller; det kritiska fyndet är att prestandaregressionen inte är en
budgetfråga utan en funktionell: den nya formen passerar Npgsql:s default command timeout och
Art. 17-torrkörningen kastar i stället för att svara. Fixen är mekanisk, mätt ekvivalent och
kostar en rad per arm.

## Fynd

### [Kritiskt] `RecruiterErasureMatchQuery.cs:176-198`

**Vad:** `FindJobAdsAsync` går från att slutföra till att **avbrytas** under 30 s-taket.
Mätt mot dev-korpusen (106 071 annonser, PG 18, `SET statement_timeout='30s'`), två körningar:

| form | körning 1 | körning 2 |
|---|---|---|
| före PR | 7,09 s | 3,98 s |
| **shippad** | **ERROR: canceling statement due to statement timeout** | **ERROR (30,0 s)** |
| föreslagen (`LIKE ANY`) | 14,96 s | 17,07 s |

Utan tak: före 16,8 s · shippad 83,4 s · föreslagen 27,1 s. PR-body:ns egna 47,4 s warm ligger
också över 30 s — varje avläsning av den shippade formen, min som din, gör det.

**Varför:** Npgsql:s `Command Timeout` default är **30 s**
(npgsql.org/doc/connection-string-parameters.html, läst 2026-08-23), repot dokumenterar samma
tal själv i `ScbCompanyRegisterStore.cs:28-29`. Ingen connection string i repot sätter
`Command Timeout`, och `AppDbContext` har inget `SetCommandTimeout` — EF sätter
`DbCommand.CommandTimeout` bara när den är satt, annars gäller providerns default. Det gör
detta till mer än AGENTS.md §2.5: den enda obligatoriska review-grinden före en oåterkallelig
radering **failar** i stället för att svara. Den failar högljutt, inte tyst — det är
förmildrande, men den kör inte.

⚠ **Den ena omätta länken:** att `Database.SqlQuery` ärver providerdefaulten har jag inte kört
end-to-end i det här repot; SQL-demonstrationen under `statement_timeout` är en proxy. Även om
talet vore större kräver §2.5 ett skrivet verdikt för en 5×-regression, och fixen kostar lika
lite då.

**Föreslagen åtgärd:** fix in-block. Ersätt `EXISTS (SELECT 1 FROM unnest({patterns}) AS p
WHERE <expr> LIKE p ESCAPE {LikeEscapeSql})` med `<expr> LIKE ANY({patterns})` på alla 25 arms.

`= ANY({writtenForms})` bevisar redan att array-parametern binder genom exakt den här vägen.
**Detta är ingen andra normaliserare.** `LikePattern` är oförändrad, mönstren är oförändrade,
regeln är en. Det enda som förloras är den *explicita* ESCAPE-bindningen — `LIKE ANY` har ingen
ESCAPE-klausul. Betala igen det med en pin: `LikeEscape` **måste** vara Postgres default
(backslash). Mätt: `like_escape('%a\_b%','\')` returnerar `%a\_b%` oförändrat, och per mönster
över `%anna%` · `%50\%%` · `%a\_b%` · `%h\\m%` · `%_%` · `%ab%` ger båda formerna identiska tal
(83·0·0·65·106071·70702), liksom jsonb-vandringen (27 160 båda vägar). Lägg till ett
enradstest `LikeEscape_is_the_postgres_default_backslash`.

**Avvisade alternativ, med mätning:**

- **Regex-alternering** avvisas på filens egen grund: den inför ARE-escaping vid sidan av
  LIKE-escaping, vilket är precis #844 som fem kommentarer i filen åberopar. Den behövs inte:
  `LIKE ANY` tar vinsten utan en ny regel.
- **`IS NOT NULL`-vakterna:** din hypotesförkastning reproducerad — 34,3 s med vakt mot 30,4 s
  utan. Hjälper inte.
- **Lateral hoist av `lower(v #>> '{}')` med bevarad ESCAPE:** 23,7 s mot 22,0 s shippat.
  Planeraren betalar inte för den.
- **`LIKE ANY (SELECT like_escape(q,'\') FROM unnest(...))`** — bevarar klausulen bokstavligt:
  **79,2 s.** Använd inte.
- **`::text`-förfilter före vandringen:** 8,5 s mot 10,2 s — men **osunt**. Den serialiserade
  formen escapar `"`, `\` och kontrolltecken, så en identifierare som bär något av dem
  exkluderas av förfiltret medan det riktiga predikatet matchar. Ett fail-open förfilter på en
  Art. 17-kanal är defektklassen den här PR:en finns för att stänga.
- **Index:** **inget index kan hjälpa.** Predikatet är `LIKE '%…%'` över utdata från en
  set-returning function per rad. Ett `pg_trgm`-index över `raw_payload::text` skulle bara
  betjäna den övermatchande formen PR:en tog bort. En genererad kolumn med skalärvärdena + GIN
  vore sund och indexerbar — men den duplicerar en kolumn `PurgeStaleRawPayloadsJob` ändå
  NULLar, för en admin-yta som körs en handfull gånger per år, via en migration på den
  farligaste hotspoten. Avböjs uttryckligen.
- **Arm-ORDNING:** för en **no-match**-identifierare — Art. 17-normalfallet — ändrar ordningen
  ingenting; varje arm utvärderas på varje rad. Se N3 för matchfallet.

### [Viktigt] `OrganizationNumber.cs:138-139` · `RecruiterErasureMatchQuery.cs:733` · `recruiter-pii-erasure.md:123`

**Vad:** "vilken kolumn normaliserar på write" står i tre hem och inga två är överens — och
Domain-hemmet, som äger regeln, motsägs av PR:ens egen F2-mätning.

- Domain: *"A column whose write path normalises (`job_ads.organization_number`,
  `recent_job_searches.employer_list`)"* — F2 mätte att den första **inte** gör det. Filen
  rördes inte av PR:en.
- Infrastructure `:733`: *"It is the ONLY such column here"* om `company_watches.organization_number`.
- Runbook `:123`: *"`company_watches.organization_number` is the only column on the first side."*

Mätt mot koden: **två** kolumner får single-form-proben — `company_watches.organization_number`
och `recent_job_searches.employer_list` (`{orgNr} = ANY(...)`). `?employer=` är grindad
`^[0-9]{10}\z` (`ListJobAdsQueryValidator.cs:22`), så kolumnen bär bara den normaliserade
formen. Samtidigt påstår klassens egen kommentar att `employer_list` normaliserar via
`ValidateEmployerList → OrganizationNumber.Create` — och `ValidateEmployerList` bor i
`SearchCriteria.cs:239` och styr `saved_searches.criteria`, inte den kolumnen.

**Varför:** commit `bbf855ff` flyttade uppräkningen in i koden just för att en handhållen lista
förfaller — och lämnade tre prosapåståenden som inte går ihop. Domain-raden är den farliga: den
namnger kolumnen PR:en nyss bevisade inte normaliserar, i det lager som äger regeln.

**Föreslagen åtgärd:** stängs mekaniskt genom **strykning** (§9.6): ta bort
`job_ads.organization_number` ur Domain-parentesen, och stryk ordet "only"/"ONLY" i de två
andra hemmen eller namnge båda kolumnerna.

### [Viktigt] `RecruiterErasureIngestTests.cs:1948` och `:2208-2209` (samt commit `749f0af5`)

**Vad:** två nya doc-kommentarer motiverar ett fixture-krav med `= 'string'`-predikatet som
**samma PR** ersatte i D1. Mätt, dokumentets exakta form
(`{"...":[],"Remote":true,"Q":null,"SortBy":0}`): 10 `$.**`-noder, **0** strängnoder, **3**
icke-container-noder. Ett bart wildcard `%` under det **shippade** predikatet når dokumentet
(`t`); under det gamla `= 'string'` gjorde det inte (`f`).

Så `:1948` och `:2209` är båda falska mot koden de står bredvid. (`:1948`s "ten `$.**` rows and
zero string ones" är däremot korrekt uppmätt — det är slutsatsen som inte längre följer.)

**Varför:** AGENTS.md §5 `Comments:` — en faktiskt felaktig kommentar är en defekt. Den här är
värre än stilistisk: `:2209` är en **instruktion till nästa testförfattare** om hur fixtures
måste byggas, mot en regel som inte finns. Den attribuerar också stringens till
`jsonb_path_query`, som inte har någon — den låg i typpredikatet, som D1 band om.

**Föreslagen åtgärd:** stryk de två meningarna. Container-predikatet släpper igenom boolean och
number, så neutrala rader nås av ett bart wildcard och kontrollen är röd utan `Q` — vilket är
starkare, inte svagare. Kravet på `Q` kan då tas bort helt.

### [Viktigt] arbetsträdet `c:/tmp/jbl-writtenform-1448` (ej i diffen)

**Vad:** `git status --porcelain` ger ` M RecruiterErasureMatchQuery.cs`. Den ocommittade
ändringen återställer `saved_searches.criteria` från value walk till
`lower(criteria::text) LIKE p` — exakt den mutation
`An_identifier_that_is_only_a_saved_search_criteria_KEY_NAME_matches_no_row` namnger.

**Varför:** en kvarlämnad mutationsprob i trädet, på en fil vars HEAD-form är hela PR:ens poäng.
§9.6 kräver att HEAD verifieras oförändrat omedelbart före `agents-done`.

**Föreslagen åtgärd:** `git checkout --` filen före något annat, och verifiera
`git status --porcelain` tomt före `agents-done`.

### [Nice-to-have] `:289` — `terms` byggs per rad

`string[] terms = [needle, .. writtenForms]` byggs per rad i `Evidence()`, men båda är
konstanta över anropet. **Fråga 4:s svar är ja, formen är rätt och kostnaden acceptabel.**
Substring-semantiken matchar SQL:ens; att `FirstMatchedAxisValue` använder likhet är korrekt
eftersom *dess* SQL är `= ANY`. Bygg `terms` en gång före `.Select(...)`.

### [Nice-to-have] `:710` — `AdContactOriginLiterals` är `internal` utan konsument

Enda referensen utanför filen är en `<c>`-tagg i en testkommentar. Gör den `private static
readonly`. (Riktningen är i övrigt rätt — Infrastructure som läser en Domain-enum är
AGENTS.md §2.1:s tillåtna riktning. **Fråga 2: bekräftat.**)

### [Nice-to-have] `:176-198` — arm-ordningen

`raw_payload`-vandringen (10-22 s) ligger före `organization_number = ANY(...)` (~0) och
`contacts`-vandringen (0,6-0,8 s). Postgres omordnar AND-kval efter kostnad men **inte**
OR-armar. För no-match ändrar det inget, men för en identifierare som **finns** betalar varje
träffad rad hela payload-vandringen först. Flytta de billiga armarna före.

### [Nice-to-have] commit `c9bab9d8` · PR-body

(a) *"Every arm now draws its patterns from WrittenFormPatterns"* — tre exakta arms gör inte
det. "Varje LIKE-arm" är sant. (b) Per-arm-tabellen är enkelkörningar utan angiven körräkning.
Min regenerering reproducerar **riktningen** men inte magnituderna: `raw_payload` 14,8× (du:
3,4×), textkolumner 3,8× (du: 2,3×), helheten 5,0× (du: 6,9×). Skriptets arm-ordning är
dessutom själv en confound — warmup:en rör inte de toastade kolumnerna.

**Verifierat och korrekt i PR-body:n:** 106 071 annonser · 62 778/62 778 org.nr exakt tio
ASCII-siffror · 40 983 contacts-rader · `clare` 16 999 före **och** efter vandringen, 10 efter
Origin-exklusionen · `name` 27 160 → 1 · noll `::text) LIKE` kvar · 25 LIKE-arms · sju
konverterade query-metoder · fjorton nya facts · `WrittenForms()`s första element är
tio-siffriga formen.

### [Nice-to-have] Origin-residualen

Residualstycket säger *"a contact whose `Name` is exactly `Declared` is not reached"*, men
`<> ALL(...)` appliceras på **varje** skalär — en kontakt vars `Role` är exakt en origin-literal
tystas också. Ett ord: `Name` → "any field".

## Vad som är korrekt implementerat (ingen åtgärd)

- **D1** — `NOT IN ('object','array')` på alla fyra jsonb-arms; `'null'`-parentesen stämmer.
- **D2** — `= ANY({writtenForms})` plus evidensgrenen. Membership-testet i C# är rätt spegling
  av SQL-armen, och `OrgNrEvidence` via `TryFromWrittenForm` är korrekt.
- **D3** — Origin-exklusionen ligger på de två `AdContacts`-formade kolumnerna och **inte** på
  `raw_payload`. `Enum.GetNames` + `ToLowerInvariant` kan inte drifta för ASCII.
- **D4** — uppräkningen ligger i koden; `Channels` är inte destinationen. Rätt.
- **Fråga 1, asymmetrin:** rätt, och dokumenterad där en läsare möter den. `NormalizedOrgNr`
  förtjänar sin plats.
- **Fråga 2, Clean Architecture:** `Jobbliggaren.Application.csproj` orörd — ingen Npgsql, ingen
  `.Relational`. **AGENTS.md §2.1 axel 3 uppfylld.**
- **Inverse-egenskapen:** `TryFromWrittenForm` accepterar exakt de sex formerna
  `WrittenForms()` emitterar.
- Att `IS NOT NULL`-vakterna kunde tas bort är korrekt.

## Eskalering

Ingen. K1 och V1-V3 är alla in-block-stängbara, K1 mot en mätning och V1-V2 genom strykning.

## Referenser

- AGENTS.md §2.1 axel 3 · §2.5 · §5 `Comments:` · CLAUDE.md §9.6 · §12
- Npgsql, *Connection String Parameters* — `Command Timeout`, default 30, läst 2026-08-23
- `ScbCompanyRegisterStore.cs:28-29` — repots egen notering om samma 30 s-default (#688)
- #844 — en regel med två normaliserare är två regler

---

# Omkontroll (scoped, report-only) — delta `bbf855ff..92fb5d4e`

> Transkriberad ordagrant av ägande session. HEAD `92fb5d4e` verifierad, `git status --porcelain`
> tomt vid granskningstillfället. Runda 1 + 1 omkontroll = §9.6:s cap förbrukad efter denna.

**Sammanfattning:** 1 viktigt + 1 nice-to-have new-in-delta. Alla nio runda-1-fynd stängs; K1 stängs
som **delta-fynd** på warm-mätningen, men kall-cache-residualen stängs inte av deltat och måste filas.

| Fynd | Utfall |
|---|---|
| **K1** 30 s-taket | **CLOSED** (delta). Kall residual → egen issue. |
| **V1** tre oense normaliseringshem | **CLOSED** |
| **V2** två falska `= 'string'`-kommentarer | **CLOSED** |
| **V3** smutsigt arbetsträd | **CLOSED** |
| **N1** `terms` per rad | **CLOSED** |
| **N2** `AdContactOriginLiterals` internal | **CLOSED** |
| **N3** arm-ordningen | **CLOSED** |
| **N4** överdrifter | **OPEN** (a) / CLOSED (b) — se F2 |
| **N5** Origin-residualen | **CLOSED** |
| **NEW F1** falsk motivering till pnr-flaggan | **Viktigt** |
| **NEW F2** "26 arms" | **Nice-to-have** |

## [Viktigt] F1 — motiveringen till `termsArePersonnummerShaped` är falsk

Meningen *"An excerpt is a window around a matched TERM, so the question … is answered by the term
that caused the match, not by re-scanning the window"* håller inte mot koden bredvid: `Description`
returnerar 200 tecken omgivande prosa och flaggan beräknas ur **request:ens** terms, så för en
icke-org.nr-identifierare är den ovillkorligt `false` oavsett vad fönstret bär. `ContactsMatch`
returnerar fyra sammanfogade fält.

**Åtgärd:** strykning av satsen. **Ersätt inte med ny prosa.**

## [Nice-to-have] F2 — "26 arms" är off by one

Mätt: `92fb5d4e` har **25** `LIKE ANY` i SQL (27 träffar minus två kommentarrader). Konverteringen är
25-för-25 och alltså komplett; talet är fel. Rätta i PR-body:ns enda tillåtna edit.

## Svar på sessionens frågor

**1. Kall-cache-läsningen.** K1 stänger som delta-fynd: warm 15,4/14,1 s och 11,1/11,0 s mot 47,4 s,
och reproducerar mina 14,96/17,07 s. Läsningen "kall överdrag är pre-existerande" är sund **om**
security-auditors 46,7 s togs på bas-formen. Residualen stängs ändå inte av deltat — fila den.
Arkitektoniskt korrekt remedie: `db.Database.SetCommandTimeout(n)` i Infrastructure, med precedens i
`CompanyWatchBrowseQuery.cs:80` och `CompanyRegisterSearchQuery.cs:63`. `IAppDbContext` förblir orörd.

**2. Är egenskapsformuleringen ett undanhopp?** Nej — den är den enda av kandidaterna som är mätbart
sann. Se F3.

**3. Är en request-nivå-boolean rätt form?** **Ja**, och att förfina den till "den term som vann" vore
en no-op: alla element i `terms` normaliserar till samma tiosiffriga värde. Att skanna excerptens
innehåll vore en andra detektor (#844) och ska inte byggas.

**4. Övrigt nytt arbete** — `snapshot_contacts`-pinnen, org.nr-faktumets purge, handlarens omskrivna
grund och `LikeEscape`-faktumet: alla rätt konstruerade, inga fynd.

## F3 — tillbakadragning av eget runda-1-fynd

**V1:s delmätning håller inte, och jag drar tillbaka den.** Jag skrev att `ValidateEmployerList` bor i
`SearchCriteria.cs:239` och styr `saved_searches.criteria`, inte `employer_list`. Mätt nu:
`RecentJobSearchCaptureBehavior.cs:211` anropar `SearchCriteria.Create(… employer: capt.Employer ?? [] …)`,
och `RecentJobSearch.cs:108` gör `_employer.AddRange(criteria.Employer)` → `employer_list`.
`SearchCriteria` är alltså en **delad** VO som styr båda kolumnerna. Hemmen som attribuerar kolumnen
till `ValidateEmployerList → OrganizationNumber.Create` har **rätt**; min grund
(`ListJobAdsQueryValidator.cs:22`) är en andra, seriell grind på samma väg — inte en konkurrerande.
V1:s kärna (Domain namngav `job_ads.organization_number` som normaliserande) höll och är stängd.

## Eskalering

Ingen. F1 stängs genom strykning, F2 i PR-body:ns tillåtna edit, kall-cache-residualen som en märkt
issue (`area:jobads`, `P2`, `BE`, `mvp`).
