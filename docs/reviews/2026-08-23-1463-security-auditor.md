# security-auditor — PR #1466 (#1463)

> Transkriberad ordagrant av ägande session. Charter är read-only och kan inte fråga Klas (§9.2);
> eskaleringen nedan är därför **inspelad**, och bärs vidare av sessionen.
>
> **Runda 1:** 0 Blockers · 2 Major · 1 Minor. Status ⛔ BLOCKED.

**Granskat HEAD:** `dc6d967a` (base `7b318483`, three-dot diff, 3 filer)
**Auktoritet:** GDPR Art. 5(1)(c), 12(2), 12(3), 17(1) · ADR 0087 D8(c) · ADR 0050 Option B ·
CLAUDE.md §5 (`Security:`, `CV & matching engines`), AGENTS.md §1.6, §9.6

## Blockers

Inga. Diffen inför ingen PII-kolumn, ingen migration, ingen ny sub-processor, ingen
SQL-semantikändring. Isolerad icke-kommentarsdiff mot produktionsfilen: primary ctor → explicit ctor
(mekanisk `db.` → `_db.`), `SetCommandTimeout`, två konstanter, `Stopwatch`-mätningen,
`WarnIfMarginConsumed` + `[LoggerMessage]`. Ingenting annat.

## Major

**1. Filen har korsat från "kan strukturellt inte logga" till "loggar, bevakad enbart av en fail-open
org.nr-blocklist" — på en Art. 17-väg vars identifierare är bredare än org.nr**

Fil: `RecruiterErasureMatchQuery.cs:1062` · `OrganizationNumberSurfacingGuardTests.cs:139,535,552`

Nuvarande: Basversionen (`git show 7b318483:…`) har **noll** `ILogger`, `_logger`, `[LoggerMessage]` —
grep ger tom mängd. Denna PR ger klassen dess **första loggyta**. Filen står på
`RawOrgNrReadingSourcePaths` (rad 139) och skannas av `FindOrgNrLoggingFragments`. Den skanningen är,
med repots egna ord på rad 542: *"a BLOCKLIST, and a blocklist is fail-open by construction: its token
list is org.nr-only, so `LogInformation("matched {CompanyName}", row.Name)` passes it GREEN"*. Filen
står **inte** på `CountsOnlyLoggingSourcePaths` (rad 552) — den fail-closed-invarianten som pinnar
"ingen loggning alls" — vilket den heller inte kunde efter denna PR.

Identifieraren här är bredare än org.nr: den registrerades namn, adress, telefon, e-post, eller ett
enskild-firma-org.nr som ÄR hennes personnummer. Blocklistan täcker **ingen** av de första fyra. Före
denna PR var det ofarligt: en fil utan `ILogger` kan inte logga. Efter den är den fail-open blocklistan
den enda stående repo-vida kontrollen på filen.

Krävs: en fail-closed loggyt-assertion över just denna fil, med `LogMarginConsumed` som enda tillåtna
anrop — exakt mönstret `AllowedBrowseLogCalls = ["LogCrossUserAttempt"]` (rad 535) redan är. Då fäller
varje *andra* loggsite i filen bygget, oavsett vad den bär.

Motivering för graden: **samma charter graderade identisk defektform Major 2026-07-13** —
transkriberat i testfilen rad 540, *"DPIA C-D5 (counts-only) — fail-CLOSED (security-auditor Major,
2026-07-13)"* — på browse-vägen, som bär strikt *mindre* känsliga data (företagsnamn, SNI/kommun). §9.6
reserverar severity till rapporterande agent; konsekvens med min egen tidigare gradering av samma form
på lägre känslighet gör att detta inte kan vara mindre än Major. Diff-orsakat: loggytan existerade inte
i basen.

**2. Förfallovillkorets avveckling namnger #196, som är STÄNGD sedan 2026-08-08 — och kommentaren är
inte lagd**

Fil: `RecruiterErasureMatchQuery.cs:99-107` · PR-body · `docs/reviews/2026-08-23-1463-senior-cto-advisor.md:76`

Nuvarande: `gh issue view 196` → `[CLOSED] closedAt=2026-08-08T17:49:46Z`. Sista kommentaren är
stängningskommentaren, 2026-08-08 — **ingen kommentar är lagd efter den**, så avvecklingen är både
olagd och, som namngiven, olagbar med läsare. CTO:ns motivering rad 76 är ordagrant *"En kommentar på
en issue som redan äger området är avveckling **med en läsare**"*. Den premissen är mätbart falsk.
PR-bodyn upprepar den i presens: *"Routed to #196 as a comment — it already owns the deploy tier"*.

Repot har redan mätt och filat exakt denna defektform: **#1298 — *"the disk-usage finding's quota half
lost its home when #196 closed — detection shipped, the limit did not"***. Detta är en återkomst, inte
en teoretisk invändning.

Kodkommentaren har en läsare — den som rör taket. Men de två aktörer som kan utlösa förfallet läser
andra filer: `src/Jobbliggaren.Api/Program.cs:38` (den som lägger till `AddRequestTimeouts`) och
`deploy/caddy/Caddyfile` (den som lägger en `/api`-matcher). Ingen av dem bär pekaren. Det gör att **den
ena av de två lapse-armar jag själv namngav i eskaleringen står oavvecklad**: korpustillväxt-armen är
avvecklad av varningen, stack-ändrings-armen är avvecklad av ingenting med läsare.

AGENTS.md §1.6: en markör som pekar på arbete som inte byggts *"is not provenance but a forward pointer
into nothing, and it shall be converted to the issue that owns that work."* §9.6: *"a line in a PR body
is not disposal because it has no reader"* — en kommentar på en issue stängd i 15 dagar är samma form.

Jag omdirigerar inte CTO:ns routing (§9.2 — routing är hans). Jag rapporterar att dess premiss är
falsk, uppmätt idag, och lämnar valet av destination tillbaka till honom. Notera att **min egen
eskalering bar samma omätta premiss** — jag skrev "vilket är #196:s område" 2026-08-23 utan att mäta
issuens tillstånd. Det är mitt fel lika mycket som hans.

## Minor

**1. Admin-gruppen saknar `RequireRateLimiting`, och hålltiden per request är nu 6× längre**
Fil: `src/Jobbliggaren.Api/Endpoints/AdminJobAdsEndpoints.cs:32-34`

Gruppen har `RequireAuthorization(AuthorizationPolicies.Admin)` men **ingen** `RequireRateLimiting` —
syskongruppen `AdminBackgroundJobsEndpoints.cs:104,120` använder `AdminWritePolicy`. `Max Pool Size` är
osatt i all produktionskonfiguration, alltså Npgsqls default 100, delad med hela appen.

Ingen privilegie-eskalering: anropet kräver admin, och en admin kan redan radera korpusen. Men
**amplifieringen är diff-orsakad** — före ändringen höll en sådan request en connection i högst 30 s, nu
180 s. Ett fastnat admin-skript kan därmed binda poolen 6× längre och slå mot resten av appen, inte bara
mot sig självt. Remedyn är en rad med precedens i repot.

## Praise

- `[LoggerMessage]`-raden bär `long elapsedMs` + `int ceilingSeconds` och inget mer; typerna stänger vad
  namnmängden pinnar, och ingen `Exception` vidarebefordras (rätt sida av #1245). ✓
- `WarnIfMarginConsumed` anropas före `if (ids.Count == 0)`, så den mäter alla utfall — inte bara
  träffar. ✓
- Att "lådans kalla fall är OMÄTT" står i kodkommentaren, commit-meddelandet, PR-bodyn och
  CTO-rapporten. Ingenstans läser det som mätt. ✓

## Svar på sessionens fem frågor

**Din inversion håller — jag verifierade den oberoende och den är korrekt.** `grep` över hela `src/`:
`AddRequestTimeouts`/`UseRequestTimeouts` finns inte; `Program.cs:38` sätter enbart
`MaxRequestBodySize`. `deploy/caddy/Caddyfile` har exakt en `handle` → `reverse_proxy web:3000`,
`write 30s`, och headern säger själv *"There is no /api matcher here on purpose"*. Endast `caddy`
publicerar portar (80/443, `compose-edge-publish-guard.sh` klausul 1).
`web/jobbliggaren-web/src/app/api/` har tolv route handlers, ingen under `admin`; `next.config.ts` har
inga rewrites mot API:t; `grep redact-recruiter-pii` över `web/src` ger noll. **Min mening från
2026-08-23 var falsk för denna route idag, och din mätning står.**

**(1) Pinnen.** Stark för *metoden*, inte för *filen*. `FieldNames.ShouldBe([…], ignoreOrder: true)` är
kollektionslikhet — varje tillagd parameter blir röd, och `long`/`int` stänger värdesidan. Men den
pinnar en metod; en framtida *andra* `[LoggerMessage]` i filen med en icke-org.nr-namngiven parameter
passerar både den och blocklistan. Det är Major 1.

**(2) Ja, 180 s avvecklar exponeringen jag reste — på tillgängligt underlag, och jag säger det utan
reservation.** Värsta mätta *slutförande* körning 63,9 s; 180 är 2,8×. Lådan går 5,4–6,1 s varm på 44 %
av dev:s korpus med 5× buffertpoolen, alltså bättre på båda mätta axlarna. Residualen jag *inte* kan
stänga: per-byte I/O-latens på lådan är omätt, och det står skrivet som omätt. **En residual till, som
ingen har namngivit och som jag inte graderar** (talen är CTO:ns): varningens runway-argument räknas i
*körningar*, och vägen körs en handfull gånger per år — korpusen kan mer än dubblas mellan två körningar
ett år isär, så varningen kan aldrig hinna fyra före första felet. Det gör inte 180 otillräckligt: felet
är högljutt, inträffar på dry run före all radering, och fördröjer ett Art. 17-svar snarare än att
korrumpera det — rätt felriktning. Men den ärliga meningen är att **varningen krymper det tysta
fönstret; den stänger det inte.**

**(3) Nej — en kommentar på #196 är inte adekvat avveckling, och skälet är starkare än en åsikt: issuen
är stängd och kommentaren är inte lagd.** Se Major 2.

**(4) Ingen DoS-yta värd Major eller Blocker.** Auth kör före handlern, `CancellationToken` propageras
end-to-end så en avbruten klient släpper connectionen, och 100 samtidiga admin-authade requests krävs
för pooltömning — en admin kan redan radera korpusen. Amplifieringen är verklig men Minor (ovan).

**(5) Övrigt i mina områden:** inget. Loggraden går till app-sinken (Seq på lådan, EU, inga publicerade
portar) och inte till audit-sinken — separationen står. `EventId 8436` är unik i `src/`. EF:s
`CommandError`-väg loggar kommandotexten men `EnableSensitiveDataLogging` är av och
`EfCoreLoggingConfigurationTests.cs:113-129` håller tripwiren armerad, så parametervärden når inte
sinken vid en timeout. Ingen residency-, secrets-, authz- eller transfer-fråga rörs.

## Sammanfattning

0 Blockers, 2 Major, 1 Minor. Båda Major är diff-orsakade och faller därmed under §9.6:s huvudregel —
in-block eller följd-PR, aldrig en issue. Ingen av dem är ett area-8-fynd, så carve-outen i
severity-tabellen gäller inte och PR:en blockeras. Remedyn för Major 1 är en testfilsändring med exakt
precedens i samma fil; Major 2 är en omrouting plus en pekare placerad där aktörerna läser. Re-review
efter fix: samma agent, report-only, scopad till fix-deltat (CLAUDE.md §9.6).

## Eskalering till Klas — ordagrant

> Lådans kalla fall för `FindJobAdsAsync` är fortfarande omätt, och det är den sista okända storheten
> under Art. 12(2)-taket. Det går inte att mäta read-only: att tömma page cache på en levande värd är
> inte en läsning, så jag har inte tagit den och sessionen har rätt i att inte ta den. **Det du behöver
> avgöra är om en kall mätning ska tas på lådan under ett underhållsfönster, och i så fall när.** Tas
> den inte, är 180 s kalibrerat mot dev — pessimistiskt på båda mätta axlarna (tabellstorlek,
> buffertpool) men med per-byte I/O-latens fortfarande ogissad — och residualen ska stå kvar skriven som
> omätt i koden, i commit-meddelandet och i PR-bodyn, precis som den gör nu. En accepterad risk blir inte
> mätt av att accepteras. Jag begär ingen ändring av talet: 180 är CTO:ns beslut och jag graderar det
> inte. Jag begär att frågan om mätningen får ett svar i stället för att förbli implicit.
>
> Två saker jag inte eskalerar, för tydlighets skull: sessionens inversion av min tidigare mening om
> request-timeouten är korrekt och verifierad oberoende, och båda Major-fynden ovan är repareringsbara
> in-block utan ditt beslut.

---

## Sessionens åtgärd

**Major 1 — STÄNGD.** `Art17_match_query_logs_nothing_but_the_margin_warning` i
`OrganizationNumberSurfacingGuardTests` håller filen fail-closed med `LogMarginConsumed` som enda
tillåtna anrop. `FindLoggingSurface` fick en `allowInjection`-flagga (default `false`, så browse-vägens
posture är byte-oförändrad) eftersom den annars fäller på själva `ILogger`-fältet.
`Logging_surface_scan_with_injection_allowed_still_flags_a_second_log_call` är den självbevisande
negativen: fältet tillåts, det tillåtna anropet tillåts, `LogInformation` fälls.

**Major 2 — STÄNGD via omrouting.** #196:s stängning verifierad av sessionen (`state: CLOSED`,
`closedAt: 2026-08-08T17:49:46Z`, sista kommentar samma sekund). `senior-cto-advisor` omroutade:
pekare i `deploy/caddy/Caddyfile` och `src/Jobbliggaren.Api/Program.cs`, ingen backlog-rad, regeln
enbart hos konstanten. Hans egen villkorsmätning togs före pekaren skrevs: den deployade
`/etc/caddy/Caddyfile` är byte-identisk med repofilen (`sha256 2781e807…` mot repofilens LF-form), så
pekaren har en läsare.

**Minor 1 — STÄNGD.** `.RequireRateLimiting(RateLimitingExtensions.AdminWritePolicy)` på
`/redact-recruiter-pii`, samma policy som syskonändpunkterna.

**Eskaleringen till Klas är INTE stängd** — den är buren vidare ordagrant i PR-bodyn och i chatten, och
är hans att besvara.

---

# Omkontroll (scoped, report-only) — delta `dc6d967a..672e3b31`

> Transkriberad ordagrant. HEAD `672e3b31`, `git status --porcelain` tomt (verifierat).
> Cap:en förbrukad efter denna.

**Status:** ⛔ BLOCKED — 0 Blockers, **1 Major (ny i deltat)**, 0 Minor.

## Mina tre fynd — avvecklade

**Major 1 (fail-open blocklist):** stängd i sak. **Ditt `allowInjection`-beslut var rätt och
nödvändigt** — `ILogger`-armen finns för att browse-vägen inte får ha någon logger alls, och den här
filen har nu legitimt en; att fälla på fältet hade gjort grinden otillfredsställbar. Default `false`, och
browse-posturen är byte-oförändrad, verifierat i diffen. **Att försvaga `ILogger`-armen på just den här
filen är alltså vad jag bad om.**

**Major 2 (#196):** stängd. Pekarna i `deploy/caddy/Caddyfile:4-10` och
`src/Jobbliggaren.Api/Program.cs:40-46` namnger symbolen, bär inget tal, ingen mätning och inget
omformulerat villkor. Caddyfile-pekaren är relativ (*"larger than the `write` timeout below"*), sann, och
kan inte drifta mot ett tal. Rätt form. Ingen backlog-rad behövdes.

**Minor 1 (rate limiting):** stängd. `AdminWritePolicy`, samma som syskonändpunkterna.

**Min residual:** inte uppmjukad. Stycket vid `MarginWarningThreshold` bär min formulering, min
attribution och tolerabilitetsskälet. Det är om något starkare än min egen mening. Godkänd.

## Major (ny i deltat)

**1. Grindens fail-closed-påstående är mätbart falskt för filens egen etablerade loggmekanism**
Fil: `OrganizationNumberSurfacingGuardTests.cs:590-591` (kommentaren) och `:1023-1031`
(`FindLoggingSurface`)

Med `allowInjection: true` är `ILogger`-armen av, så hela fail-closed-egenskapen vilar på den enda
kvarvarande armen, `\b(Log[A-Z]\w*)\s*\(`. Den är **namnform**, inte struktur. Mätt med perl mot samma
regex, tre undvikande former i en syntetisk källa: `FindLoggingSurface Log-arm hits: NONE` — för en
`[LoggerMessage]`-partial döpt `IdentifierNotFound`, för `_logger.Log(LogLevel.Information, …)` och för
`_logger.BeginScope("{Identifier}", identifier)`. Ingen av de tre fångas. Och `FindOrgNrLoggingFragments`
fångar dem inte heller: `{Identifier}` innehåller inget org.nr-token. Ett andra loggsite som bär den
registrerades **namn, adress, telefon eller e-post** passerar därmed varje kontroll i repot.

Testets egen kommentar säger: *"any SECOND log call fails the build whatever it carries."* Den meningen
är falsk.

Detta är inte en exotisk lucka. Mätt över `src/`: **39 av 178 `[LoggerMessage]`-partialer är inte
`Log`-prefixade** — 22 %. Konventionen grinden tyst vilar på är ingen konvention. Och filens **enda**
loggsite är just en `[LoggerMessage]`-partial, så den oskyddade formen är den lokala idiomatiken.

Krävs: antingen (a) **strykning** av den falska meningen, eller (b) **kodändring** — en arm som fäller
varje `[LoggerMessage]`-attribuerad deklaration vars metodnamn inte står i `allowed`, plus
`\b_logger\s*\.`, stängd genom att köra om mätningen ovan. Jag föreskriver inte vilken.

Grad: en kontroll som **läses** som fail-closed och inte är det är sämre än en känt partiell, eftersom
nästa läsare litar på den — repots egen formulering i samma fil: *"a test that passes because it checks
nothing, which is the exact vacuity this file exists to fight."*

## Praise

- `ErasurePortInjectionRadiusTests` pinnar radie-premissen som konstruktorplaceringen vilar på — den var
  oskriven och obevakad i runda 1. Rätt fynd, inte mitt. ✓
- Tröskeltestet korsar nu 89/90 s exakt och asserterar `EventId 8436`; `{OriginalFormat}` filtreras
  explicit och namngivet. ✓
- `statement_timeout = 0` är omskrivet till *"the dev server's … measured at 0 the same day"* — en
  korrekt inskränkning ingen bad om. ✓

## Sammanfattning

1 Major, ny i deltat, inget annat. Mina tre ursprungliga fynd är avvecklade i sak och två av
avvecklingarna är bättre än det jag föreskrev. Cap:en är förbrukad: ett new-in-delta-fynd stängs genom
**strykning** eller genom en **kodändring stängd av att fyndets egen mätning körs om**. Överlever fyndet
båda vägarna → STOPP till Klas och `blocked`-etikett.

## Eskalering till Klas — oförändrad, fortfarande obesvarad

> Lådans kalla fall för `FindJobAdsAsync` är fortfarande omätt, och det är den sista okända storheten
> under Art. 12(2)-taket. Det går inte att mäta read-only: att tömma page cache på en levande värd är
> inte en läsning, så jag har inte tagit den och sessionen har rätt i att inte ta den. **Det du behöver
> avgöra är om en kall mätning ska tas på lådan under ett underhållsfönster, och i så fall när.** Tas
> den inte, är 180 s kalibrerat mot dev — pessimistiskt på båda mätta axlarna (tabellstorlek,
> buffertpool) men med per-byte I/O-latens fortfarande ogissad — och residualen ska stå kvar skriven som
> omätt i koden, i commit-meddelandet och i PR-bodyn, precis som den gör nu. En accepterad risk blir inte
> mätt av att accepteras. Jag begär ingen ändring av talet: 180 är CTO:ns beslut och jag graderar det
> inte. Jag begär att frågan om mätningen får ett svar i stället för att förbli implicit.
>
> Två saker jag inte eskalerar, för tydlighets skull: sessionens inversion av min tidigare mening om
> request-timeouten är korrekt och verifierad oberoende, och båda Major-fynden ovan är repareringsbara
> in-block utan ditt beslut.

## Sessionens åtgärd

**Major (ny i deltat) — STÄNGD via BÅDA vägarna, eftersom de stänger olika halvor.**

- **Kodändring:** `FindLoggingSurface` har nu två armar till — en som fäller varje
  `[LoggerMessage]`-attribuerad deklaration vars metodnamn inte står i `allowed`, och en på
  `\b_logger\s*\.` — så de tre former du mätte stängs **strukturellt** i stället för av namnkonvention.
- **Strykning:** meningen *"any SECOND log call fails the build whatever it carries"* är raderad. Grinden
  påstår inte längre mer än den levererar.
- **Fyndets egen mätning är körd om, som gate:** dina tre undvikande former är nu en `[Theory]` med tre
  `InlineData` (`Logging_surface_scan_flags_the_three_forms_that_evade_the_name_shaped_arm`), och hela
  klassen är grön — `total: 23, failed: 0`. Ingen ny omkontroll är skyldig, och inget STOPP behövs.

**Din eskalering är INTE stängd** — den är buren vidare ordagrant till Klas, i PR-bodyn och i chatten.
