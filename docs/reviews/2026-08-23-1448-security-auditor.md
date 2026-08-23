# security-auditor — PR #1458 (#1448)

> Transkriberad ordagrant av ägande session. Agentens charter förbjuder varje repo-skrivning
> (`Write`/`Edit`/commit/push) — hon mäter och rapporterar, sessionen transkriberar per
> CLAUDE.md §9.2. **Eskaleringen till Klas står ordagrant sist och är promotad med
> `git add -f`**, eftersom `docs/reviews/` är gitignorerad.

**Status runda 1:** ⛔ **BLOCKED** — 1 Blocker · 1 Major · 3 Minor.
**Auktoritet:** GDPR Art. 12(2), 12(3), 17(1), 5(1)(d), 5(2), 32 · ADR 0087 D8(c) · ADR 0106 D8 ·
ADR 0032 §9 (precedent) · CLAUDE.md §5, §9.6 · AGENTS.md §2.5.

## Blocker

### 1. Art. 17-dry-runen överskrider Npgsqls 30 s command timeout — rättighetsmekanismen slutar exekvera

Fil: `RecruiterErasureMatchQuery.cs:137-200` (orsak) · `DependencyInjection.cs:1138-1145` (bindande gräns).

**Nuvarande:** `AddDbContext<AppDbContext>` sätter **ingen** `CommandTimeout`. Mätt 2026-08-23:
`SetCommandTimeout`/`CommandTimeout(` finns ingenstans på `AppDbContext` (endast
`MigrationsOptionsFactory.cs:35` = 600 s och tre råa `NpgsqlCommand`-siter). Serverns
`statement_timeout` = `0`. Bindande gräns är Npgsqls klientdefault **30 s** — samma tal
`ScbCompanyRegisterStore.cs:29` redan namnger.

Egen mätning, dev-korpus 106 071 ads, samma identifierare `5509281234`, literal-inlinead SQL:

| | cold | warm |
|---|---|---|
| gammal form | 46,7 s | **6,7 s** |
| ny form | 170,2 s | **127,6 s** |

Mina warm-tal är ~2,7× PR-kroppens (47,4 s) — jag körde literaler, koden kör parametrar, och min
låda hade last. **Riktningen och tröskelpassagen är oberoende av det:** gammal warm 6,7 s ligger
bekvämt inom 30 s, ny warm ligger utanför i båda mätningarna.

**Krävs:** att `FindJobAdsAsync` kan slutföras inom den timeout som faktiskt gäller — antingen
genom att `raw_payload`-vandringen görs billig, eller genom en explicit, motiverad timeout på
just denna query **plus** en mätning mot request-timeouten i ledet utanför ASP.NET. En höjd
`CommandTimeout` ensam flyttar felet uppåt, den tar inte bort det.

**Motivering:** detta är inte en prestandagradient utan en tröskel. Kommandot är den enda
implementerade Art. 17-vägen för rekryterar-PII, och dess dry run är den enda mänskliga grinden
före en irreversibel radering. En `NpgsqlException` gör både Art. 17(1) och Art. 12(3) omöjliga
att uppfylla via produktens egen mekanism — Art. 12(2) är dessutom absolut. Datat är verkligt:
**51 347 kontaktposter över 40 983 annonser**, namngivna rekryterare. **Repot har redan
avvecklat en admin-endpoint för exakt detta felläge** — `AdminJobAdsEndpoints.cs:35-43`,
ADR 0032 §9-amendment: *"Endpointen körde snapshot synkront i requesten → ALB-timeout vid ~47k
upserts."* Samma fil, samma grupp, samma synkrona form.

**Detta är inte samma fynd som prestandaregressionen.** Severity tillhör rapportören (§9.6):
`dotnet-architect` graderar kostnaden i sin skala, jag graderar rättighetstillgängligheten i min.
En accepterad prestandaregression stänger inte denna.

**Delegera till:** dotnet-architect (query/timeout), senior-cto-advisor (routing mot #196).

## Major

### 1. Written-form-vidgningen förstorar ytan där ett personnummer-format värde surfas UN-FLAGGED i `MatchedExcerpt`

Fil: `RecruiterErasureMatchQuery.cs:278-330` (`Evidence`), jfr `:255-265` (`OrgNrEvidence`).

**Nuvarande:** `OrgNrEvidence` flaggar korrekt på kanalen `OrganizationNumber`. `Evidence()`
flaggar **inte** — `Description`, `Title`, `CompanyName` och `ContactsMatch` returnerar rå text.
Samma PR vidgar just de fyra kanalerna från en pattern till sex written forms.

Mätt 2026-08-23: `description` bär **34** hyphenerade och **922** bara 10-siffriga former, varav
**3** resp. **395** annonser är personnummer-formade enligt ADR 0087:s egen heuristik. Av de
hyphenerade träffarna är **4 av 4 inte** också fångade av den flaggade `organization_number`-armen
— de går genom `Description` med **oflaggad** excerpt. `title`: 1 ad. `company_name`: 0.

**Krävs:** samma flaggning på `Evidence()`-kanalerna som `OrgNrEvidence` redan har — eller en
skriven, signerad grund för varför just de fyra är undantagna.

**Motivering:** ADR 0087 D8(c), ordagrant: *"sole-prop (personnummer-shaped) org.nr is
flagged/masked/excluded in **any display projection** … Safe default on detection-uncertainty:
treat-as-sensitive (flag/mask), never surface."* Klassen är pre-existerande, men **populationen
förstoras av denna diff**, och arkitekturvakten `OrganizationNumberSurfacingGuardTests.cs:215-270`
når inte hit: den klassificerar typer på org.nr-*medlemmar*, och `MatchedExcerpt` är ett
fritextfält.

**Inte Blocker:** ytan är admin-autentiserad, inget loggas, och värdet är den registrerades egen
inskickade identifierare.

## Minor

1. **`OrgNrEvidence`s flaggningsgrind fail:ar ÖPPET på en null-parse** — `:260-264`.
   `TryFromWrittenForm(storedForm)?.IsPersonnummerShaped() == true` ger `false` när parsningen
   returnerar null — oflaggat vid osäkerhet, tvärtemot D8(c):s treat-as-sensitive-default. Idag
   onåbart (alla sex former round-trippar). Det är polariteten som är fel, inte utfallet.
2. **Runbooken inför ett falskt påstående om vilka kolumner som normaliserar** — nytt stycke i
   `bbf855ff`: *"`company_watches.organization_number` is the only column on the first side."*
   Falskt. `recent_job_searches.employer_list` ligger också där. **Den borttagna prosan namngav
   `employer_list` korrekt.** PR:en ersätter en sann lista med en falsk i samma andetag som den
   argumenterar att listor blir inaktuella.
3. **Tre påståenden som diffen lämnar inaktuella** — (a) `recruiter-pii-erasure.md:253-255`
   *"the **normalised** org.nr as the excerpt"*; (b) `EraseRecruiterAdsResponse.cs:82` samma sak
   på det publika API-kontraktet; (c) `EraseRecruiterAdsCommandHandler.cs:213` *"nothing of any
   USER'S is destroyed"*, som PR-kroppens egen F1 fastslår är falsk och lämnar stående — den enda
   nedskrivna grunden för att den enda destruktiva armen saknar bekräftelsegrind (Art. 5(2)).
   Alla tre stängs mekaniskt genom **strykning**.

## Svar på de fem frågorna

**1 — Disclosure direction: ingen ny under-match.** Mätt, inte resonerat. `$.**` når rot-objekt,
rot-array och rot-skalär, och stiger ned i arrayer. Typprediktatet tappar bara containers;
JSON-`null` avkodas till SQL NULL och matchar aldrig. Nyckelnamnen är slutna mängder: **36** i
`raw_payload`, **5** i `contacts`, konverter-drivna i övriga — **inga dynamiska,
användarlevererade nycklar någonstans**.

Delta-mätning, `job_ads.contacts`, hela korpusen:

| identifierare | gammal | ny | förlorade | *därav med värdeträff* |
|---|---|---|---|---|
| `%anna%` | 1 966 | 1 966 | 0 | **0** |
| `%karlsson%` | 356 | 356 | 0 | **0** |
| `%name%` · `%role%` · `%email%` · `%phone%` · `%origin%` | 27 160 | 1–3 | ~27 157 | **0** |
| `%clare%` | 16 999 | 10 | 16 989 | 16 989 (samtliga `Origin`) |

`raw_payload`, 5 000 samplade: `%anna%` 2 063→2 063, `%karlsson%` 25→25, **noll förlust**; hela
förlusten ligger på nyckelnamnsformade identifierare.

**2 — Den destruktiva armen.** Prediktatet är materiellt sundare. **Origin-residualen är korrekt
disclosed, inte bortcertifierad** — mätt: över 40 983 annonser förekommer
`declared`/`extractedfrombody` **51 347 gånger och uteslutande under nyckeln `Origin`** — noll
`Name`/`Role`/`Email`/`Phone`. Residualen är verklig i princip och tom i mätning. **Vad som
kvarstår och inte är nytt:** armen är fortfarande substring-matchning med fyra teckens golv utan
per-id-bekräftelse; **9 859 av 31 125** `Phone`-värden bär en bar 10-siffrig sekvens.
Pre-existerande — men handlarens skrivna grund för att grinden saknas är fortfarande falsk (Minor 3c).

**3 — Evidensvägen.** Bytet från `FromTrusted` till `TryFromWrittenForm` är **korrekt och
nödvändigt**. Alla sex written forms round-trippar. **Svaret på frågan är ändå nej i det
generella fallet:** de fyra `Evidence()`-kanalerna når admin-skärmen oflaggade — Major 1.

**4 — Ja, populationen förstoras.** Se Major 1.

**5 — Ingen Art. 12(1)-konsekvens av registry-texten.** `UnsearchableSurfaces.FromRegistry()`
byggs ur `HeldButNotSearchable`-mängden — de tre registry-redigeringarna når aldrig den
registrerade. Runbook-ändringarna är operatörsvända. Vad som återstår är accuracy-defekter i
ansvarsdokumentationen (Art. 5(2)), graderade Minor 2 och 3.

## Praise

- Nyckelnamnssmalningen är en **mätt nollförlust** på värdeaxeln ✓
- Ingen `[LoggerMessage]` bär identifierare eller excerpt ✓
- Origin-residualen namngiven som ⚠ RESIDUAL och härledd ur `Enum.GetNames` ✓

## Eskalering till Klas — ordagrant

**(1) Art. 17-kommandot slutar exekvera på verklig korpus efter denna PR.** Mätt 2026-08-23 mot
dev-korpusens 106 071 annonser: samma query, samma identifierare, gammal form 6,7 s warm — ny
form 47,4 s (PR-författarens parametriserade mätning) respektive 127,6 s (min literal-inlineade).
Bindande gräns är Npgsqls klientdefault på 30 sekunder: `AppDbContext` sätter ingen
`CommandTimeout` någonstans, och serverns `statement_timeout` är 0. Konsekvensen är inte
långsamhet utan otillgänglighet — den obligatoriska dry-runen, som är den enda mänskliga grinden
före en oåterkallelig radering och den enda vägen att alls besvara en Art. 12(3)-begäran, kastar
`NpgsqlException` i stället för att svara. Vi håller 51 347 verkliga kontaktposter över 40 983
annonser med namngivna rekryterare. Repot har redan avvecklat en admin-endpoint för precis detta
felläge, i samma fil, av samma skäl (ADR 0032 §9-amendment, `AdminJobAdsEndpoints.cs:35-43`:
*"Endpointen körde snapshot synkront i requesten → ALB-timeout"*). **Att acceptera
prestandaregressionen som prestanda stänger inte detta fynd** — severity tillhör rapportören, och
jag rapporterar detta som en rättighetstillgänglighetsblocker, inte som en kostnad. Det du behöver
avgöra är vilken väg som tas: göra `raw_payload`-vandringen billig, eller sätta en explicit och
motiverad timeout på just denna query och samtidigt mäta request-timeouten i ledet utanför
ASP.NET (#196). En höjd `CommandTimeout` ensam flyttar felet uppåt i stacken, den tar inte bort det.

**(2) ADR 0087 D8(c) på de fyra fritextkanalerna.** `Evidence()` returnerar `Description`-,
`Title`-, `CompanyName`- och `ContactsMatch`-excerpter helt oflaggade, medan `OrgNrEvidence` i
samma klass flaggar korrekt. Denna PR vidgar just de fyra till sex written forms, och jag har mätt
att 4 av 4 hyphenerade personnummer-formade träffar i `description` går genom `Description`-kanalen
utan att också fångas av den flaggade org.nr-armen. D8(c) är skriven absolut — *"flagged/masked/
excluded in any display projection"*, *"even to the admin operator, even when the subject herself
supplied it"* — och arkitekturvakten når inte hit eftersom `MatchedExcerpt` är ett fritextfält,
inte en org.nr-medlem. Antingen flaggas de fyra som den femte redan är, eller så behöver undantaget
en skriven grund. Jag graderar det Major och inte Blocker eftersom ytan är admin-autentiserad,
ingenting loggas och värdet är den registrerades eget inskickade värde — men Major blockerar, och
jag skriver inte under på ett undantag jag inte fått se motiverat.

**(3) Runbooken påstår nu något som inte är sant, och det påståendet är nytt i denna PR.** Meningen
*"`company_watches.organization_number` is the only column on the first side"* är falsk:
`recent_job_searches.employer_list` normaliserar också på skrivvägen och matchas på den
normaliserade formen ensam — vilket klassens egen kommentar säger uttryckligen, och vilket den
*borttagna* prosan namngav korrekt. Detta är värt din uppmärksamhet mindre för sin egen skull än
för formen: PR:en argumenterar att uppräkningar i prosa blir inaktuella och flyttar dem till koden,
och skriver i samma stycke en ny uppräkning i prosa som är fel från dag ett. Tillsammans med de tre
påståenden diffen lämnar inaktuella — runbookens *"normalised org.nr as the excerpt"*,
DTO-kontraktets *"the normalised org.nr that matched"*, och handlarens *"nothing of any USER'S is
destroyed"* som PR-kroppens egen F1 fastslår är falsk och ändå lämnar stående — är det fyra osanna
meningar i ansvarsdokumentationen för ett Art. 17-förfarande. Samtliga stängs genom strykning, inte
genom ny prosa.

---

# Omkontroll (scoped, report-only) — delta `bbf855ff..92fb5d4e`

> Transkriberad ordagrant av ägande session (charter förbjuder repo-skrivning).

**Status:** ✓ **Approved** — 0 Blocker / 0 Major **i deltat**. Ett Major på repo-tillstånd som
deltat inte skapade rapporteras nedan och blockerar inte (§9.6 undantag (1), deklarerat av
rapporterande charter).

| # | Fynd (runda 1) | Verdikt |
|---|---|---|
| **B1** | Art. 17-dry-runen överskrider 30 s command timeout | ✅ **CLOSED** — 38,7/41,3 s → 14,9/16,2 s warm |
| **M1** | Fyra fritextkanaler surfar personnummer-format UN-FLAGGED | ✅ **CLOSED** — flaggan når alla fem kanaler, pinnad av två tester |
| **m1** | `OrgNrEvidence`-polariteten fail:ar öppet | ✅ **CLOSED** — grinden strukturellt upplöst |
| **m2** | Runbookens falska "only column on the first side" | ✅ **CLOSED** — struken i alla tre hem |
| **m3a** | Runbookens "normalised org.nr as the excerpt" | ✅ **CLOSED** |
| **m3b** | DTO-kontraktets "the normalised org.nr that matched" | ⚠️ **PARTIALLY CLOSED** — fjärde hem kvar |
| **m3c** | Handlarens "nothing of any USER'S is destroyed" | ✅ **CLOSED** (stängd genom omskrivning, se anmärkning) |
| **NY** | Cold-cache-överskridandet | 🔶 **Major, repo-tillstånd** — filas, blockerar inte |

## Egna mätningar (dev-korpus 106 071 ads, `job_ads` 1 761 MB, `shared_buffers` 128 MB)

**1. `LIKE ANY` ≡ `LIKE … ESCAPE '\'`** — 16 värden × 8 patterns, PG 18.3: **noll oenigheter**.
Negativa kontroller faller rätt. Och `LIKE ANY(…) ESCAPE` är ett **syntaxfel** på servern —
kommentarens bärande påstående är verifierat, inte antaget.

**2. Timing, samma warm cache, alternerande körningar:**

| form | warm 1 | warm 2 | shared hit | physical read |
|---|---|---|---|---|
| pre-PR-basen (`::text`, 1 pattern) | 4,25 s | 4,08 s | 870 958 | 319 662 |
| **formen jag blockerade** | **38,74 s** | **41,28 s** | 2 811 136 | 322 003 |
| **fixen** (`LIKE ANY`, 6 patterns) | **14,88 s** | **16,24 s** | 871 264 | 319 270 |

## Major 1 — cold-cache-överskridandet är repo-tillstånd, inte deltats

**`physical read` är invariant över alla tre formerna** (319 662 / 322 003 / 319 270, 0,9 %
spridning). Scan-kostnaden är *tabellen*, inte prediktatet. Fixen återför `shared hit` till exakt
pre-PR-basens nivå. Cold mätt till 63,9 s; pre-PR-basen ligger också över 30 s cold.
**Deltat varken skapar eller kan ta bort detta.**

**Varför Major och inte Blocker:** runda 1:s Blocker bar en **relativ** tröskelpassage som diffen
orsakade — oberoende av lådan. Detta är ett **absolut** påstående om otillgänglighet, mätt på en
dev-låda med 128 MB `shared_buffers`, inte på Netcup. En mätning i fel miljö friar inte — och den
fäller inte heller. Vad som är okänt förblir skrivet som okänt.

**Disposition:** §9.6 undantag (1) — filas som issue med eskaleringen namngiven i den, PR:en går igenom.

## Minor 1 — `m3b` har ett fjärde hem

`RecruiterErasureIngestTests.cs:2845` säger fortfarande *"the evidence is the normalised org.nr
that matched"*. Fix på ett av N är ingen fix.

## Svar på sessionens frågor

**Q1 — stänger deltat Blockern?** **Ja.** Sessionens läsning av mitt runda-1-tal var korrekt i
slutsats men vilade på fel siffra: mina 46,7 s cold / 6,7 s warm var **pre-PR-basen**, inte formen
jag blockerade. Den blockerade formen var **38,7–41,3 s warm** — över 30 s även varm.

**Q2 — är polariteten fel?** **Polariteten är rätt — men motiveringen är fel.** Null-grenen är
**inte** onåbar: `WrittenForms()` returnerar `[]` för varje icke-org.nr-identifierare. Vad som
ändrades är vad null *betyder*: i `OrgNrEvidence` opererade grinden på ett **lagrat** värde (äkta
parse-osäkerhet); i `termsArePersonnummerShaped` på **begäran**, där null betyder "inte ett org.nr
alls" — att inte flagga är då korrekt. Grinden är **strukturellt upplöst**, inte bara onådd.

## Namngiven residual

Flaggan är en egenskap hos **begäran**, inte hos **excerpten**. Mätt på hela korpusen: 165 annonser
bär en tiosiffrig sekvens i `description`, **82** är personnummer-formade, **4** har ett kontaktnamn
i beskrivningen — och **0** har namnet inom excerptfönstret. `title`/`company_name`: 0/0.
Populationen är noll idag och deltat förstorar den inte.

## Anmärkning på formen

`m3c` stängdes genom **omskrivning**, inte strykning — tvärtemot §9.6 och tvärtemot vad min egen
eskalering bad om. Kostnaden blev omedelbart synlig: den nya prosan bar tre nya påståenden, varav
*"this channel's predicate is the tightest of the set"* är en superlativ som `job_ads.contacts`-armen
tangerar. Jag graderar det inte (§9.6 förbjuder phrasing-fynd i en omkontroll) men noterar det.

## Eskalering till Klas — ordagrant

Art. 17-dry-runens cold-cache-fall överskrider Npgsqls 30 s command timeout, och PR #1458 varken
orsakar eller kan ta bort det. Jag har mätt det idag mot dev-korpusens 106 071 annonser: `physical
read` är invariant över alla tre queryformerna — 319 662 blocks för formen som gällde före PR:en,
322 003 för formen jag blockerade i runda 1, 319 270 för fixen, en spridning på 0,9 %. Scan-kostnaden
är tabellen på 1 761 MB, inte prediktatet. Fixen återför `shared hit` till exakt den nivå som gällde
före PR:en (871 264 mot 870 958), vilket är hela dess verkan och en riktig sådan: warm går från
38,7–41,3 s till 14,9–16,2 s. Men cold-I/O-premien på cirka 46 sekunder ligger kvar oförändrad, och
den lägger även den ursprungliga formen över 30-sekundersgränsen — cirka 50 sekunder. Konsekvensen
när den faller ut är densamma som i runda 1: den obligatoriska dry-runen, som är den enda mänskliga
grinden före en oåterkallelig radering och den enda vägen att alls besvara en Art. 12(3)-begäran,
kastar `NpgsqlException` i stället för att svara. Vi håller 51 347 verkliga kontaktposter över 40 983
annonser med namngivna rekryterare.

Jag graderar det Major och inte Blocker, och skälet ska stå skrivet eftersom det begränsar vad du kan
luta dig mot. Runda 1:s Blocker bar på en relativ tröskelpassage som diffen själv orsakade, och den
slutsatsen är oberoende av vilken låda jag mäter på. Detta är ett absolut påstående om
otillgänglighet, och jag har mätt det på en dev-låda med 128 MB `shared_buffers` — inte på Netcup,
vars buffertkonfiguration och I/O-latens jag inte har mätt. En mätning i fel miljö friar ingenting,
och den fäller inte heller något. Vad jag vet är att marginalen warm är 15 sekunder mot 30, att den
krymper när korpusen växer, och att cold-fallet är den realistiska formen för den *första*
Art. 17-begäran på en låda som inte kört en nyligen. Vad jag inte vet är hur stort cold-fallet är på
lådan. Det förblir skrivet som okänt, och en accepterad risk blir inte mätt av att accepteras.

Det du behöver avgöra är inte om #1458 ska merga — det tycker jag att den ska, och jag släpper den
under §9.6 undantag (1) eftersom att blockera en PR för tillstånd den inte orsakat är precis den
deadlock designen håller ute ur CI. Det du behöver avgöra är **vem som äger remedyn och när den mäts
på lådan**: en explicit och motiverad `CommandTimeout` på just denna query, plus en mätning av
request-timeouten i ledet utanför ASP.NET, vilket är #196:s område. En höjd `CommandTimeout` ensam
flyttar felet uppåt i stacken, den tar inte bort det. Och lapse-frågan är verklig: ingenting i repot
detekterar att marginalen har ätits upp, så gränsen passeras tyst nästa gång korpusen växer eller
lådan är kall.
