# dotnet-architect — PR #1474 (#1208)

**Agent:** dotnet-architect · **Date:** 2026-08-23 · **Head:** `ec91a7c0` · **Base:** `75dbba78`
**Mode:** report-only (charter is read-only; transcribed verbatim by the driving session per
CLAUDE.md §9.2).

## Sammanfattning

**Behöver åtgärdas — 0 kritiska, 2 viktiga, 3 nice-to-have.** Mekanismen är arkitektoniskt riktig:
choke point-invarianten är korrekt placerad, predikatet håller mot varje label-boundary-fälla jag
testade, och inga layer- eller package-gränser korsas. Båda viktiga fynden är samma defektklass som
PR:en existerar för att stänga — ett påstående som inte kan falla — och båda ligger kvar i `src/`.

## Fynd

### [Viktigt] F1 — `IsReservedRecipient`:s XML-doc påstår något som inte håller

`src/Jobbliggaren.Infrastructure/Email/ConsoleEmailSender.cs`

**Vad:** *"every recipient that legitimately reaches this sender is already under the rule"* är
falskt. Uppräkningen ärvdes från CTO-bindets tabell och mättes aldrig repo-vitt.

**Mätning:** `tests/Jobbliggaren.Worker.IntegrationTests/` är den enda sviten som komponerar
`ConsoleEmailSender` via DI (`Common/WorkerTestFixture.cs:152`, env `"Test"`). Distinkta
mottagardomäner där: `@example.com` **och `@test.local`**. `test.local` är reserverad av RFC 6762
(mDNS) — inte av 2606/6761 — så grinden klassar den som icke-reserverad. Adressen är recipienten på
riktigt: `Matching/DigestDispatchJobIntegrationTests.cs:152` och
`CompanyWatches/FollowedCompanyDigestIntegrationTests.cs:344` seedar den på `ApplicationUser.Email`,
som `IUserAccountService.GetEmailAsync` läser tillbaka och ger till sendern.

**Varför:** AGENTS.md §5 `Comments:` — ett faktamässigt fel påstående är en defekt och lagas. Samma
mening bär PR-kroppens *"The dev flow is unchanged for every recipient that already appears in the
repo"*, som därmed också faller.

**Effekt:** Ingen svit går sönder — båda testerna säger uttryckligen att de använder
notification-STATUS som observerbar proxy och aldrig läser loggen (mätt:
`FollowedCompanyDigestIntegrationTests.cs:206,269`, `DigestDispatchJobIntegrationTests.cs:86`). De
sänden byter bara arm, 3001 → 3008.

**Föreslagen åtgärd:** Stryk parentesuppräkningen. **Lägg inte `.local` i mängden** — RFC 6762 är en
annan auktoritet, och att vidga setet för att passa en fixture är precis den drift
compile-time-setet finns för att förhindra. Fixture-domänen hör hemma i den `example.se`-issue
CTO:n redan beordrade filad.

### [Viktigt] F2 — sveppet är byggt på återkallelsens egna ord, inte på egenskapen

**Vad:** PR-kroppen publicerar
`grep -rIn -i "hela .\?PlainTextBody\|hela mejlkroppen\|whole body\|entire body" .` och påstår
*"Swept on the property, in both languages, not on a file list"* samt *"Five further files carried
it"*. Det är ett svep på **frasen**. Två `src/`-filer bär samma påstående i andra ord och är
osvepta:

- `src/Jobbliggaren.Infrastructure/DependencyInjection.cs:999` — *"`ConsoleEmailSender` skriver
  mottagar-email + plaintext-token till ILogger (dev-providern)"*
- `src/Jobbliggaren.Infrastructure/Email/NullEmailSender.cs:10` — *"`ConsoleEmailSender` writes the
  recipient email + notification body to …"*

**Mätning:** Jag körde den publicerade strängen ordagrant — noll träffar i båda filerna. Ett svep på
beteendet i stället (`(skriver|loggar|writes|logs)[^.]{0,60}(mottagar-?e?-?mail|recipient|plaintext-token)`)
hittar båda.

**Varför:** `DependencyInjection.cs:999` är XML-docen för `AddEmailSender` — den enda seam båda
hostarna anropar, och den doc en läsare öppnar för att ta reda på vad Console-armen gör. Efter
grinden är meningen sann bara för reserverade mottagare.

**Föreslagen åtgärd:** Kvalificera båda meningarna där påståendet sitter, som du redan gjorde i
`LoggingBuilderExtensions.cs`. Byt sveppet i PR-kroppen mot det beteendebaserade, eller stryk
fullständighetspåståendet.

### [Nice-to-have] F3 — aritetspinnen: två axlar den inte stänger

`typeof(IEmailSender).GetMethods().Where(m => !m.IsSpecialName)` stänger tillväxt av `IEmailSender`
(en nionde metod fäller pinnen, sen fäller theory:n den nya arm som inte går via `WriteEmail`). Den
stänger **inte**: (a) medlemmar på ett *bas*-interface — `Type.GetMethods()` på ett interface
returnerar inte ärvda interface-medlemmar; (b) en icke-`IEmailSender`-metod inuti klassen som
anropar `LogEmail` direkt.

**Mätning:** `IEmailSender` har inget bas-interface idag, och `LogEmail` har exakt ett anropsställe
(`ConsoleEmailSender.cs:140`, inuti `WriteEmail`). Klassen är `sealed` och registreras på ett ställe
(`DependencyInjection.cs:1025`), så (b) är i praktiken bunden.

**Föreslagen åtgärd:** Ingen åtgärd krävs.

### [Nice-to-have] F4 — `LogSuppressedBody`-kommentarens attribution

*"Parity with NullEmailSender.LogSuppressedConsequential, whose doc states the invariant"* följt av
ett citat. Citatet står i `NullEmailSender`:s **klass**-doc (rad 30–31), inte på
`LogSuppressedConsequential` (vars egen doc, rad 201–204, säger samma sak med andra ord). AGENTS.md
§5 `Comments:` — fel gate-namn är samma klass av defekt som fel siffra. Själva parity-påståendet är
sant och verifierat: båda är `Warning` med enbart `{EmailKind}`.

**Föreslagen åtgärd:** Byt `LogSuppressedConsequential` mot `NullEmailSender`, eller stryk
attributionen.

### [Nice-to-have] F5 — odeklarerad avvikelse från beslutet av record

CTO-bindet listar `NullEmailSender.cs` som in-block-fix (*"its doc cites #1208 as an open gap"*).
PR:en rör inte filen och säger inte varför.

**Mätning:** Båda `#1208`-citaten (rad 31, 204) är parentetiska provenance-hänvisningar
`(CLAUDE.md §11, #1208)` — ingen av dem läser som en öppen lucka. **CTO:ns premiss håller alltså
inte**, och att hoppa över den är rätt beslut. §9.6 gör *"the finding does not hold"* till ett
riktigt utfall — men det ska sägas rent, med mätningen. Notera också att F2 hittade en **verklig**
defekt i just den filen (rad 10); hade avvikelsen skrivits ner hade filen öppnats.

**Föreslagen åtgärd:** En rad i PR-kroppens verdict-tabell.

## Svar på sessionens fyra frågor

**1. Placering — behåll den där den är.** Ett dedikerat `ReservedDomains`-typ i
`Jobbliggaren.Infrastructure.Email` flyttar kunskapen utan att minska den, och gör något värre: en
namngiven typ inbjuder till en andra konsument, vilket är exakt den tillväxtaxel compile-time-setet
köptes för att stänga. SRP-spänningen är dock **verklig** — klassen bär nu två reasons to change
(RFC:n ändras / mejlytan ändras). Den vägs upp av n=1. Namnge triggern i stället för att extrahera
nu: **andra konsumenten flippar beslutet** (t.ex. om `example.se`-svepet vill samma predikat för
fixture-lint). Det är OCP ärligt tillämpat.

**2. Layering — bekräftat.** `ConsoleEmailSender` i Infrastructure, `IEmailSender` i
`src/Jobbliggaren.Application/Common/Abstractions/`, testet når internals via
`Jobbliggaren.Infrastructure.csproj:208`. Nya usings är `System.Collections.Frozen` +
`System.Collections.Immutable`, båda BCL. **Ingen `.csproj` och ingen `Directory.Packages.props`
rörd** (mätt) — package-axeln i AGENTS.md §2.1 är orörd. **Precisering:** PR:en ändrar också ett
test i `Jobbliggaren.Api.IntegrationTests` — inte en layering-överträdelse, men sessionens
formulering nämnde bara `Application.UnitTests`.

**3. Radien.** Ingenting subklassar (`sealed`) och ingenting komponerar utanför
`DependencyInjection.cs:1025` (mätt). Api-integrationshosten når den aldrig — `ApiFactory.cs` gör
`RemoveAll<IEmailSender>()`, CTO:ns påstående verifierat. Men #1463-misstanken **bar frukt**:
Worker-sviten komponerar den, och där ligger F1.

**4. `FrozenSet` + `ImmutableArray` — rätt val, och `ImmutableArray` är det starkare av de två.**
`FrozenSet` med `OrdinalIgnoreCase` speglar `RecurringJobIds.cs:52`; divergensen från dess `Ordinal`
är korrekt, inte drift — DNS är skiftlägesokänsligt. `ImmutableArray<string>` är repots första i
`src/`, och det är ett argument **för**: fältet är `internal` och Infrastructure delar internals med
fem assemblies, så en `string[]` hade kunnat skrivas över i runtime av var och en av dem — vilket
river CTO:ns avgörande grund (*"a compile-time constant cannot be widened at runtime"*). `FrozenSet`
går inte här eftersom TLD-regeln är suffix-matchning, inte membership.

## Påståenden i uppdraget som inte håller

**Diff-området.** *"`git diff 75dbba78...HEAD` (TRE punkter …)"* är falskt. `75dbba78` **är** en
ancestor till `HEAD`, så merge-base är `75dbba78` självt och tre punkter är identiskt med två: båda
ger **25 filer, 953 insertions**, web-filerna inkluderade. Området som faktiskt isolerar innehållet
är `git diff 75dbba78 fcc30e2b` eller `git diff ec91a7c0^2 ec91a7c0` — **8 filer, 438 insertions**.
Tvåpunkts-→trepunkts-regeln gäller när basen har rört sig ifrån dig; här har den inte det.

**Grindarna.** Fyra rapporterade. Två svitar som ändringen når saknar `total:`-rad:
`Jobbliggaren.Api.IntegrationTests` (innehåller `Configuration/AddEmailSenderGateTests.cs`, som
PR:en **ändrar**) och `Jobbliggaren.Worker.IntegrationTests` (komponerar den ändrade klassen och kör
den mot `@test.local` — F1). Api-ändringen är kommentar-only och kan inte falla; Worker-sviten byter
faktiskt runtime-arm. Per AGENTS.md §7 är beviset `total:`-raden, och den finns inte för någondera.

**PR-kroppen:** *"Swept on the property … Five further files carried it"* (F2) och *"The dev flow is
unchanged for every recipient that already appears in the repo"* (F1) håller båda inte.

**Verifierat och sant:** EventId 3008 är ledig — svepte varje `[LoggerMessage]`-id i `src/`, noll
dubbletter (3001 Console, 3002/3007 Null, 3005/3006 Scaleway). `deploy/docker-compose.yml:340`
pinnar literalen `Production`. `ApiFactory` gör `RemoveAll`. `WriteEmail` är en fullständig cut — 8
anropsställen, `LogEmail` har exakt ett. Predikatet klarar varje label-boundary-fälla jag räknade
för hand, inklusive `example.com.jobbliggaren.se` och `jobbliggaren-example.com`.

## §12

**Inget av fynden är en STOPP-klass.** F1/F2/F4 ligger i §5 `Comments:`, som §12 uttryckligen
carve-ar ut. F1 och F2 är ändå **Viktigt** i min skala och därmed merge-blockerande per §6: in-block
eller följd-PR, aldrig en issue.

## Referenser

AGENTS.md §2.1, §2.2, §5 `Comments:`, §7 · CLAUDE.md §9.6, §11 ·
`docs/reviews/2026-08-23-1208-cto.md` (beslutet av record; dess adressuppräkning i axel 1(a)-tabellen
är den ärvda källan till F1)
