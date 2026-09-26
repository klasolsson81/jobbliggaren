# 2026-09-06 — #1681 part 2 — security-auditor

**Fas:** #1681 del 2 (ADR 0139) — materialiserad läsväg, PR #1691
**HEAD:** `7c577203` · **Bas:** `origin/main` @ `c2d782e8` · **Branch:** `feat/1681-read-path`
**Status:** ⛔ BLOCKED
**Auktoritet:** GDPR Art. 5(1)(c), 5(1)(d), 24(1), 30(1)(c) · DPIA #456 Part D (M-D4, M-D6, R-D6) ·
ADR 0139 (Klas-beviljande 3, villkor Major 3), ADR 0120, ADR 0125 Case 2 · CLAUDE.md §5, §9.6
**Transkriberad av sessionen** — agentens charter förbjuder varje repo-effekt.

## Blockers

Inga. Ingen auth bypass, ingen PII-exponering, ingen RCE, inget GDPR-brott. Alla fyra anropsvägar in
i den om-nycklade porten är owner-scopade (mätt: `CriterionOwnerScopedLoader` ×3 +
`Where(c => c.UserId == userId)` ×1), resolvern är `AddScoped` (`Program.cs:73`), varje sats är
parameteriserad, och migrationens `Sql` är en konstant.

## Major

### 1. `MeListRead`-budgeten är inte omprissatt för en rutt som nu kör upp till 40 satser + ett grading-anrop per request — och den dominerande termen är omätt

`RateLimitingOptions.cs:177-203` · `ListCompanyWatchCriteriaQueryHandler.cs` ·
`CriterionMatchingAdSetResolver.MatchingBatchAsync`

`MeListRead` = 120/60 s. Härledningen i filen är **uteslutande request-amplifiering**
(`/oversikt` ~7 anrop/laddning, 120 ÷ 7 ≈ 17 laddningar/min). Den innehåller ingen
per-request-backendkostnadsterm alls, och denna PR lägger inte till någon. Mätt mot samma fixtur som
tvillingen: tvillingens läsning 0,166 ms p95, den nya ruttens satshalvor 21,4 ms (realistisk) /
52,5 ms (vid grinden) — ~130x resp. ~320x. Vid taket: 120 × 40 = **4 800 satser/min/användare** plus
120 grading-anrop, vardera med fan-in 4 040 (realistiskt) till 40 000 (aritmetiskt tak) mot det enda
tidigare mätta produktionsformade fan-in:et på 2 000. **Alla tre smarta-bevakningssidor** anropar
`getCompanyWatchCriteria()` — även de två detaljsidorna, som betalar hela fan-outen för en etikett.

**Krävs:** fan-in mätt mot det riktiga schemat (redan CTO:ns bindande villkor före `agents-done`)
och därefter en omprissättning som landar i `MeListRead`-härledningens eget hem — eller en ratchet.
**Signaturen på hinken hålls inne tills talet finns.**

ADR 0139 alternativ 3 lyfte sin invändning på **två** grunder. Den första — *"det finns ingen
registerjoin vid läsning kvar"* — håller, mätt och pinnad (Result 3, plan utan `company_register`-nod,
med positiv kontroll). **Den återöppnas inte.** Den andra — att läsvägen blir *"samma bundna
`GROUP BY` över `job_ads` som företagsblocket redan kör"* — håller **inte som levererat**: tvillingen
kör 2 satser, den här rutten upp till 40. Det är hinkbeslutets bärande likhet, och den är falsk.
Result 2 är uttryckligen ett **GOLV** och serien är icke-monoton, så ingen extrapolation finns.

**Höjd `PermitLimit` är inte en tillgänglig åtgärd** — samma doktrin filen redan skriver:
*"Buy the margin back by removing a call, not by raising this."*

### 2. Läsvägen har ingen åldersgräns: en gammal materialisering läses som ett färskt exakt tal

`CompanyWatchBrowseQuery.cs` (`MaterialisedGate`)

Grinden läser `state` + `criteria_fingerprint`. `materialised_at` skrivs och läses av **ingenting** i
`src/` (mätt: enda träffarna är skrivvägen, konfigurationen och docblock). Materialiseraren fångar
per-kriterium-undantag, loggar och fortsätter; en körning rapporteras som lyckad så länge ≥1
kriterium gick igenom. Ett kriterium kan alltså faila varje körning i all evighet medan ytan serverar
gamla exakta tal utan någon signal.

**Krävs:** en läs-sidig åldersgräns som degraderar till `NotMaterialised` förbi en skriven bound. Det
inför **inget femte tillstånd** — det använder det tredje — så CTO:ns slutna hierarki rörs inte.

Del-1-villkor Major 3 lyder *"Utebliven/**gammal** materialisering degraderar ärligt, aldrig till ett
tyst tal."* Del 1 löste "utebliven" (ingen rad → `NotMaterialised`). Del 2 är läsvägen och därmed
platsen där "gammal" avgörs; den halvan är inte infriad. Art. 5(1)(d), DPIA R-D6/M-D6. **Nytt fynd
mot del 2:s diff, inte en omprövning av del 1:s dom.**

⚠ **`test-writer`s M-D6-fönster är mätt och är INTE fyndet.** `ScbRegister:SyncCadenceCron` =
`"0 6 * * 6"` med `Enabled` **default false**; `CompanyWatchMaterialisation:CadenceCron` =
`"30 5 * * *"` med `Enabled` **default true**. I defaultläget ändras registrets `status` aldrig, så
materialiseringen lägger till **noll** M-D6-inaktualitet. Med synken påslagen är det tillagda fönstret
från lördagssynkens avslut till nästa 05:30 UTC — under ett dygn, en gång i veckan, ovanpå en redan
accepterad ≥7-dygnsmarginal. Det ordinarie fönstret är alltså inne i en större accepterad marginal.
Det **obundna** fallet är det som inget begränsar och inget ytar.

### 3. Resolverns docblock påstår ett försvar som diffen tog bort

`CriterionMatchingAdSetResolver.cs:39`

*"...so the criterion's SPEC is what enters here — never an id the caller has not already proven it
owns."* Under del 1 var Guid:en enbart memo-nyckel; ett fel id var en korrekthetsbugg. Under del 2 är
Guid:en **den enda DB-selektorn** för de tre ad-metoderna, så ett fel id är en läsning av en annan
användares materialiserade annonsmängd.

Ingen live IDOR (alla fyra anropsställen mätta owner-scopade). Men meningen som talar om för nästa
bidragsgivare att sömmen är säker är nu falsk på exakt den egenskap som avgör om ett anroparfel är en
no-op eller en läcka. **Stängs mekaniskt** enligt §9.6 (strykning, noll `+`-rader).

## Minor

4. **Två härledda-data-uppräkningar saknar den nya kolumnen** — `gdpr-processing-register.md:1484-1488`
   och `MappedPlaintextExposureRegistry.cs:163`. Klassificeringen är korrekt och automatisk (STEG
   1-radtestet är härlett), kategoribeskrivningen fortsatt juridiskt riktig — ingen exponering, därför
   Minor. Asymmetrin är talande: `ErasureCascadeRegistry` **uppdaterades** i samma diff.
5. **Ad-browse-portens transportgränser gick från strukturella till validator-only** —
   `BrowseAdIdsAsync` tar nu lösa `int page, int pageSize` och validerar bara `>= 1`. Överkanterna kom
   tidigare från `CompanyBrowseCriteria`s konstruktor, vars docblock argumenterar att *"An invariant
   that holds 'as long as you came in the front door' is not an invariant"*. Validatorn kapar
   fortfarande, så ingen live yta.
6. **`adsNotMaterialised` / `matchingNotMaterialised` lovar en tid systemet inte håller** — kadensen är
   dygnsvis 05:30 UTC och klausul (ii) är inte byggd, så ett kriterium skapat 06:00 UTC väntar ~23,5 h.
   Copy-domen är andras; posten här är enbart att meningen är osann.
7. **Resolverns `Scoped`-livstid är opinnad** — korrekt i dag, men del 2 höjer sprängradien: memot
   håller upp till 20 kriteriers hela ad-id-mängder plus per-användar-graderingar. Pre-existerande
   (#1656).

## Vad som kontrollerades i fingerprint-argumentet

Argumentet **håller**, kontrollerat och inte accepterat: (a) person-attribuerbarheten stämmer —
STEG 1-radtestet är härlett, kolumnen blir `PlaintextPersonalData` automatiskt; (b) klartextkoderna
ligger i `company_watch_criteria`, redan på restexponeringslistan, samma databas. Tre modeller söktes
där digesten finns men koderna inte: den korsar aldrig tråden, loggas aldrig, är ingen
auktorisationstoken. Injektiviteten verifierad **i bytesen**: kanoniserad ordinal `Distinct`+`OrderBy`,
längdprefix per axel, `` mellan koder och `` mellan fält. **En nyansering, inget fynd:**
*"adds no exposure"* är sant om **restore**-modellen som ADR 0125 Case 2 täcker — inte som allmänt
påstående.

## Praise

- Plan-testets frånvaroassertion har en **positiv kontroll** — precis den vacuous-guarantee-klass
  huset skeppat två gånger. ✓
- Fingerprint-grinden ligger **i SQL**, och `CASE`/`LEFT JOIN LATERAL` behåller tillståndsraden så
  "för bred" och "aldrig materialiserad" förblir åtskilda hela vägen ut i ACL:ens `refine`. ✓
- Inget org.nr korsar Application-gränsen på någon av de tre om-nycklade metoderna eller i
  `MatchingBatchAsync` — verifierat kolumn för kolumn. ✓

## Område 8 (supply-chain)

**Ej triggat och ej kört, deklarerat snarare än underförstått.** Diffen rör inte `package.json`,
`pnpm-lock.yaml`, `pnpm.overrides`, `ignoreGhsas`, `--audit-level`, `ignoredBuiltDependencies`,
`NuGetAudit` eller `pnpm/action-setup`.

## Eskalering till Klas — ordagrant

**(1) ADR 0139 Klas-beviljande 3 är fortsatt ÖPPET och denna PR avgör det inte.** Läsningen är
**oförändrad** av del 2: PR:en läser `company_watch_criterion_materialisations` på båda ytorna och gör
den bärande, men lagrar inget nytt om personen utöver en digest av data som redan står på listan.
**Eskaleringen utvidgas inte av agenten, och PR:en får inte läsas som att den avgjort den.**
⚠ En sak ändrar del 2 som Klas bör ha framför sig: den nya kolumnen gör att den andra tabellen nu bär
ett **härlett** värde (en digest av hennes SNI-/kommunval) och inte längre bara två tal, ett
tillståndsenum och en tidsstämpel — vilket är formen på det argument den första tabellen sattes på
listan för. Det avgör inte frågan; det gör "två poster" till ett något starkare fall. Frågan är
oförändrad: utvidgas beviljande 3 till båda tabellerna, eller konstrueras materialiseringstabellen om
så att den inte är person-attribuerbar? Ingen gradering är flyttad och ingen §9.6 (3)-acceptans är
inblandad — en Art. 24(1)-post av samma klass som ADR 0125 Case 2 redan är.

**(2) `MeListRead`-hinken (Major 1).** Om fan-in-mätningen mot det riktiga schemat landar utanför
budget är det en STOPP till Klas — CTO:n band redan det. Gränsen för vad som INTE är en utväg:
**höjd `MeListRead.PermitLimit` är ett security-auditor-beslut och beviljas inte här.** Marginalen
köps tillbaka genom att ta bort ett anrop eller smalna av fan-outen, aldrig genom att höja taket.
Klas kan överpröva; han ska då veta att det han köper är 120 requests/min mot upp till 40 satser var,
och att den dominerande termen i den kostnaden fortfarande är omätt.

**(3) Informationellt, inget beslut.** CTO:ns bindande mätning före `agents-done` (grading-fan-in mot
riktigt schema) är **inte tagen** per 2026-09-06. Det är sessionens att bära, men står här därför att
`agents-done` inte får sättas medan det är sant.
