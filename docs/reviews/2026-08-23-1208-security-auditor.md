# security-auditor — PR #1474 (#1208)

**Agent:** security-auditor · **Date:** 2026-08-23 · **Head:** `ec91a7c0` · **Base:** `75dbba78`
**Mode:** report-only (charter forbids any repo effect — no `Write`/`Edit`/commit). Transcribed
verbatim by the driving session per CLAUDE.md §9.2.

Two corrections to the session's framing, both measured, before the audit:

- **`75dbba78...HEAD` is not a filtered range.** `git merge-base 75dbba78 ec91a7c0` = `75dbba78`,
  so two-dot and three-dot are **the same 25 files** — the 17 web files are in both. The PR's own
  content is `git diff 75dbba78..fcc30e2b` (8 files). I scoped by path; a reviewer trusting the
  framing would treat merged main content as the session's.
- **Area 8 not triggered:** 0 files touch `package.json`, `pnpm-lock.yaml` or `.github/`. The
  suppression guard was not run, and that is the charter call ("match to the diff"), not a skip.

---

## Security-audit: dev-Seq body-write gate, ConsoleEmailSender (PR #1474, issue #1208)

**Status:** ⛔ BLOCKED (1 Major)
**Auktoritet:** GDPR art. 5.1(c) dataminimering · art. 5.1(f)/32 · art. 25 privacy by design ·
CLAUDE.md §11, §9.6, §5 (`Comments:`) · AGENTS.md §12 · ADR 0087 D8 · ADR 0127 ·
senior-cto-advisor-bind 2026-08-23

### Blockers

Inga. Ändringen är en dataminimering: exponeringen krymper monotont, inget PII-fält tillkommer,
ingen retention-, transfer- eller auth-yta rörs. Art. 25 uppfylld — invarianten sitter i
skrivvägen, inte i prosan.

### Major

**1. Täckningspåståendet i shipped källkod är MÄTBART FALSKT.**
Fil: `src/Jobbliggaren.Infrastructure/Email/ConsoleEmailSender.cs:172-175`

Nuvarande: *"Nothing is granted for convenience today either — every recipient that legitimately
reaches this sender is already under the rule (user@example.com …, klas@jobbliggaren.test …,
test-e2e-\*@e2e.jobbliggaren.test …)"*. Samma mening bär CTO-bindet (axis 1a) och PR-bodyn.

Mätt mot sänkan själv, `jobbliggaren-seq` `/data`, 2026-08-23, med sessionens egen publicerade
kommandorad: **7 distinkta mottagare** har nått denna sender och ligger UTANFÖR regeln —
`render-1303-1280-…@example.se`, `render-1303-1920-…@example.se`, `render-1303-3440-…@example.se`,
`render-1786391664352@example.se`, `render-1786391754098@example.se`, `e2e-1786391810@example.se`,
`bii-render-1291@exempel.se`. Det är 19 av 106 `[ConsoleEmailSender] To=`-förekomster
(18 `example.se` + 1 `exempel.se`). De tre `render-1303-*` är repots egna mandaterade viewports
1280/1920/3440 — alltså design-lanens levererade rendered-verification-flöde, inte hypotetisk
trafik.

Motsägelsen är intern: mätdokumentets egen §3-tabell listar `example.se`/`exempel.se` och
annoterar dem *"nothing — registrable under `.se`"*, men talen korsläses aldrig mot
täckningspåståendet. Tredje raden i tabellen pekar dessutom på `tests/e2e/helpers/auth.ts`, en
sökväg som inte finns (rätt: `web/jobbliggaren-web/tests/e2e/helpers/auth.ts:18`).

**Krävs:** stryk meningen. Resten av stycket (*"The membership rule is an RFC rather than an
allow-list … so the set cannot drift toward convenience"*) är sant och bär sig självt — noll
tillagda rader, §9.6:s mekaniska stängning. Samma korrigering i PR-bodyn.

**Motivering:** meningen är den shippade motiveringen för mängdens exakta medlemskap, och den är
det nästa underhållare läser när ett dev-lane-flöde slutar fungera. *"Inget legitimt flöde
påverkas"* gör en känd, filad, ägd migrering (#1475) till en till synes regression i grinden — och
billigaste skenbara reparationen är att lägga `example.se` i `ReservedSecondLevelDomains`, exakt
det designen förbjuder. Testet `ReservedSecondLevelDomains_AreExactlyRfc2606s` fäller det, så
kontrollen håller; det är trycket mot testet som meningen skapar. **Ingen GDPR-implikation** —
grinden felar stängt.

### Minor

**2. Ny retention-claim i två hem, båda tillagda av denna PR.**
Fil: `ConsoleEmailSender.cs:225-226` (*"into a sink with no retention (CLAUDE.md §11)"*) och `:243`
(EventId 3008: *"and this sink has no retention"*).

Mätt: `grep -i retention CLAUDE.md` → **0 träffar**. §11 säger ingenting om retention; basen
`75dbba78` (#1170) raderade just den utsagan från varje hem, med motiveringen *"The claim this PR
deletes was prose, so nothing could have failed when it drifted"*. PR:en återinför klassen en
commit senare och tillskriver den en §-referens som inte bär den. CTO-bindet avvisade Variant A på
precis den grunden: dev-Seq-retention är per-utvecklares container-state — *"no PR can deliver it,
no CI job can read it, and no reviewer can verify it"*.

**Krävs:** stryk båda satserna. `NonReservedRecipient_WithholdsBodyAndRecipient_AtWarning`
asserterar inte meddelandesträngen, så strykningen fäller inget test.

**3. Två ovillkorliga hem som svepet missade.**
Fil: `src/Jobbliggaren.Infrastructure/DependencyInjection.cs:999` (*"`ConsoleEmailSender` skriver
mottagar-email + plaintext-token till ILogger"*) och
`src/Jobbliggaren.Infrastructure/Email/NullEmailSender.cs:10` (*"`ConsoleEmailSender` writes the
recipient email + notification body to `ILogger`"*). Båda presens, båda ovillkorliga, båda orörda.

Sessionens svep sökte på `hela mejlkroppen|whole body|entire body|hela .?PlainTextBody` — ingen av
dessa två stavar egenskapen så. Fullständigt hemsvep: **11 hem, 9 uppdaterade, 2 missade**.
`DependencyInjection.cs:999` är kompositionsrotens egen doc, alltså det hem en läsare av
DI-wiringen faktiskt möter.

**Krävs:** strykning av satsen (slutsatsen *"registreras BARA i Development/Test"* står kvar och är
oförändrat sann).

**4. Testfilens egen självbeskrivning är falsk om 2 av 4 adresser.**
Fil: `tests/Jobbliggaren.Application.UnitTests/Email/ConsoleEmailSenderReservedRecipientTests.cs:26`

Nuvarande: *"The non-reserved addresses below are all under the project's own domain, deliberately
… any other choice would be a name a third party can register — the exact hazard the gate exists to
close."* `probe@jobbliggaren-example.com` och `probe@notexample.org` ligger INTE under
`jobbliggaren.se`; de är namn tredje part kan registrera. Filens egen metodkommentar säger
motsatsen på raden intill (*"belonging to another owner"*). PR-beskrivningen upprepar felet och
utelämnar `notexample.org` helt.

Litteralerna är nödvändiga och ofarliga — en label-boundary-trap KAN inte konstrueras inuti en
domän man äger, och ingenting skickas någonsin. Det är påståendet som ska strykas, inte data:
behåll *"nothing is ever sent, and the point of each case is a classification, never a mailbox"*,
vilket är sant om alla fyra.

**5. §3:s `To=`-tal är tick-only under en rubrik som intygar omskopning.**
Fil: `docs/reviews/2026-08-23-1208-seq-measurement.md` §3 + PR-bodyn (*"22 rendered
`[ConsoleEmailSender] To=` events"*).

Mätt, uppdelat per extent-typ: `.tick` **22**, `.span` **84**, `.index` 0, övrigt 0. 22 ÄR
tick-only-talet. §1 säger uttryckligen *"Every number in §3 is from the re-scoped sweep over every
file under `/data`"* — sant om adress-censusen (jag reproducerade den med den publicerade
kommandoraden) men falskt om `To=`-talet, som är det enda tal i §3 utan publicerat kommando. Samma
skop-fel som §1 återkallar, i avsnittet §1 intygar. Sidoresultat: §3-tabellen säger 6 distinkta
`example.se`/`exempel.se`; sessionens egen kommandorad ger nu **7**.

**Krävs:** korrigera talet eller stryk det. Dokumentet är gitignorerat, så PR-bodyn är det hem en
läsare möter.

### Praise

- Chokepunkten är verkligt stängd: `LogEmail` har **exakt ett** anrop, inuti reserved-armen;
  arity-testet tvingar in en nionde metod i `Cases` och egenskapstestet fäller den då den kringgår
  `WriteEmail`. Två test som komponerar, inte överlappar.
- Absens-assertionen läser BÅDE renderat meddelande och structured properties — den enda form som
  ser en otemplatead `[LoggerMessage]`-parameter. Icke-vakuös sedan #1237.
- Predikatet felar stängt åt rätt håll, med label-boundary i båda riktningarna, `LastIndexOf('@')`,
  FQDN-punkt — och `CanDeliver` orörd, med felresonemanget nedskrivet där misstaget skulle göras.

### Sammanfattning

0 blockers, 1 major, 4 minor. Samtliga fem remedier är STRYKNINGAR eller en talkorrigering — noll
tillagda claim-meningar, alla stängbara in-block i en runda (§9.6 filar inga issues för dessa;
#1475 äger redan `example.se`-migreringen). Re-review efter fix: samma agent, report-only, scopad
till fix-deltat (CLAUDE.md §9.6).

**Eskalering till Klas: nej.**

---

## Utlåtanden på sessionens sex frågor

**1. Recipient-axeln — komplett. Innehållsaxeln — en klass, oförändrad av PR:en.**
`SendEmailChangeConfirmationAsync`s mottagare ÄR `content.NewEmail` (`ChangeEmailCommandHandler.cs`,
*"Send the confirmation link to the NEW address"* — grinden nycklar på samma adress som mallen
bäddar in). `EmailChangedNotification()` tar ingen content och bär ingen adress. Ingen av de åtta
mallarna bäddar in en annan adress än `toEmail`. Verifierat i `EmailTemplates.cs`.

Tredjeparts-PII som grinden inte kan se: **`MatchNotificationItem.CompanyName` /
`FollowedCompanyAdItem.CompanyName`**. För en enskild firma ÄR firmanamnet innehavarens namn — en
fysisk person, som ingen av CLAUDE.md §9.6:s tre register (konto, send-logg, publik läsning) mäter.
Kontrakten bär inget org.nr (ADR 0087 D8), så #841:s personnummer-formade org.nr når inte kroppen;
0 personnummer-former i sänkan. **Inget fynd mot denna PR** — populationen som kan utlösa det
krymper från "vilken mottagare som helst" till "reserverad mottagare". Registrerat så att nästa
läsare slipper härleda om det.

**2. Formen räcker, meddelandet läcker inget.** `LogSuppressedBody(string emailKind)` — en
parameter, compile-time-literal på alla åtta anropsställen, `EveryKind_IsDistinct` fäller kollision.
EventId 3008 är unikt i repot (3001/3002/3005/3006/3007 upptagna). Paritet med
`NullEmailSender.LogSuppressedConsequential` verifierad. Sessionens mutation kunde jag inte köra om
— den kräver en repo-skrivning min charter förbjuder — men jag verifierade det den mäter:
assertionen läser `properties`, och `RecordingLogger` snapshottar dem (#1237), så en otemplatead
parameter ÄR synlig. Meddelandesträngen bär ingen PII. Enda invändningen är retention-satsen
(Minor 2).

**3. Domänvalet är rätt, självbeskrivningen är fel.** `jobbliggaren.se` för det live-fall som krävs
är exakt rätt: ingen tredje part kan registrera det, ingenting skickas, och alternativet aliasar en
verklig brevlåda. Att medlemskapstestet pinnar mängden direkt i stället för att kräva en
consumer-mail-literal någonstans är den starkare halvan av designen. Det som faller är påståendet
att *alla* fyra ligger där (Minor 4).

**4. §11:s residual är ärlig och i rätt bredd för det den påstår.** Den påstår inte att sänkan
saknar riktig PII — det ovillkorliga villkoret är raderat, inte korrigerat, vilket §9.6 kräver. Ett
sömrum värt att känna till, som meningen inte når: residualen är formulerad som *"varje ANNAN rad"*,
men bäraren i punkt 1 ovan sitter på **samma** rad (3001-kroppen), utanför nyckeln. **Detta är ett
utlåtande, inte ett graderat fynd** — att stänga det skulle kräva ny prosa i §11, vilket §9.6 gör
till en STOPP-väg. Lämna meningen som den är.

Sidomätning: `AuthAuditLogger` (EventId 1001-1006) skriver `Ip` + `UserAgent` till samma sänka. IP
är personuppgift (Breyer C-582/14). Det ligger inom den residual §11 namnger som omätt, kontrollen
är loopback + admin-auth, och bäraren är personuppgiftsansvarig själv. Inget fynd — men nu mätt, i
stället för antaget.

**5. Ja, ADR 0127 ska lämnas.** Två grunder. (i) ADR:n är ett daterat beslut, Accepted 2026-08-10,
vars utsaga var sann då; att bakvägs-editera en daterad ADR-body är vad ett amendment finns till,
och det ägs av adr-keeper som separat change-reason. (ii) Avgörande: 0127 är **gitignorerad**, så
dess läsarmängd är en maskin — och den enda §9.6-tillåtna stängningen av ett sådant fynd vore en
strykning, medan den ärliga formen är en tillagd amendment-rad. Att gradera det hade tvingat fram
antingen fel remedie eller en STOPP. Sessionens bedömning står.

**6. Svepet missade tre saker**, alla ovan: två ovillkorliga hem (Minor 3), testfilens
självbeskrivning (Minor 4), och `To=`-talets skop (Minor 5). Rotorsaken är gemensam med Major 1 —
mätningen togs, men lästes bara längs den axel den togs för.

## Vad jag verifierade och som håller

`dotnet test --project tests/Jobbliggaren.Application.UnitTests/… -- --filter-class
"*ConsoleEmailSenderReservedRecipientTests*"` → `total: 41, failed: 0`. `AddEmailSenderGateTests` →
`total: 19, failed: 0`. Inget test i repot asserterar på 3001-raden (`grep` → noll träffar), så
ingen committad svit eller CI-väg går sönder av grinden — de påverkade flödena är de otrackade,
utvecklardrivna, och de felar läsbart med remediet i loggraden och i runbooken.
`deploy/docker-compose.yml` pinnar `ASPNETCORE_ENVIRONMENT: Production` som literal (läst av mig
själv) → `NullEmailSender` på lådan. **Ingen rad i diffen läser som en låd-sidig mätning** —
retention-claimen är utvecklarmaskins-state, samma kategori men inte lådan.
