# Auth epic #1732 — part 0 pre-review (security-auditor, 2026-09-17)

> Transcribed verbatim by the driving session from the agent's returned report. Brief the agent
> answered: the session's scratch files `1733-brief-context.md` + `1732-epic.md`, and the CTO and
> architect reports of the same date. Worktree `C:/tmp/jbl-1733` at HEAD `081e4c67`. The session
> re-ran the load-bearing measurements before transcribing: `DevEndpoints.cs:30/64` (the two map
> methods), `ProductionStartupSmokeTests.cs:160/187/208` (`confirm-email` pinned in both polarities
> of `DevTools:EnableResetMyData`), `CaddyfileTokenScrubbingPinTests.cs:152/161-172` (the
> three-path regex and the three-template list), `content-legal.json:32` ("lösenord … visningsnamn"),
> `:101` ("inga överföringar … utanför EU/EES"), `ConsoleEmailSender.IsReservedRecipient` at
> `:138/181`, `PersistKeysToFileSystem` at `DependencyInjection.cs:1611` with no `ProtectKeysWith*`,
> the register's "Användarkonto och autentisering" at `gdpr-processing-register.md:1329` and the
> `Sessioner` bullets at `:1380/1426`, `UserAccountService.cs:125-137` (`IsLockedOutAsync` →
> `CheckPasswordAsync` → `AccessFailedAsync`) — all confirmed. The break-glass block and the
> escalation were put to Klas in plain text the same turn, unabridged, not transcribed as settled.

## Security-audit: auth-epik #1732 design (del 0) — D1–D10 mot HEAD 081e4c67

**Status:** ⛔ BLOCKED (1 Blocker, 12 Major — allt är bindningstext ADR 0142 ska bära; ingen kod finns)
**Auktoritet:** GDPR Art. 5(1)(a)/(c)/(e), 5(2), 6(1)(b), 12(1), 13(1)(e), 24(1), 25(2), 30(1)(c)/(f), 32(1), 45, 49 · ePrivacy Art. 5(3) / LEK 6 kap. 18 § · WP29 WP194 (04/2012) · CLAUDE.md §2/§9.2/§9.6/§11 · AGENTS.md §5 · ADR 0012/0013/0017/0018/0023/0024/0031/0050 (N-1)/0065/0083/0103/0132

---

### A. Försöksbudgeten — räknat själv, inte övertaget

`ScriptedClock`-fri ren aritmetik: 6 siffror = 10⁶. 3 försök/utmaning, 10 mints/24 h per adress ⇒ **30 gissningar/dygn**.
- per dygn per angripen adress: 1 − (1 − 10⁻⁶)³⁰ = **3,00·10⁻⁵ ≈ 0,003 %** — epikens siffra **stämmer**.
- **uthålligt över ett år: 1,089 % per adress.** Den siffran saknas i epiken och är den som styr.
- **≈92 konton** ⇒ ett förväntat övertagande/år om varje konto angrips uthålligt.

**Räcker det mot avvisningen i `DependencyInjection.cs:1658-1676`?** Ja — men **bara** för att det lastbärande ordet i avvisningen är *stateless*, inte *6 siffror*. Dagens TOTP-avvisning gäller en provider utan försöksräknare: 10⁶ / 20 per min (`AuthWrite`) = ~35 dygn från **en** IP, timmar från hundra. D1 byter obegränsat mot 30/dygn. **ADR 0142 ska skriva reversalen som en reversal av *statelösheten*** och binda att utmaningsvägen **aldrig** går genom Identitys `opts.Tokens`-providers — en återaktivering av "Email"-providern återinför exakt den egenskap som avvisades.

**Lapse-villkor för budgeten (ADR 0142 ska lista dem och kräva ny mätning vid var och en):**
1. `Auth:RegistrationsOpen=true` utanför Development (#734-flippen) — öppen registrering gör adressmängden oavgränsad.
2. **Den första registrerade som inte är Klas** (samma trigger som ADR 0132; den ärvs inte).
3. **Kontoantalet passerar ~90** — då är 1,089 %/år ≥ 1 förväntat övertagande/år under uthålligt angrepp.
4. **En IdP går live** (6a) — premissen "mail är enda vägen in" faller, både DoS-ytan och länkningsytan ändras.
5. Kodlängd, försöksantal eller mint-budget ändras i någon riktning.
6. **5b landar** (ingen lösenordsfallback kvar).

---

### G/D4. Rättsgrundsanalys att transkribera — stänger #1494

ADR 0142 ska bära denna text (den är skriven för att kopieras):

> **Frågan.** ADR 0018 `Amendment 2026-07-05` gjorde persistent login till ett opt-in med hänvisning till Art. 25(2) och till en "ADR 0093" som aldrig skrevs (#1494). D4 vänder valet till persistent-by-default på controller-beslut (Klas 2026-09-16). Grunden nedan är den som saknades.
>
> **1. Kakans existens har aldrig varit frågan.** Sessionskakan är *strikt nödvändig* för en tjänst användaren uttryckligen begärt — ePrivacy Art. 5(3)/LEK 6 kap. 18 § andra stycket, WP29 WP194 kriterium B (authentication cookies). Inget samtycke, ingen banner. Det som 2026-07-05 låg till grund för opt-in är **varaktigheten**: WP194 §3.2 undantar autentiseringskakan som *sessionskaka*, och behandlar en kvarstående "kom-ihåg-mig"-kaka som något som kräver en informerad, positiv handling av användaren.
>
> **2. Den positiva handlingen finns kvar, den har bytt form.** Under passwordless är själva inloggningen en aktiv handling per enhet (adress + kod ur egen inkorg). Kravet uppfylls om — och bara om — persistensen **uppges där handlingen görs**: kodsteget och samtyckessteget säger i klartext att man förblir inloggad på enheten i upp till 180 dagar och att Logga ut är kvar på varje sida. Detta är en argumenterbar position, inte en självklar; ADR 0142 ska skriva den som argumenterbar.
>
> **3. Art. 25(2) besvaras, inte kringgås — för att ändamålet har bytts ut.** 25(2) mäter nödvändighet mot ändamålet, inklusive lagringstid. Under lösenord kostade en ominloggning noll extra behandling, och 24 h var därför det minimerande valet. **Under D4 kostar varje ominloggning ett mejl med adressen genom biträdet (Scaleway, fr-par), en ny PII-post i Redis, och en klickträning på inloggningslänkar som ökar phishing-känsligheten.** En 24-timmarsdefault innebär ≈1 kodmejl per användare och dygn; 180 dagar innebär ≈1/180. Persistens är därmed **det dataminimerande valet under det nya ändamålet** — och det är precis den omvändning 25(2) kräver att man motiverar snarare än påstår.
>
> **4. Proportionaliteten bärs av levererade, mätta kontroller.** `Persistent` = 30 d **glidande** fönster + 180 d absolut tak + rotation av session-id var 24:e timme (`SessionStoreOptions.cs:39-43`); `__Host-` + HttpOnly + Secure + SameSite=Strict (`session.ts:88-93`); serverside-återkallelse med per-user-index och tombstone. **Den effektiva defaulten för en inaktiv enhet är 30 dagar, inte 180** — och 30, inte 180, är talet analysen ska föra fram.
>
> **5. Residualen, namngiven.** En användare på delad dator som inte loggar ut är inloggad i upp till 30 dagars inaktivitet, där hen tidigare loggades ut vid webbläsarstängning. Remedy är **copyn plus Logga ut på varje sida**, och copyn (`content-legal.json` rad 330 + inloggningssidan) landar i **samma PR som `setSessionCookie(id, true)`** (del 2, per CTO:ns formuleringsrättelse). Beslutet fattas under Art. 24(1) med risken utskriven; det är inte ett påstående om att opt-in var fel.
>
> **6. ADR 0018 rättas, inte bara upphävs.** Amendmentets **två** `ADR 0093`-pekare (rad 155 och 179) pekar på en fil som inte finns. ADR 0142:s 0018-amendment ska **stryka eller peka om båda** — ett upphävt beslut med död pekare till sin motivering är sämre än endera.

---

### Blockers

1. **D10:s dev-seam namnger inte VILKEN tvågrind — och grannrutten i samma fil är PÅ i produktion** — Fil: `src/Jobbliggaren.Api/Endpoints/DevEndpoints.cs:20-82`, `src/Jobbliggaren.Api/Program.cs:481-496`
   Nuvarande: epiken säger *"under the same two-gate pattern as `IDevEmailConfirmer`"*. Filen bär **två** mönster med olika ändringsskäl: `MapDevEnvironmentOnlyEndpoints` (endast `IsDevelopment()`, ingen flagga får vidga) och `MapDevResetMyDataEndpoint` (konfigurationsgrindad, avsiktligt påslagbar utanför Development — och `DevTools:EnableResetMyData` är **påslagen på lådan sedan 2026-08-29**). Filens egen doc säger att splitten finns just för att `confirm-email` inte ska ligga ett `||` från att återarmeras.
   Krävs: ADR 0142 namnger **`MapDevEnvironmentOnlyEndpoints` och `AddDevOnlyTestingSupport`** ordagrant för `POST /api/v1/dev/login-code`, förbjuder varje flaggväg, och binder att `ProductionStartupSmokeTests` (`tests/Jobbliggaren.Api.IntegrationTests/Configuration/ProductionStartupSmokeTests.cs:187-230`) får ett par för den nya rutten i **båda** polariteterna av `DevTools:EnableResetMyData` — samma form som `confirm-email` redan har.
   Motivering: rutten delar ut en levande inloggningskredential för **godtycklig adress, oautentiserat**. Fel grind = total auth bypass på `jobbliggaren.se`. Art. 32(1). Det som blockeras är ADR-texten; en mening urladdar den.
   Delegera till: adr-keeper (texten), dotnet-architect + test-writer (1a)

---

### Major

1. **Mint-budgeten måste prövas FÖRE bränningen, annars är den en tredjeparts-DoS mot inloggning** — Fil: D1/D2 (ingen kod)
   Nuvarande: "en levande utmaning per adress (ny mint bränner föregående)" + "konsumenten skriver alltid en post" + budget 3/10 min, 10/24 h. Ordningen är oskriven.
   Krävs: konsumenten prövar `IRateBudget` **först**; en över-budget-mint är en **no-op som lämnar den levande utmaningen orörd** och skriver ingen ny post. Budgeten konsumeras dessutom **existensoberoende**, före kontouppslaget — annars brinner den olika fort för känd/okänd adress och frånvaron av ett fjärde mejl blir ett enumerationsorakel (samma skäl `CooldownScopes.PasswordReset` är tyst).
   Motivering: utan ordningen kan vem som helst som känner adressen (a) bränna den kod offret just fått genom att posta adressen igen, och (b) tömma 10 mints/24 h så offret **inte kan logga in alls** resten av dygnet — efter del 5 finns ingen annan väg in. Uniform 202 gör att offret ser "vi har skickat en kod" och inget kommer. Art. 32(1) tillgänglighet.
   Delegera till: dotnet-architect (1a)

2. **`codeHash` och `emailEncrypted` bär två olika hotmodeller i samma post** — Fil: D1
   Nuvarande: adressen DataProtector-krypteras (skyddar mot en Redis-läsare), koden hashas. En 6-siffrig preimage är 10⁶ — en osaltad hash faller offline på mikrosekunder, så `codeHash` skyddar **ingenting** mot samma läsare. En som kan läsa Redis äger alltså varje levande inloggning, medan adressen förblir skyddad.
   Krävs: ADR 0142 väljer **en** hotmodell och skriver den. Antingen (a) en Redis-läsare ingår — då skyddas koden med **samma DataProtector/keyring** (en `Protect`-anrop till) eller HMAC:as med en serverside-nyckel; eller (b) den ingår inte — då skrivs varför adressen ändå krypteras (PII-konfidentialitet + Art. 5(1)(f), inte kontoskydd) och att `codeHash` är en **missbruksspärr inuti appen** (ingen väg att återutsända eller logga koden), aldrig en konfidentialitetskontroll. Ett ADR som antyder det senare är en falsk skyddsuppgift.
   Delegera till: dotnet-architect (1a)

3. **Adressnormaliseringen får inte kopieras till tre nya ställen** — Fil: `src/Jobbliggaren.Infrastructure/Auth/RedisCooldownGate.cs:43-70`
   Nuvarande: `Trim().Normalize().ToUpperInvariant()` bor som privat `Key` i en klass. Designen skapar **tre** nya hashningsställen: `LoginChallenge`-cooldownen, `IRateBudget`-budgeten och "en levande utmaning per adress"-nyckeln. Arkitekten binder "exakt samma normalisering" — en kopia är två hem för U+017F/NFD-fixen.
   Krävs: **ett** hem. Normaliseraren lyfts till en intern delad funktion som den skeppade gaten delegerar till (mekanisk, noll beteendeändring, pinnad av befintlig `RedisCooldownGateTests`-svep), och varje nytt ställe kallar den. Svepet utökas till de nya nycklarna.
   Motivering: en glömd `ToUpperInvariant` ger 2^k oberoende fönster/budgetar för samma konto och återöppnar en stängd bypass tyst. Filens egen kommentar säger att egenskapen ska **mätas i den runtime som skeppar**, inte antas.
   Delegera till: dotnet-architect (1a)

4. **#706 stängs medan samma form återöppnas en rutt bort — och edge-scrubbens pin är blind för den** — Fil: `tests/Jobbliggaren.Architecture.Tests/CaddyfileTokenScrubbingPinTests.cs:151-193`, `deploy/caddy/Caddyfile:61-71`
   Nuvarande: pinnen härleder parameternamnen ur en **handskriven lista om tre** `EmailTemplates`-metoder, och `TokenLink`-regexen hårdkodar tre sökvägar (`bekrafta-epost|bekrafta-konto|aterstall-losenord`). `/logga-in/lank?token=` matchar ingen av dem — faktumet passerar **vakuöst** i båda riktningar. Värre: **5a raderar två av de tre** kvarvarande mallarna, så mängden krymper mot tom, och en assertion över tom mängd passerar.
   Krävs: (i) den nya mallen och `/logga-in/lank` in i `RenderedLinks()` + `TokenLink` **i samma PR som 1a**; (ii) parametern stavas exakt **`token`** — filtret är skiftlägeskänsligt (mätt 2026-08-29, caddy 2.11.4); (iii) 5a får inte lämna faktumet tomt — en `ShouldNotBeEmpty` på den härledda mängden; (iv) ADR 0142 skriver **varför en inloggningstoken i en URL accepteras där #706:s byt-e-post-token inte gjordes** (15 min mot 24 h, engångsbruk, delad post som bränner båda, edge-scrub, `no-referrer`, `no-store`) — annars är #706:s stängning kosmetisk.
   Delegera till: dotnet-architect + test-writer (1a), docs-keeper (#706-noten i 3a)

5. **OAuth-callbackens state-cookie måste vara OBLIGATORISK, inte kompletterande** — Fil: D8
   Nuvarande: "state i en `SameSite=Lax` `__Host-`-cookie ≤10 min" + "PKCE-verifier i Redis". CTO-bind 4 beskriver de två som olika roller men säger inte att någon är **krav**.
   Krävs: callbacken **vägrar** när state-cookien saknas eller inte matchar — aldrig fallback till enbart Redis-posten. Redis-posten konsumeras med `GETDEL`. `Lax` (inte `Strict`) är korrekt och skälet skrivs ut: callbacken anländer som cross-site-initierad top-level-navigering och en Strict-cookie skulle inte skickas, så state-valideringen skulle falla — en framtida "härdning" till Strict bryter flödet.
   Motivering: **login CSRF.** En angripare fullbordar sitt eget IdP-flöde, fångar sin `code`+`state` och matar offret callback-URL:en. Offret har ingen cookie ⇒ måste avvisas, annars loggas offret tyst in på angriparens konto och skriver sitt CV där. Art. 32(1).
   Delegera till: dotnet-architect (6a)

6. **Ingen kontorad får skrivas före samtyckesposten — på BÅDA vägarna** — Fil: D3/D6/D8
   Nuvarande: `complete` skapar användare + `JobSeeker.Register(TermsAcceptance)` (bra). Men "första OAuth-inloggning återanvänder samtyckessteget" säger inget om ordningen, och den naturliga implementationen skapar Identity-raden vid callbacken och frågar om villkoren efteråt.
   Krävs: ADR 0142 binder att **ingen** `AspNetUsers`- eller `job_seekers`-rad skrivs innan acceptansen finns, på kod- såväl som OAuth-vägen; den externa identiteten hålls i **grant-posten** och förfaller med dess TTL om användaren avbryter. Därmed blir `terms_accepted_at` **NOT NULL för varje rad skapad efter 1b** — inte bara "obligatorisk i factoryn", vilket är en konvention en andra skrivväg kan kringgå (§2.2).
   Motivering: GDPR-korrektheten i frågan — ja, OAuth-förstagången **måste** gå genom samtyckessteget; avtalet ingås inte för att man kom via Google. Innehavet av `sub` + adress före acceptans är lagligt som **Art. 6(1)(b) andra ledet** (åtgärder på den registrerades begäran före avtal) — den grunden ska citeras, och den håller bara så länge inget skrivs durabelt.
   Delegera till: dotnet-architect (1c), db-migration-writer (1b)

7. **D4:s rättsgrund saknas i utkastet** — Fil: ADR 0142 (obefintlig), `docs/decisions/0018-cookie-and-csrf-strategy.md:155,179`
   Nuvarande: epiken säger att analysen "recorded in ADR 0142" utan att skriva den; ADR 0018:s två `ADR 0093`-pekare är döda (#1494).
   Krävs: sektionen **"G/D4. Rättsgrundsanalys att transkribera"** ovan, ordagrant, samt att 0018-amendmentet stryker/pekar om **båda** pekarna.
   Motivering: Art. 25(2), ePrivacy Art. 5(3), Art. 5(2) accountability. #1494 stängs av analysen, inte av beslutet.
   Delegera till: adr-keeper (del 0)

8. **Klas svar flyttade kryssrutan efter koden — då måste `/logga-in` själv bära insamlingsnotisen, och rutan får inte säga "godkänner integritetspolicyn"** — Fil: `web/jobbliggaren-web/messages/sv/content-legal.json:32,165`
   Nuvarande: adressen samlas in i steg 1; villkorstexten ligger i steg 3. Idag bor Art. 13-notisen i `/registrera`-kryssrutan — som Klas svar flyttar bort från insamlingspunkten.
   Krävs: **ingen ny separat notis** — samma länkrad som kryssrutan redan bär flyttas till `/logga-in` under e-postfältet ("Så behandlar vi din e-postadress: integritetspolicyn"). Art. 13(1) kräver informationen *när uppgifterna samlas in*.
   Och: **`terms_accepted_at` är rätt fältnamn** — detta är avtalsingående (Art. 6(1)(b)), inte samtycke i Art. 7-mening, och `consent_*` skulle antyda en återkallelserätt (Art. 7(3)) som inte finns. Men `privacy_policy_version` stämplar en **Art. 13-notis**, inte något som accepteras: kryssrutan godkänner **villkoren**, och integritetspolicyn **länkas och anges som läst** — aldrig "godkänner … och integritetspolicyn". ADR 0142 skriver att `privacy_policy_version` är en notisversionsstämpel för Art. 5(2), inget acceptansfaktum.
   Delegera till: nextjs-ui-engineer (del 2), adr-keeper (del 0)

9. **Art. 30-posten, grannbulletens påstående, och keyringens utskrivna blast radius** — Fil: `docs/runbooks/gdpr-processing-register.md:1329-1400` (gitignorerad), `src/Jobbliggaren.Infrastructure/DependencyInjection.cs:1576-1587`
   Krävs, allt i samma PR som 1a:
   (a) **Ny bullet under `Datafält` i "Behandling: Användarkonto och autentisering"**: *Inloggningsutmaning (Redis, `auth/challenge/v1/*`, `auth/grant/v1/*`, `auth/oauth-state/v1/*`)* — DataProtector-krypterad e-postadress, `codeHash`, `linkTokenHash`, grenflaggor; TTL 15 resp. 10 min, självutgående, ingen reaper. Egen rad under **Retention**.
   (b) Den befintliga `Sessioner`-bulleten säger att nyttolasten bär **enbart** icke-PII. Den blir falsk genom närhet om (a) uteblir — den får en klausul *"detta gäller sessionsposten, inte utmaningsposten"*. Riktningen är densamma som `organization_number`-korrigeringen: Art. 30(1)(c) underskattning.
   (c) `AddApiDataProtection`:s doc säger att keyringens blast radius är **tre token-KINDS**. D1 gör keyringen till skydd för **personuppgift** (adressen), och 5a tar bort två av de tre. Meningen blir felaktig — en faktiskt felaktig kommentar är en defekt (AGENTS.md §5). Rättas i 1a. Nycklarna persisteras dessutom **oskyddade på filsystemet** (`PersistKeysToFileSystem`, ingen `ProtectKeysWith*`) — ADR 0142 skriver att den som läser keyring-volymen därmed läser varje levande utmanings adress, och att CTO-bind 3:s mätning av `DataProtection__KeyPath` på lådan är 1a:s DoD.
   (d) **Copyn följer datat, aldrig före.** Att stryka "lösenord (hash)" (rad 32) medan `password_hash` fortfarande bär hashar är ett falskt transparensutsagt — Art. 5(1)(a)/12(1). "lösenord (hash)" stryks i **5b**, inte 5a; "visningsnamn" i **4b**, inte 4a. Samma för registrets `Datafält` och för dess `Retention`-mening *"operationen BÄR en credential (lösenordet)"*, som D5 falsifierar i **3a**.
   Delegera till: docs-keeper + adr-keeper (respektive del)

10. **IdP:erna är självständiga personuppgiftsansvariga, och 6a falsifierar tredjelandsmeningen** — Fil: `content-legal.json` ("Mottagare av uppgifter", "Överföring till tredje land")
    Nuvarande: policyn säger *"I dagsläget sker inga överföringar av dina personuppgifter till länder utanför EU/EES."* Rad 32 utlovar redan "identifierare från en extern inloggningstjänst".
    Krävs, i **samma PR som första levande provider**: (i) IdP:erna in under Mottagare som **självständiga controllers, inte biträden** — vi lämnar ut att en person autentiserar mot Jobbliggaren, och tar emot `sub` + verifierad adress (Art. 13(1)(e) + källangivelse); (ii) tredjelandsavsnittet skrivs om; (iii) **Kap. V besvaras per provider med en daterad mätning, inte ett antagande** — EU-etablering (Google Ireland, LinkedIn Ireland, Microsoft Ireland) och/eller DPF-adekvans (Art. 45) verifieras enligt CLAUDE.md §9.5 vid varje adapters PR och skrivs i registret; **en provider som inte täcks skeppas inte**. Art. 49-derogation duger inte: en inloggningsväg är varken tillfällig eller icke-repetitiv.
    Delegera till: security-auditor via 6a-panelen; nextjs-ui-engineer (copy), docs-keeper (register)

11. **Lockout-DoS:et stängs i 1c, och 5b måste rotera `security_stamp`** — Fil: `src/Jobbliggaren.Infrastructure/Auth/UserAccountService.cs:129-137`, `:346-372`
    Nuvarande (mätt): `CheckPasswordAsync` misslyckas på null-hash och rad 133 kör ändå `AccessFailedAsync` ⇒ vem som helst som känner adressen låser ett lösenordslöst konto i 15 min. Och `TryPreparePasswordResetAsync` + `ResetPasswordAsync` **ger ett lösenordslöst konto ett lösenord**.
    Krävs: (i) `if (user.PasswordHash is null) return InvalidCredentials;` **före** `IsLockedOutAsync`/`CheckPasswordAsync` — i **1c**, inte 5a, eftersom fönstret 1c→5a är där passwordless-konton samexisterar med ett levande `/auth/login`. Byte-identiskt svar, inget nytt orakel. (ii) Arkitektens rad 358-gate i samma del. (iii) ADR 0142 binder att utmaningsvägen **aldrig** kallar `IsLockedOutAsync`/`AccessFailedAsync` — dess anti-automation är 3-försöksbränningen + mint-budgeten, punkt. (iv) **5b roterar `security_stamp` i samma sats** som nollningen, och dess `Down` är en explicit `throw`, aldrig en tom metod som ser reverterbar ut.
    Motivering: inte Blocker eftersom 5a river redemption-ytan före 5b — men utan (iv) överlever varje utestående reset-token nollningen som ett latent återarmeringsspår.
    Delegera till: dotnet-architect (1c), db-migration-writer (5b)

12. **Boot-vägran på mailkapabilitet måste tappa sitt `RegistrationsOpen`-villkor** — Fil: `docs/runbooks/gdpr-processing-register.md` ("Trail (levererad)"), D10
    Nuvarande: den levererade regeln vägrar boot utanför Development/Test *"på öppen registrering med bekräftelsekrav när den registrerade avsändaren inte kan leverera"* — villkorad på **öppen registrering**. Lådan kör stängd registrering.
    Krävs: efter 1a behövs mejl för **inloggning**, inte bara registrering, så villkoret faller: boot vägras utanför Dev/Test när avsändaren inte kan leverera, **oavsett** `RegistrationsOpen`. Regeln fortsätter fråga avsändarens **kapabilitet** (`CanDeliver`), aldrig `Email:Provider`-nyckeln. Ny fail-fast-nyckel ⇒ CLAUDE.md §11:s dev-boot-kontrakt (`appsettings.Local.json.example` + `local-dev-setup.md` i samma PR) gäller även `Auth:LoginChallengeDispatch:Capacity` och budgetfönstren.
    Delegera till: dotnet-architect (1a), docs-keeper

13. **Dev-seamen måste återanvända `IsReservedRecipient`, inte kringgå den** — Fil: `src/Jobbliggaren.Infrastructure/Email/ConsoleEmailSender.cs:130-220`
    Nuvarande: #1208:s grind sitter i `WriteEmail` och håller kroppen borta för varje icke-reserverad mottagare. En dekoratör som fångar `email → last code` fångar för **alla** mottagare och serverar dem över HTTP — den återöppnar exakt det #1208 stängde, ett lager upp.
    Krävs: dekoratören anropar **samma medlem** `ConsoleEmailSender.IsReservedRecipient` (ett hem, aldrig en kopia); en icke-reserverad mottagare fångas inte och endpointen svarar 404 för den. Fångsten är storleks- och TTL-begränsad och håller **koden**, aldrig kroppen. Grindens egen doc-mening om producentmängden ("a complete cut") blir falsk om en andra konsument av samma kod uppstår utanför den — den meningen uppdateras i samma PR.
    Delegera till: dotnet-architect (1a)

---

### Minor

1. **Länk-token och kod måste ha skild försöksbokföring** — delad post är rätt (en kredential, en budget), men ett felaktigt `linkTokenHash` får inte konsumera kodens 3 försök: 128 bitar behöver ingen försöksbudget, och en scanner som POST:ar skulle annars bränna användarens kod. Och för `isNewAddress=true` är `linkTokenHash` **null**, så `/auth/link` inte kan lyckas by construction (magic link bara för befintliga konton — försvaret ska ligga i posten, inte i att mejlet inte innehöll länken).
2. **Primitiver som ska stå i ADR:n:** koden mintas med `RandomNumberGenerator` med rejection sampling, aldrig `Random` och aldrig `% 1_000_000` utan avvisning; PKCE endast `S256`, aldrig `plain`; varje IdP-adapters verified-claim parsas **fail-closed** (LinkedIn returnerar `email_verified` som sträng i vissa svar — saknad eller oparsbar claim = *inte* verifierad); GitHub endast `/user/emails` med `primary && verified` (CTO-bind 5).
3. **Token ur URL:en efter ett hopp (skriv beslutet, oavsett riktning).** GET läser `?token=`, sätter den i en kortlivad `__Host-`-httpOnly-cookie och 303:ar till bar sökväg; formuläret POST:ar utan token. Fungerar med JS av, till skillnad från `history.replaceState`. Om den enklare formen väljs ska ADR:n skriva att en levande token ligger kvar i webbläsarhistoriken i upp till 15 min. Oavsett: `Cache-Control: no-store` gäller **även POST-svaret**, och `Referrer-Policy: no-referrer` på rutten måste **mätas** mot den globala `/(.*)`-regeln (`security-headers.ts`, `strict-origin-when-cross-origin` skickar hela URL:en på **same-origin**-subresurser) — en route-regel som inte vinner är en regel som inte finns.
4. **Små bindningar:** `__Host-jobbliggaren_login` är `SameSite=Strict` och det skrivs ut (länkvägen bär sin egen token och behöver den aldrig — annars "fixar" någon länkvägen genom att läsa cookien). CTO:ns cookie-bind har ett skäl till som bör stå: läser `complete` cookiens adress skapas kontot på **fel adress** när användaren bytt adress i en andra flik — det är en nåbar bugg, inte bara två hem. `pendingDeletion` återställer aldrig kontot genom inloggning (30-dagarsklockan rörs inte). Registret får en rad om att utmaningsposten är praktiskt onåbar för Art. 15/17 eftersom den förfaller inom svarstiden. 0.5:s bootstrap stannar i testassemblyn och blir aldrig en `IDev*`-port. **Ingen DPIA krävs** (Art. 35(3)(a)–(c) alla negativa) och ADR 0142 skriver den slutsatsen med skälet, för DoD punkt 8.

---

### Praise

- **D2:s "request path never reads the account" är starkare anti-enumeration än dagens inloggning** — den uppnår tidsparitet *by construction* i stället för med `LoginTimingEqualizer`:s PBKDF2-kompensation; bind att inget får lägga ett kontouppslag där. ✓
- Always-write-a-record, uniform 202 för känd/okänd/kyld/budget-slut, och "grena först efter bevisad inkorg" är **inget orakel** — den som bevisat innehav är den registrerade och har rätt till svaret. ✓
- Redis-nere → uniformt 503 speglar `Program.cs:304`; `VerifiedEmail`-typen, purpose-enum inuti `Redeem` och `ILoginChallengeDispatcher`-`void` gör tre bind sanna *av konstruktion*. ✓

---

### Sammanfattning

1 Blocker, 12 Major, 4 Minor — samtliga är **bindningstext ADR 0142 ska bära före kod**, inte fynd i levererad kod. Blockern är en mening (namnge grinden). Två Major är rena dokumentplikter (D4-analysen, Art. 30-raden), resten är portar och ordningsvillkor i 1a/1c/3a/5b/6a. Re-review efter fix: samma agent, report-only, scopad till fix-deltat (CLAUDE.md §9.6). Inga filer redigerade, inga issues filade.

---

### Break-glass — säkerhetskostnad per form (underlag till Klas; ingen rekommendation, valet är hans)

**(a) Ops-gated engångskod i lådans logg.** Grinden kan inte vara `IsDevelopment()` (lådan kör Production) och inte en vanlig flagga — `DevTools:EnableResetMyData` är själva exemplet på en flagga som står kvar påslagen. Och koden landar då i **två loggsänkor**: Seq på lådan i **klartext, 30 dagars retention**, och Docker `json-file` som enligt behandlingsregistret har **ingen åldersgräns alls och odefinierad Art. 17-position**. Formen är alltså inte "en kod i en logg" utan en inloggningskredential persisterad i en sänka utan raderingsrutin. Kräver: engångsbruk, mycket kort TTL, audit-rad, och medveten uteslutning ur båda sänkorna — plus egen motivering eftersom den passerar #1208:s mottagargrind helt.

**(b) Andra mailprovider som fallback.** Kräver ett andra Art. 28-biträdesavtal, en rad i behandlingsregistret **och** i integritetspolicyns Mottagare-avsnitt, en Kap. V-kontroll, egna credentials på lådan med egen rotation, samt en failover-regel som inte dubbelsänder — den sista finns redan (claim-then-send + `ICooldownGate`, ADR 0103). Kostnaden är nästan helt **compliance-yta**: det är den enda av de tre som lägger till en ny mottagare av varje användares adress. Säkerhetsmässigt den renaste — ingen ny kredentialklass, ingen ny bypass.

**(c) Lösenord kvar enbart för adminkontot.** Kräver att 5b blir "nolla alla utom en", varmed invarianten blir *"passwordless utom ett konto"* — ett villkorat påstående ingen billig test kan uttrycka. Och det kräver att **hela lösenordsytan** lever vidare: `/auth/login`, `ValidateCredentialsAsync`, lockout-vägen, `PwnedPasswordValidator` och lösenords-UI:t. Alltså faller inte bara 5b utan **5a**. Den återinför dessutom lockout-DoS:et (Major 11) på exakt det konto vars tillgänglighet break-glassen finns för att skydda. Högst säkerhetskostnad; enda formen som varken behöver mejl eller logg.

**Eskalering till Klas:** ja — CTO:ns tre obesvarade D10-frågor står kvar och ADR 0142 kan inte skrivas färdig utan dem: (1) accepterar du att ett mailavbrott = totalt inloggningsstopp, inklusive för dig själv? (2) vill du ha en break-glass, och i så fall form (a), (b) eller (c) — säkerhetskostnaden per form står i blocket direkt ovan och ska transkriberas oavkortat med frågan? (3) ska del 5b (nollningen av `password_hash`) köras före lansering, eller ligger lösenordsvägen kvar inaktiv men intakt tills OAuth är live? Jag lägger till en fjärde, som är min och inte hans: **de tre svaren måste in i ADR 0142 innan del 1a öppnas**, eftersom Major 11 och Major 12 (var lockout-hålet stängs, och att boot-vägran tappar sitt `RegistrationsOpen`-villkor) får olika rätt svar beroende på om en break-glass finns.

---

## Scoped re-check (report-only, 2026-09-17, against commit `9d591eb5` on PR #1748)

> Transcribed verbatim by the driving session. Scope: the agent's own Blocker, Major 1–13 and Minor 1–4 against ADR 0142 + the 0017/0018 amendments. No files edited by the agent, no issues filed. Disposition of the two partly-open rows and the 0018 sentence is in the PR #1748 verdict table.

## Security-audit: omkontroll av fix-deltat 9d591eb5 (PR #1748, ADR 0142)
**Status:** ✓ Approved för deltat — 0 nya Blocker/Major. Två rader står **delvis öppna** (Major 3, Minor 3); Minor blockerar inte merge, Major 3 gör det.
**Auktoritet:** CLAUDE.md §9.6 (rapport-läge, skopat till deltat) · min rapport `C:/tmp/jbl-1733/docs/reviews/2026-09-17-auth-epic-security.md`

Delta mätt: `git show 9d591eb5 --stat` → 4 filer, +675/−7. ADR:n är 612 rader, `git ls-files` bekräftar att den är spårad (främjad).

### Fynd → status

| # | Fynd | Status | Var i ADR 0142 |
|---|---|---|---|
| **B1** | Dev-seamens grind namnges | **stängd** | `### D10` — `MapDevEnvironmentOnlyEndpoints` + `AddDevOnlyTestingSupport` ordagrant, "never `MapDevResetMyDataEndpoint`", "No flag may widen it", smoke-test-par i **båda** polariteterna |
| **M1** | Budget före bränning, existensoberoende | **stängd** | `### D2` + `## Attempt budget` ("only after the budget admits it") |
| **M2** | Hotmodell för `codeHash` | **stängd — val (a)** | `### D1`, "Threat model, chosen": Redis-läsare **ÄR** i scope, koden skyddas med samma DataProtector-purpose som adressen, bara 128-bitars länktoken hashas. Postformen är omskriven till `codeProtected`; `codeHash` grep:ar till **noll** i ADR:n. Valet är det starkare av mina två och stänger raden rent |
| **M3** | Ett hem för normaliseraren | **delvis öppen** | `### D1` bär ETT hem + att varje ny plats kallar den + `ICooldownGate.cs:16`-rättelsen. **Saknas:** min klausul "svepet utökas till de nya nycklarna" — `RedisCooldownGateTests` grep:ar till noll i ADR:n. Egenskapen är bunden men dess pin är inte; utan pin kan en ny hashningsplats gå förbi hemmet och inget fångar det. Ingen namngiven avvikelse, alltså utelämnad snarare än avfärdad. **Stängs av en mening i 1a:s DoD** |
| **M4** | Edge-scrub-pinnen + #706-skälet | **stängd** | `## Page form` → "Link landing": (i) ny mall + `/logga-in/lank` in i `RenderedLinks()`/`TokenLink` i 1a, (ii) `token` exakt, skiftlägeskänsligt, (iii) `ShouldNotBeEmpty` i 5a, (iv) skälet utskrivet |
| **M5** | State-cookien obligatorisk | **stängd** | `### D8` — "mandatory, not complementary", callbacken **vägrar** utan match, ingen fallback till Redis-posten, `GETDEL`, Lax-skälet utskrivet |
| **M6** | Ingen kontorad före samtycke, båda vägarna | **stängd** | `### D3` (bägge vägarna + Art. 6(1)(b) andra ledet) och `### D6` (NOT NULL för varje rad efter 1b som skrivvägsegenskap, inte factory-konvention) |
| **M7** | D4-rättsgrunden + båda 0093-pekarna | **stängd** | `### D4` + `0018` `Amendment 2026-09-17`. Se verbatim-mätningen nedan; båda pekarna (rad 155, 179) ompekade |
| **M8** | Art. 13-notisen följer insamlingspunkten; `privacy_policy_version` är notisstämpel | **stängd** | `### D6` ("no new separate notice") + `## Page form` (hintraden på `/logga-in`; kryssrutan godkänner **villkoren**, policyn i syskonmening) |
| **M9 (a–d)** | Art. 30-posten, grannbulleten, keyringens blast radius, copy-följer-data | **stängd** | `## Processing register and DoD 8` (a: egen `Datafält`-bullet + egen `Retention`-rad; b: `Sessioner`-klausulen; d: 5b/4b/3a). (c) i `### D1`: "three token kinds" rättas i 1a, `PersistKeysToFileSystem` utan `ProtectKeysWith*` utskriven, `DataProtection:KeyPath` mätt på lådan = 1a:s DoD |
| **M10** | IdP:er som självständiga controllers + Kap. V per provider | **stängd** | `### D8` sista stycket — alla tre leden, "a provider that is not covered does not ship", Art. 49 avvisad |
| **M11** | Lockout-DoS i 1c + `security_stamp` i 5b | **stängd** | `### D3` (null-hash-gaten **före** `IsLockedOutAsync`, `TryPreparePasswordResetAsync` → null, utmaningsvägen rör aldrig lockout) + `## Implementation status` (5b roterar `security_stamp` i samma sats, `Down` explicit throw) |
| **M12** | Boot-vägran tappar `RegistrationsOpen` | **stängd** | `### D10` — ordagrant, `CanDeliver` kvar som frågan, nya fail-fast-nycklar under §11:s dev-boot-kontrakt |
| **M13** | Dev-seamen återanvänder `IsReservedRecipient` | **stängd** | `### D10` — samma medlem, 404 för icke-reserverad, storleks-/TTL-bunden, håller **koden** aldrig kroppen. *Delklausulen om `ConsoleEmailSender.cs:130`-kommentaren ("a complete cut of the producer set") **faller** — jag har mätt om den: cutten gäller skrivningar till sänkan, och dekoratören ligger under samma mottagargrind och skriver inte i sänkan. Meningen blir alltså inte falsk under ADR:ns val. Premissen håller inte; ingen fix, ingen issue* |
| **m1** | Skild försöksbokföring länk/kod, `linkTokenHash` null vid `isNewAddress` | **stängd** | `### D1` |
| **m2** | Primitiver (CSPRNG+rejection, S256, fail-closed claim, `/user/emails`) | **stängd** | `### D8` + `### D10` + budgettabellen |
| **m3** | Token ur URL:en efter hopp | **delvis öppen** | `## Page form` → "Link landing" valde den **enklare** formen (dolt fält, inget 303-hopp) och bär `no-store` på GET **och** POST samt `no-referrer` **mätt** mot den globala regeln. **Saknas:** det villkor jag knöt till just det valet — att en levande token då ligger kvar i webbläsarhistoriken i upp till 15 min. `histor` grep:ar till noll. Minor: blockerar inte |
| **m4** | Små bindningar (6 st) | **stängd** | `### D2` (Strict), `### D1` (cookie-ekot + den nåbara buggen), `### D3` (`pendingDeletion` återställer aldrig), `## Processing register` (Art. 15/17-raden + ingen DPIA med skäl), `### D9` (0.5 stannar i testassemblyn) |

### D4-analysen — mätt, inte bedömd
`diff` mellan blocket i min rapport och blocket under `### D4`: **identiskt, 13 rader, noll avvikelser.** Break-glass-blocket: identiskt, enda skillnaden är blockquote-markörer på tomrader. Båda ligger ordagrant där de ska.

### Eskaleringen och defaulten
CTO:ns tre frågor står ordagrant under `## Open — Klas decides`, oavkortat. Min **fjärde** punkt bärs — "the three answers must be in this ADR **before part 1a opens**" — men **översatt till engelska, inte ordagrant**. Substansen (bindningen till 1a, och att det är Major 11/12 som får olika rätt svar) är intakt; jag graderar det som buret.

**Är defaulten förenlig med min fjärde punkt?** Ja, men bara genom läsning, och ADR:n säger det inte själv. Defaultens rubrikmening är skriven "so the sequence is not blocked" och namnger bara 5b; min mening tre stycken ned grindar 1a. De är **kumulativa, inte motstridiga** — 0.5 (#1734) och 1b (#1736) är genuint oberoende av break-glass-svaret, så sekvensen är faktiskt inte blockerad, och 1a är grindad. Men den läsordningen är inte utskriven, och den som stannar vid rubrikmeningen öppnar 1a. Det är ett beslut om sekvens, inte om säkerhet, och det hör hemma i eskaleringen nedan snarare än som eget fynd.

### Noteringar utan gradering (inte mina rader)
- **Design B1(a) — bekräftas: inget orakel.** Fyra grunder, alla mätta mot ADR-texten: (1) konsumenten skriver **alltid** en post, så wrong/expired/burned skiljer aldrig känd från okänd adress; (2) den gren som faktiskt bär kontoexistens (kod+länk / bara kod / stängd / pendingDeletion) ligger bakom inkorgen och syns inte i verify-utfallet; (3) `ChallengeId` är ≥128 bitar och cookie-bunden, så bara den som mintade ser distinktionen — och hen har själv orsakat varje tillstånd den skiljer på; (4) dummy-jämförelse på varje väg stänger tidskanalen. Ett villkor jag skriver ut: en **över-budget**-mint är per M1 en no-op utan post, så innehavaren får `expired` i stället för `wrong`. Det avslöjar innehavarens egen budgetstatus, inte någon annans, och är därför inte heller ett orakel — men det är förutsättningen bekräftelsen vilar på och den bör inte tyst ändras.
- `0018:157` säger att ADR 0142 är "a local-only file per the ADR 0072 docs-privacy convention". Den är **främjad och spårad** (`git ls-files` träffar, och README-raden listar den). Faktiskt fel mening i spårad fil, inte formulering — code-reviewer/adr-keeper, inte min gradering.
- För verdikt-tabellens skull: min rapports sammanfattningsrad säger "12 Major" medan listan numrerar **13**. Listan är den rätta; talet i sammanfattningen är ett räknefel i min egen rapport, inget i deltat.

**Nya-i-deltat Blocker/Major:** inga.

**Eskalering till Klas:** ja — tre saker, varav de tre första är CTO:ns obesvarade D10-frågor som ADR:n korrekt bär som ÖPPNA (mailavbrott = totalt inloggningsstopp? break-glass (a)/(b)/(c)? ska 5b köras före lansering?), och en fjärde som är min: **ADR:n grindar 1a på dina svar i en mening, men dess egen default-rubrik är skriven "so the sequence is not blocked" och namnger bara 5b. Avgör explicit om del 1a (#1735) får öppnas innan svaren är i ADR:n.** Min position är oförändrad: Major 12 (boot-vägran) och Major 11 (var lockout-hålet stängs) landar i 1a respektive 1c och får olika rätt svar beroende på om en break-glass finns — 0.5 och 1b kan köras nu, 1a bör vänta.
