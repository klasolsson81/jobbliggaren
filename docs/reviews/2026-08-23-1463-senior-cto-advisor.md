# senior-cto-advisor — #1463 (PR mot `main`)

> Transkriberad ordagrant av ägande session. Agenten kan inte skriva repo-filer själv och kan inte
> fråga Klas (CLAUDE.md §9.2). Ingen eskalering.
>
> **Beslutsfattare**, inte rådgivare: sessionen gav med avsikt ingen egen rekommendation på D1/D2.
> Underlaget agenten fick var mina mätningar (Netcup-lådan + dev) och de två #1448-rapporterna.
> **Branch** `fix/erasure-command-timeout-1463` · **base** `7b318483` · **Datum:** 2026-08-23

Read: both review reports verbatim, `gh issue view 1463`, the four precedent constant sites,
`FindJobAdsAsync` in full, the DI registration, and `SessionStoreUnavailableLog`. Two measurements
of my own below feed the decisions.

---

## CTO-rekommendation

### Beslut

**D1 — `internal const int CommandTimeoutSeconds = 180;`** i `RecruiterErasureMatchQuery`.
**D2 — WARN-tröskeln binds som en andel av taket, `CommandTimeoutSeconds / 2` = 90 s** — härledd i koden, aldrig ett andra tal.
**D3 — konstruktorn bekräftas.** Din motivering håller; grunden nedan är starkare än den du gav och bör vara den som skrivs.

---

### D1 — 180 s

**Motivering mot principer**

- **Taket måste få sin EGNA skrivna grund, inte ett återanvänt tal.** Repots tre befintliga konstanter bär var sin härledning på plats (`CompanyWatchBrowseQuery.cs:42-79` är mönstret: namnge det värsta fall taket är headroom över, och skriv ut vad som är omätt). Att kopiera `ScbCompanyRegisterStore`s 120 vore att importera *dess* grund — en bulk-batch vars felläge är en retry — till en väg vars felläge är att Art. 12(2) inte kan uppfyllas genom produktens egen mekanism. **DRY är ett ställe per kunskapsstycke** (Hunt/Thomas 1999, kap. 7), inte per sifferföljd; två olika kunskapsstycken som råkar dela siffror ska inte dela namn eller härledning.
- **Vad taket är FÖR här:** inte headroom över en känd kostnad, och inte "ett tak på en bugg". Det är gränsen för hur länge **en** admin-request får hålla en poolad anslutning innan vi förklarar kommandot hängt. Asymmetrin styr kalibreringen: ett spuriöst utfall gör rättighetsmekanismen oexekverbar; att vänta längre kostar en anslutning av poolens default 100 (repot sätter ingen `Max Pool Size` — mätt, noll träffar i `src/`), en handfull gånger per år, på en admin-autentiserad väg. **Poolargumentet skiljer alltså inte 120 från 300.** Det som skiljer dem är marginalen mot mätt värsta *slutförande* körning, och punkten där "ändligt" upphör att vara en liveness-garanti.
- **Mätt värsta slutförande körning = 63,9 s** (dev, kall). Den är pessimistisk i rätt riktning: dev-tabellen är 2 493 MB mot lådans 834 MB, och `shared_buffers` 128 MB mot lådans 640 MB — båda axlarna som driver kall kostnad är sämre i dev. Lådans kalla fall förblir **omätt** och ska stå skrivet som omätt (§9.6 Filing discipline; security-auditors egen formulering).
- **180 = 2,8× det talet, ~116 s marginal.** Det överlever en tredubbling av den kalla kostnaden — alltså en tredubbling av lådans korpus, eller att lådan degraderar mot dev:s buffertkvot — utan spuriöst utfall. Och 180 är fortfarande ett tal en operatör kan få skrivet: *har den inte svarat på tre minuter är något fel.*

**Avvisade alternativ**

- **120.** 1,9× det mätta värsta fallet, ~56 s marginal. Kostnaden är tabellskanningen (`physical read` invariant, 0,9 % spridning över tre queryformer) och tabellen växer monotont med ingest. En dubbling av korpusen äter marginalen — och hela poängen med #1463 är att **ingenting upptäcker att den ätits**. Att talet råkar matcha ett annat site är inte ett skäl, det är en inbjudan till nästa läsare att tro att grunderna delas.
- **300.** Fem minuter ligger bortom punkten där ett ändligt tak fortfarande garanterar liveness för den människa som sitter vid grinden, och bortom där något annat led (operatörens `docker exec … curl`, eller vad #196 senare lägger på vägen) hinner ge upp först. Ett tak ingen någonsin når är inte ett granskat tak. Repots egna skrivna regel — *"never 0/infinite — a genuinely hung command must still fail loud"* (`ScbCompanyRegisterStore.cs:33`) — gäller i sin anda, inte bara i sin bokstav.

**In-block-krav som följer med talet**

1. Härledningen skrivs på konstanten i `CompanyWatchBrowseQuery`s form: vilket värsta fall 180 är marginal över, **daterat** (`2026-08-23`), plus EXPLAIN-kommandot som regenererar talet (§5: ett *levande* mätt tal i en spårad fil förfaller; ett *daterat historiskt* gör det inte).
2. **Skriv ut att lådans kalla fall är omätt.** Inte som en brasklapp — som den enda ärliga formen.
3. **En pin på beteendet, inte på litteralen.** `db.Database.GetCommandTimeout()` efter att porten konstruerats ska vara `CommandTimeoutSeconds`. Utan den är taket *dokumenterad avsikt, inte exekverbar garanti* — precis den fällan CLAUDE.md §11 redan namnger om `ReverseProxyOptions` (*"the pin covers the constant, not the composition root"*).
4. De fyra befintliga siterna påverkas **inte** — råa `NpgsqlCommand` plockar aldrig upp EF:s `SetCommandTimeout`, vilket `CompanyWatchBrowseQuery.cs:44` redan säger. **Peka dit, restatera inte** (#1173).

---

### D2 — en andel av taket: `CommandTimeoutSeconds / 2` = 90 s

**Motivering mot principer**

- **Andel, inte absolut tal — och skälet är SPOT, inte smak.** Ett absolut `WarnThresholdSeconds = 90` är ett andra tal som måste räknas om för hand varje gång taket rör sig, och båda driftlägena är tysta: höjs taket till 300 fyrar 90 s vid 30 % och blir brus; sänks taket fyrar tröskeln aldrig före taket och blir tystnad. Det är exakt defektklassen `dotnet-architect` V1 fällde i samma granskningsrunda — *"vilken kolumn normaliserar" står i tre hem och inga två är överens*. **Ett kunskapsstycke — "marginalen äts" — uttryckt en gång, i termer av det den är marginal mot** (Hunt/Thomas 1999; **SPOT/SRP**, Martin 2017 kap. 7). Skriv den som `private static readonly TimeSpan WarnThreshold = TimeSpan.FromSeconds(CommandTimeoutSeconds / 2.0);`.
- **Varför just halva.** Signalen måste klara två egenskaper, och bara den andra är bindande.
  - *Inte brus:* lådan varm ligger på 5,4–6,1 s. 90 s är ~15× det. Ingenting rutinmässigt når dit — inte ens en kall körning av dev:s form (63,9 s), som har en frisk 2,8× marginal och därför **inte ska** varna. En varning som fyrar på en frisk körning är brus även när talet är sant, och operatören lär sig ignorera raden.
  - *Inte tystnad:* det som detekteras är monoton korpustillväxt, inte en spik. Vid 50 % måste körningen **dubbla** sin kostnad mellan första varningen och första felet — på en väg som körs en handfull gånger per år är det den runway som räknas, mätt i *körningar*, inte i dagar. Vid 75 % (135 s) räcker 33 % tillväxt, och du kan få exakt en varnad körning före den fallerande. Vid 33 % (60 s) fyrar den på kalla körningar som är friska. **Halva taket ger hela andra halvan som varningsband.**
- **Mät rätt kvantitet — och det här är skillnaden mellan en signal och ett kategorifel.** Taket är en **per-command** `CommandTimeout`. `FindJobAdsAsync` utfärdar **två** kommandon (den råa `SqlQuery` som matchar id:n, och EF-projektionen över `typedIds`). Klockas hela metoden jämförs en väggklocka över två kommandon mot ett per-kommando-tak — två olika storheter, och larmet blir osant i den lugnande riktningen så fort projektionen växer. **Klamra mätningen om enbart matchningskommandot** (`SqlQuery … ToListAsync`), som är det enda som kan nå taket.
- **Ingen throttle.** `SessionStoreUnavailableLog` throttlar för att ett Redis-avbrott gör *varje* request till en 503. Här körs vägen en handfull gånger per år — det finns inget att översvämma, och en importerad throttle skulle kunna svälja den enda körning som någonsin varnar. Skriv ut det, annars inför nästa granskare den per analogi.
- **Logg-raden: elapsed + taket, ingenting annat — bekräftat.** Identifieraren är den registrerades namn/adress/personnummerformade org.nr; §5 förbjuder att den loggas, och ADR 0087 D8(c) är skriven absolut om *"any display projection"* — en Seq-sink är en. Att raden bär **taket** och inte andelen är dessutom rätt: läsaren räknar kvoten själv, och vi lagrar aldrig ett tredje tal som kan drifta.
- **Skriv INTE ett test som påstår att tröskeln är halva taket.** Mot samma uttryck är det en tautologi och pinnar ingenting (`feedback: a pin must cross the threshold of the property it pins`). Härledningen är sin egen garanti; det som förtjänar en pin är taket (D1, punkt 3).

---

### D3 — konstruktorn, bekräftad

Inte tvetydigt, och grunden är starkare än "blast radius är liten". Båda dina halvor stämmer mot koden: `SetCommandTimeout` sätts på `DatabaseFacade` och lever så länge den scoped `AppDbContext` gör, så ett per-metod-anrop höjer taket för allt *efterföljande* bruk av samma context i requesten — en **bredare och odeklarerad** radie, inte en smalare. Och porten är `AddScoped` (`DependencyInjection.cs:1224-1226`), resolvad bara där erasure-kommandot injicerar den.

Men det bärande skälet är designnivån: taket är inte *"`FindJobAdsAsync` är långsam"* — det är *"den här porten betjänar Art. 17-mekanismen, vars tillgänglighet är Art. 12(2)-absolut, och 30 s är kalibrerat för interaktiva användarvägar som detta inte är."* Det påståendet är sant om **varje** metod på porten. Porten har en change-reason, och timeout-policyn ändras av samma change-reason — så policyn hör hemma där den change-reason bor (**SRP**, Martin 2017 kap. 7). Ett per-metod-anrop sprider en policy över 13 siter, eller över 1 site med 12 tysta arvingar, vilket är sämre än båda. Detta blir repots **första** EF-nivå-`SetCommandTimeout` på `AppDbContext`; säg det, så vet nästa läsare att avsaknaden av fler siter är ett faktum och inte en lucka.

---

### Namngiven residual — och den blir INGEN ny issue

security-auditors stående invändning — *"en höjd `CommandTimeout` ensam flyttar felet uppåt i stacken"* — är **falsifierad för idag** av din mätning (Kestrel utan exekveringstimeout, Caddy utan `/api`-matcher, operatörsvägen på interna nätet). Den mätningen förfaller. Skriv den **daterad vid konstanten** som villkoret som ogiltigförklarar taket: *lägger #196 Caddy på `/api`-vägen, eller läggs `AddRequestTimeouts` till, blir 180 dekorativt igen och ska mätas om.*

**Läggs som en kommentar på #196**, som redan äger deploy-ledet och som båda granskarna pekade på — inte som en ny issue. En kommentar på en issue som redan äger området är avveckling med en läsare; en ny issue är en dublett, och §9.6:s netto-tak ska inte spenderas på en. Ingen ny etikett behövs.

---

### Trade-offs accepterade

180 är ett fjärde distinkt timeout-tal i repot i stället för ett återanvänt tredje. Acceptabelt: talens *grunder* är olika, och att tvinga fram numerisk likhet mellan olika grunder är precis den falska DRY som gör att nästa läsare drar fel slutsats. Vidare: taket täcker även EF-projektionen (konstruktorplacering) medan WARN klamrar bara matchningskommandot — asymmetrin är avsiktlig och ska skrivas ut, annars läser nästa granskare den som en bugg.

---

### Referenser

- Robert C. Martin, *Clean Architecture* (2017), kap. 7 (SRP)
- Hunt/Thomas, *The Pragmatic Programmer* (1999), kap. 7 (DRY — ett ställe per kunskapsstycke)
- CLAUDE.md §9.6 (avveckling, Filing discipline, netto-taket) · §11 (pin ≠ komposition) · AGENTS.md §5 (`Comments:`, loggning av PII) · §2.5
- ADR 0087 D8(c) · ADR 0009 (porten exponerar inte `Database` — orörd) · ADR 0091 amendment (#688, granskade konstanter, aldrig 0)
- `CompanyWatchBrowseQuery.cs:42-79` (härledningsformen) · `CompanyRegisterSearchQuery.cs:57-63` · `ScbCompanyRegisterStore.cs:28-35` · `SessionStoreUnavailableLog.cs:17-19` (monoton mätning)
- `docs/reviews/2026-08-23-1448-security-auditor.md` · `docs/reviews/2026-08-23-1448-dotnet-architect.md` · #1463 · #196

---

## Sessionens tillämpning (utanför agentens text)

D1, D2 och D3 är alla implementerade som beslutade. Två avvikelser i **skärpande** riktning, båda
redovisade i PR-bodyn:

- **D1 punkt 3** bad om en pin på `db.Database.GetCommandTimeout()`. Levererat i stället: en
  `DbCommandInterceptor` som läser `CommandTimeout` på det **verkliga `DbCommand`** som exekveras,
  plus en kontrollhalva som visar att en scope utan porten fortfarande bär 30. Den pinnen korsar
  allt `GetCommandTimeout()` skulle ha korsat och därtill det led `dotnet-architect` skrev som
  **omätt** — att `Database.SqlQuery` ärver facade-värdet. Mätt: det gör den.
- **D2:s sista punkt** förbjuder ett test som påstår att tröskeln är halva taket (tautologi). Inget
  sådant test finns. Det som pinnas är **emittern** — polaritet i båda riktningar och radens
  strukturella fältuppsättning, så att ett tillagt fält (PII-vägen) fäller testet.
