# dotnet-architect — PR #1444 (#1435)

> Transkriberad ordagrant av ägande session. Agentens charter är read-only, så han kunde inte skriva
> filen själv. Ingen egen eskalering till Klas.

**Runda 1:** 1 Kritiskt · 2 Viktigt · 2 Nice-to-have.
**Lagergränser, DDD-mönster och registry-design håller.** Inga Clean-Architecture-överträdelser.

## Kritiskt — de nya jsonb-armarna reproducerar den written-form-lucka #1425 stängdes för att laga

`RecruiterErasureMatchQuery.cs:637` och `:661`.

`filter::text`, `match_preferences::text` och `preferences::text` söks med `LikePattern(identifier)`
— mönstret byggt på identifieraren **som den skrevs**. Båda kolumnernas write path validerar
`^[A-Za-z0-9_-]{1,32}\z` (`WatchFilterSpec.cs:79-80`, `MatchPreferences.cs:49-50`) — **shape only,
ingen normalisering**. En begäran om `5560125790` når därför aldrig ett lagrat `556012-5790`, och
tvärtom. **Fem av `WrittenForms()`:s sex former är onåbara i varje request.**

`CountJobSeekerProfilesAsync` har **ingen strukturerad nyckelarm alls**, så hela den ytan är
exponerad; på `company_watches` räddar org.nr-armen bara de rader där användaren dessutom *följer*
just det numret.

**Varför:** `OrganizationNumber.WrittenForms()`:s egen XML-doc skriver regeln generellt, inte om de
fem axlarna: *"A column validated on SHAPE ONLY stores whatever was typed … comparing a normalised
request to an unnormalised store reaches only the one form that happens to coincide (#1425)."*
PR:ens motivering — *"WrittenForms() is deliberately absent because both write paths normalise
through OrganizationNumber.Create"* — är **sann om `company_watches.organization_number` och falsk
om de tre jsonb-kolumnerna**. Registryns egen ground bär premissen ordagrant: *"Karlsson,
Anna-Karlsson and a ten-digit org.nr all persist."*

**Detta är #1425:s felmod reproducerad inuti sin egen fix** — precis det PR-texten anför som skäl att
undvika keyed unnest.

**Föreslagen åtgärd:** samma form som `FindRecentJobSearchesAsync` redan har, en disjunkt per skriven
form, via `unnest({forms})`.

**Pre-existerande tvilling:** `CountSavedSearchesAsync:509` söker `saved_searches.criteria` (samma
grammatik) med samma nakna `LIKE`. Repo-tillstånd som deltat inte skapade — **antingen med i samma
svep eller uttryckligen scope-ad ut i PR-body:n, inte tyst ärvd.**

## Viktigt 1 — "ONE channel per TABLE"-grunden är mätbart falsk i BÅDA leden

`ErasureCascadeRegistry.cs:250-255`.

1. Fyra kanaler läser redan `applications` (`RecruiterErasureMatchQuery.cs:534, 558, 577, 715`), och
   `ApplicationsReferencingMatchedAds` + `ApplicationSnapshots` **dubbelräknar rutinmässigt** samma rad.
2. ADR 0106 §Amendment 2026-07-14(d) skriver axeln själv: *"one entry per reported **surface**"*, och
   T2 CTO 2026-07-16 gav `snapshot_contacts` en egen surface på **samma tabell** just för att
   dispositionen skiljer sig.
3. `matched.Total` läses på exakt **ett** ställe — `EraseRecruiterAdsCommandHandler.cs:139`,
   `matched.Total == 0` — så en dubbelräkning kan **aldrig** vippa outcome-ordet.

**Beslutet är rätt och ska inte ändras** — båda kolumnerna är `MatchedHumanErases`, så Matched−Erased
har en enda innebörd och en surface är korrekt per ADR 0106:s egen axel. **Det är motiveringen som
ska skrivas om:** en surface per *disposition*, inte per tabell, och kostnaden är den rapporterade
siffran (Art. 15/17-svaret + Art. 5(2)-raden), inte outcome-ordet.

## Viktigt 2 — den nya facten sattes in mellan ett doc-block och dess metod

`ErasureCascadeRegistryTests.cs:952-1024`. `Every_wholesale_excluded_table_carries_a_written_ground`
står nu **helt utan dokumentation**, och den nya facten bär **två** `<summary>` och **två**
`<remarks>` — där det första paret (*"This is the cheapest false verdict in the system, and it
produced a Blocker"*) beskriver en annan fact och en historia som inte är dess egen.

AGENTS.md §5 `Comments:` — en faktiskt felaktig kommentar är en defekt och lagas. **I just den här
filen ÄR doc-blocket grunden**; en ground som sitter på fel medlem är samma felklass filen finns för
att stänga. Bygget klagar inte: `GenerateDocumentationFile` är inte satt.

**Åtgärd:** rent blockflytt, ingen ny claim-mening — stänger mekaniskt enligt §9.6.

## Nice-to-have

1. `:609-610` — `var plain = orgNr?.Value;` duplicerar helperns `NormalizedOrgNr(identifier)` (`:130`).
2. `:612-614` — kompositionen `IsPersonnummerShaped() ? Tokenize(v) : v` finns i tre former.
   `CompanyWatchFollowExecutor` är `internal` i Application, så Infrastructure **kan fysiskt inte**
   återanvända den — duplikationen är motiverad, men värd en rad som säger det.

## Svar på sessionens sex frågor

1. **Tokenizer-seamen: rätt val, inget fynd.** Infrastructure som konsumerar en Application-ägd port
   är §2.1:s normalriktning, och Singleton→Scoped är rätt håll. Alternativet vore **sämre**: det
   lyfter in en at-rest-kodning i handlern och gör porten till bärare av en persistensartefakt.
2. **Två ytor: rätt beslut, fel skäl** (Viktigt 1). **Ingen dubbelräkning skapad** — båda nya
   kanalerna är ett enda `count(*)` över OR-ade disjunkter på **en** tabell.
   `preferences`/`Language` är en registry-nyckeldubblett, inte en count-dubblett.
3. **Aritet/ordning: inget missat.** Fyra hem, alla uppdaterade. Kvarstående *pre-existerande*
   skörhet: `Total_sums_EVERY_reported_surface_and_not_a_subset` bygger sina
   `Activator.CreateInstance`-argument ur `GetProperties()`-ordning.
4. **`TextColumnsByTable()`: seamen rätt, ackumuleringen korrekt.** `Contains`-matchningen är
   fail-open för korta kolumnnamn, men **exakt samma svaghet som**
   `Every_grounded_columns_ground_actually_names_the_column` — trogen analogi, inte ny defekt.
5. **Org.nr-armen håller, jsonb-valet håller, LIKE-formen gör det inte.** Han argumenterar
   **inte** emot `::text` framför keyed unnest — *"resonemanget är rätt och
   additiv-nyckel-blindheten är den dyrare felmoden"*. Men `::text` löser **nyckelkontraktet**, inte
   **skrivformen**; två oberoende axlar.
6. **Båda soft-delete-halvorna verifierade sanna.** `SoftDelete()` sätter `DeletedAt` och
   `Filter = null` och lämnar `OrganizationNumber` stående; `UnfollowCompanyCommandHandler.cs:57` är
   enda soft-delete-vägen; inga `ExecuteUpdate` mot tabellen. Och `Filter = null` landade i **samma
   commit** som `Filter`-propertyn (`1427b30a`, #806), så det finns **inget legacy-fönster** med
   soft-deletade rader som bär filter. *"By construction" håller.*

**Eskaleringar:** inga.

---

# Omkontroll (scopad, rapport-läge, delta `fbcb04c5`)

**Kritiskt STÄNGT · Viktigt 1 STÄNGT · Viktigt 2 STÄNGT.** 1 nytt Nice-to-have i deltat.

- **Kritiskt:** `WrittenFormPatterns()` konsumeras av alla fyra armarna, pinnad av två nya facts.
  Undantaget för org.nr-nyckelarmen vilar på en premiss han **mätte**: enda skrivarna är
  `FollowCompanyCommandHandler` och `FollowCompanyFromJobAdCommandHandler`, båda via
  `OrganizationNumber.Create`. *"Rätt korrigerad, inte generaliserad."*
- **Scoping-out av `CountSavedSearchesAsync`: bekräftad** som den arm han erbjöd. #1448 mätt OPEN med
  rätt etiketter och hans formulering ordagrant. **Villkor:** skipet ska namnges i **PR-bodyn**;
  commit-bodyn räcker inte som hem.
- **Viktigt 1:** omskrivningen mätt sann **oberoende av sessionens transkribering** —
  `Matched.Total` läses på exakt två ställen, båda zero-tests.
- **Viktigt 2:** verifierad som ren flytt genom **mängdsubtraktion av diffens `-`/`+`-rader**.
- **NTH 1/2:** ej värre än det han flaggade; ingen åtgärd krävd.

**Skopnotering (ograderad):** samma falska klausul stod kvar på `ErasureCascadeRegistry.cs:201`, en rad
deltat inte rörde — deltat skapade motsägelsen genom att skriva negationen 46 rader ned. Struken
mekaniskt: sex ord, noll tillagda rader.

## Nice-to-have (ny i deltat) — `btrim` är inte trogen `#>> '{}'`

JSON-escaper står kvar och `btrim` strippar *alla* yttre citattecken. **Motiveringen var falsk:** repot
använder den dubbeldollar-prefixade raw-strängformen på tio ställen, inklusive featurens egen testfil.
Han höjde insatsen genom att notera att #1448 citerar formen som mall för fem kanaler till.
**Åtgärdat** — den valda formen binder i stället den tomma jsonb-sökvägen som parameter, vilket inte
kräver någon ändring av strängformen alls.

## Hans bedömning av query-formen

`$.**` **korrekt och totalt** — stiger ned i både objektfält och array-element, empiriskt pinnat mot
PG 18. NULL-kolumn: strict SRF ger noll rader, EXISTS falskt. **Plan-risk: ingen materiell** — inga
GIN/trigram-index finns på kolumnerna, både gamla och nya formen är seq scans, unnest ≤ 6 element,
EXISTS kortsluter. Rör inte §2.5-budgetarna. Parameterisering intakt, lagergränser orörda.

**Eskaleringar:** inga.

