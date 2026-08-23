# security-auditor — PR #1444 (#1435, de två helhetsundantagen ur Art. 17-registret)

> Transkriberad ordagrant av ägande session. Agentens charter förbjuder repo-writes, så hon kunde
> inte skriva filen själv. Eskaleringsavsnittet är oförkortat.

**Status runda 1:** ⛔ **BLOCKED** — 1 Blocker · 1 Major (repo-tillstånd, ej blockerande) · 5 Minor.
**Auktoritet:** GDPR Art. 4(1), 5(1)(a)/(c)/(d)/(f), 6(1), 12(1)/12(3)/12(4), 15, 17, 24(1) ·
ADR 0106 · ADR 0090 D5 · ADR 0087 D8(b)/D8(c) · AGENTS.md §5 (`Comments:`, `Tests:`) ·
CLAUDE.md §9.6/§12.
**Mätt själv:** arkitektursviten 517/0 i worktreet (verifierar PR-bodyns siffra). Integrationssviten
kördes inte (kräver Docker).

## Blocker 1 — B2-grinden avfyras på `matched.jobSeekerProfiles`

`docs/runbooks/recruiter-pii-erasure.md:370-372` och `:421-434`.

Grinden är imperativ (*"the reply **must** disclose it — template B2"*) och listar
`matched.jobSeekerProfiles > 0`. B2:s brödtext säger *"Dina uppgifter förekommer också i innehåll
som användare själva har skrivit eller valt … uppgifter i en användares egen profil … De uppgifterna
tas bort manuellt … Vi hör av oss när det är klart."*

**Mätt:** `job_seekers.display_name` är kontoinnehavarens EGET namn. `RegisterCommandHandler.cs:113`
matar in registreringsformulärets `DisplayName`; `settings.json:174-175` etiketterar det
*"Visningsnamn — Namnet som visas i appen och på dina ansökningar"*; integritetspolicyn listar det
under *"identifiera dig i tjänsten"*. En träff är därför **presumtivt** "en av våra användare heter
så", inte "en användare skrev hennes namn någonstans".

Konsekvenser av att ändå skicka B2:
- **Art. 4(1)/5(1)(d)/12(1):** "Dina uppgifter" är falskt.
- **Art. 12(1)/12(4):** raderingslöftet kan inte hållas — registrets egen ground säger *"a system
  does not rename a person"*.
- **Art. 5(1)(f)/6(1):** för ett ovanligt namn är B2 en skriftlig bekräftelse till en utomstående att
  ett konto finns under hennes namn. **Samma account-existence-oracle som #1349 stängde i går**
  (`4c8ad6e1`), genom en annan dörr.

⚠ Runbooken motsäger sig själv: §3-raden säger *"do not write the reply as though a match here were
a finding about her"*, och grinden tre skärmar ned gör exakt det obligatoriskt.

**Krävs:** `matched.jobSeekerProfiles` får inte vara en ovillkorlig B2-trigger. Ren strykning räcker
inte — vid `AdsErased` + kollision blir ytan onämnd, vilket bryter grindens egen princip. Outcome E
bär redan rätt form (*"Granskningen visade att träffarna inte avser dig"*); bygg grenen där.

⚠ **Klassificeringen `MatchedHumanErases` är RÄTT och hon signerar sökningen.** Defekten sitter i
svarsgrinden ovanpå den.

## Major 1 — de fem `::text`-LIKE-kanalerna matchar bara identifierarens SKRIVNA form

`RecruiterErasureMatchQuery.cs:640, 662-664` (nya) samt `:510-511`, `:599` (befintliga).

`LikePattern(identifier)` byggs på råsträngen. Ett personnummer lagrat som `5512181234` nås inte av
begäran `551218-1234`, och tvärtom — exakt asymmetrin `WrittenForms()` byggdes för i #1425, och den
används i samma fil 250 rader upp.

**Mätt som befintligt vid `55bb85e6`:** `CountSavedSearchesAsync` (`criteria::text`) och
`CountCompanyWatchCriteriaAsync` (`label`) har identisk lucka. Deltat **reproducerar husmönstret**,
det skapar det inte.

**Hon blockerar INTE på detta**, och namnger varför: hennes charter ger REPO-vs-diff-carve-out endast
för audit-område 8; detta är område 1/4. Per §9.6 (*"Where a charter is ambiguous about its own
outcome, §9.6 does not pick a side"*) eskalerar hon rutten till Klas. Hennes yrkande: PR:en går
igenom, fyndet filas som issue över **alla fem** kanaler.

⚠ **`dotnet-architect` graderade samma sak Kritiskt** i samma runda. Severity tillhör rapportören
(§9.6) och graderas inte om — de tre nya armarna lagas därför in-block på hans grad, och de två
förbefintliga kanalerna filas på hennes.

## Minor

1. `IRecruiterErasureMatchQuery.cs:213-216` — *"both write paths normalise through
   `OrganizationNumber.Create`"* är sant om **nyckelarmen**, falskt om `filter`-armen i samma metod.
   Samma defektform som PR:en fixar. Stryk eller scopa meningen till nyckelarmen.
2. `RecruiterErasureMatchQuery.cs:655-657` — *"the account flow HARD-deletes the row"* är **faktafel**:
   `AccountHardDeleter.cs:135-147` har ett 30-dagars soft-delete-fönster. Beteendet är rätt, skälet
   falskt. Andra meningen är det korrekta och tillräckliga skälet — fixen är en **strykning**.
3. `ErasureCascadeRegistryTests.cs:1003-1013` — vaktens `ground.Contains(column)` är ren substring;
   fail-open för generiska kolumnnamn ("status", "state", "text" finns redan i grunderna). **Inget
   nuvarande fall är vakuöst** — hon läste alla sex grunder mot EF-konfigurationen. Prospektivt.
4. `ErasureCascadeRegistryTests.cs:952-967` — XML-doc-blocket för
   `Every_wholesale_excluded_table_carries_a_written_ground` blev föräldralöst. (Samma fynd som
   `dotnet-architect` Viktigt 2 och `test-writer` Minor 2.)
5. Token-armens tysta nolla är dokumenterad **enbart i PR-bodyn**, som saknar läsare efter merge.

## Praise (urval)

- Grunderna är re-deriverade mot **mappningen**, inte docstringen; alla fyra omskrivna
  wholesale-grunder och båda nya kolumnklasserna stämmer mot EF-konfigurationen.
  `matched_skill_concept_ids` verifierad hela vägen till `MatchScorer.CoveredSkillConceptIds` ✓
- `SoftDelete()`-asymmetrin skriven och asserterad **per arm** i stället för kopierad ✓
- Den riktiga tokeniseraren i integrationssviten stänger AGENTS.md §5 `Tests:` korrekt ✓
- Oracle-mitigeringarna håller vid mätning; `EnableSensitiveDataLogging` har noll träffar i repot ✓

## Svar på sessionens sex frågor (urval)

**1. `display_name`:** klassificeringen är **tvingad** och hon signerar sökningen. Men risken vändes
bakåt i PR-bodyn: det **vanliga** namnet är brus, det **ovanliga** är röjandet.
**2. Oraklet:** acceptabelt. Ny egenskap värd att skriva ner: `Tokenize` körs för första gången på
**operatörsstyrd** indata.
**3. Pepper-driften:** disclosure räcker inte; hon vill ha ett **pepper-fingerprint**
(`HMAC(pepper, fast publik sentinel)`) jämfört av den befintliga `ValidateOnStart`-validatorn.
Graderad **Minor**, egen change-reason, inte in-block.
**6. Mall C var FALSK före denna PR.** Mätt: vid `55bb85e6` innehöll `RecruiterErasureMatchQuery.cs`
**noll** förekomster av `company_watches`, medan mall C sa *"Vi har sökt igenom … bevakningar …"* —
och produktens egen svenska kallar **båda** sakerna bevakning. Repo-tillstånd deltat inte skapade,
och **deltat lagar det**; ingen accepted-risk-väg behövs.

## ESKALERING TILL KLAS — tre punkter, ordagrant

> **(1)** Har reply-mall C i `docs/runbooks/recruiter-pii-erasure.md` någonsin skickats till en
> registrerad? Vid `55bb85e6` påstod den att vi sökt igenom "bevakningar" medan `company_watches` var
> helt osökt, och produktens egen svenska kallar företagsbevakningar för bevakningar. Har den
> skickats finns en rättelseplikt enligt Art. 12(1); har den inte skickats stängs saken av denna PR
> och inget mer behövs. Detta är ett faktum om drift som ingen agent kan mäta.
>
> **(2)** Min charter ger uttrycklig REPO-vs-diff-carve-out endast för audit-område 8. Fyndet
> "Major 1" (skriven-form-luckan i de fem `::text`-LIKE-kanalerna) är område 1/4, är repo-tillstånd
> som deltat inte skapade, och deltat följer husets befintliga mönster. Jag graderar det Major och
> yrkar att PR:en INTE hålls på det, utan att det filas som issue. Per CLAUDE.md §9.6 avgör §9.6 inte
> en charter-tvetydighet — den lämnar den till charterns ägare. Bekräfta rutten, eller säg att en
> Major utanför område 8 alltid blockerar.
>
> **(3)** Jag vill ha en boot-time pepper-fingerprint-kontroll för `CompanyWatchPseudonymization`
> (`HMAC(pepper, fast publik sentinel)`, jämförd av den befintliga `ValidateOnStart`-validatorn).
> Utan den är en driftad pepper en tyst nolla på en Art. 17-begäran, utan användarsynligt symptom.
> Jag graderar den Minor och kräver den inte in-block — men jag vill att beslutet att skjuta upp den
> är ditt och skrivet ner, inte mitt och underförstått.

## Hennes egen slutnot

> `Language`-frånvaron är nu en **load-bearing** premiss i en levande registerground
> (`job_seekers:MatchedHumanErases`), så den issuen får inte tappas: stängs den utan att grunden
> uppdateras blir grunden falsk i samma ögonblick.

*(Den issuen är [#1446](https://github.com/klasolsson81/jobbliggaren/issues/1446); kravet är inskrivet
där av sessionen.)*

---

# Omkontroll (scopad, rapport-läge, delta `fbcb04c5`)

**Status:** ⛔ BLOCKED vid rapporttillfället — 1 Major NY i deltat. Samtliga sju runda-1-fynd bedömda.

| Runda-1-fynd | Utfall |
|---|---|
| Blocker 1 — B2-grinden | **STÄNGD.** B5 gör inget ägarskapspåstående (*"Det du har uppgett"*, inte *"dina uppgifter"*) och lovar ingen radering. §3-grinden säger uttryckligen vad B5 finns för. |
| Major 1 — skriven form | **DELVIS STÄNGD, uppdelningen är den hon begärde.** Tre armar lagade; två pre-existerande kanaler filade i #1448 med hennes gradering och eskalering ordagrant. **Godkänd rutt.** |
| Minor 1 — portdokets påstående | **STÄNGD** — skopat till nyckelarmen. |
| Minor 2 — falsk `AccountHardDeleter`-kommentar | **STÄNGD** — greppar till noll, tillståndet pinnat av `A_SOFT_DELETED_profile_is_still_reported`. |
| Minor 3 — substring-vakten | **Filad, korrekt rutt** (#1449). |
| Minor 4 — föräldralöst doc-block | **STÄNGD**, ren flytt. |
| Minor 5 — pepper-driften | **INTE LEVERERAD** vid rapporttillfället: rapportfilen var gitignorerad OCH otrackad. |

**Residual hon accepterar uttryckligen:** B5 röjer fortfarande *existensen* av en profilträff för ett
ovanligt namn. *"Det är samma orakel B2 bar, inte ett vidgat — och att undanhålla en träff vi hittat
vore sämre Art. 12(1)/15(1)-transparens."*

## Major (ny i deltat) — mall D:s uppräkning saknar B5

`recruiter-pii-erasure.md:498`. Parentesen var komplett före deltat; B5 gjorde den ofullständig.
Runbooken definierar felet själv: *"A matched surface the reply never mentions is a search whose
result never reached her"*, med registrerat prejudikat (`resumeMetadata`, round-5 B5-3.4).
Art. 12(1)/15(1). **Stängd genom ren strykning av hela parentesen** — §3-grinden är auktoriteten och
den obligatoriska avslutningen är redan deklarerad *"appended to EVERY reply"*.

## Minor (ny i deltat) — strängtypsfiltret mot "any additive key"-löftet

Tre hem lovade att en tillagd nyckel täcks; `jsonb_typeof = 'string'` utesluter tal och booleaner.
**Ingen levande under-match** — hon mätte samtliga properties i de tre dokumenten (fem bools, en enum,
två `int?` bundna 0–70; ingen kan bära en identifierare). **Stängd genom strykning av
löftessatserna** i fyra hem (hon namngav tre; det fjärde var `ErasureCascadeRegistry`).

## Hennes angrepp på värdevandringen — inget nytt hål

Consent-timestamps nås nu som strängvärden och är ofarliga: ingen av `WrittenForms()`:s sex former kan
förekomma i en ISO-8601-sträng. `PreferredOccupationExperience` ger ingen ny exponering —
`ConceptId` står redan i `PreferredOccupationGroups` via subset-invarianten, `Years` filtreras bort.
**Nyckelnamnsdefekten är stängd på de tre nya armarna**, pinnad från båda hållen.

## ESKALERING — hennes tre punkter står OFÖRÄNDRADE, och leveransen var själv ett fynd

Hon fällde leveransen: rapporten var gitignorerad (`.gitignore:158`) och otrackad, i ett worktree som
ska reapas, medan 242 systerrapporter i samma katalog ÄR trackade. Åtgärdat: filen promotad med
`git add -f`, och de tre punkterna står ordagrant i PR-bodyn.

