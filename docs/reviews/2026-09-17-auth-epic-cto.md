# Auth epic #1732 — part 0 decision round (senior-cto-advisor, 2026-09-17)

> Transcribed verbatim by the driving session from the agent's returned report. Brief the agent
> answered: the session's scratch files `1733-brief-context.md` + `1732-epic.md` (the epic body as
> filed, D1–D10, the parts table, #1733). Worktree `C:/tmp/jbl-1733` at HEAD `081e4c67`. The session
> re-ran the report's load-bearing measurements before transcribing (DropWrite at
> `PasswordResetDispatchChannel.cs:64`, `CreateProtector(` 0 hits in `src/`, `DataProtection:KeyPath`
> at `DependencyInjection.cs:1569/1609`, `AuthProvider`/`ProviderUserId` read only in
> `ApplicationUser.cs` + its configuration, `form-action 'self'` at `security-headers.ts:74`,
> `SessionStoreUnavailableException` → 503 at `Program.cs:304`, 187 `RegisterAndGetSessionIdAsync`
> references) — all confirmed. The escalation under "Eskalering till Klas" was put to Klas in plain
> text the same turn, not transcribed as settled.

## CTO-rekommendation — beslutsrunda #1733 (epik #1732, del 0)

**Vad jag mätte själv i C:/tmp/jbl-1733 @ 081e4c67** (allt nedan vilar på dessa avläsningar, inte på brieftexten):

| Påstående | Mätning | Utfall |
|---|---|---|
| Application refererar inget providerpaket | `Jobbliggaren.Application.csproj` | ✅ endast FluentValidation, Mediator.Abstractions, EFCore, DI.Abstractions |
| JwtBearer finns redan i Infrastructure | `Jobbliggaren.Infrastructure.csproj:13` + `using System.IdentityModel.Tokens.Jwt` i `CurrentUser.cs`, `SessionAuthenticationHandler.cs`, `RateLimitingExtensions.cs` | ✅ finns, och ADR 0017-amendment 2026-07-25 säger varför det stannar |
| 187 anropsplatser | `grep -rn RegisterAndGetSessionIdAsync tests/ \| wc -l` | ✅ **187**, alla mot `POST /api/v1/auth/register` över `HttpClient` |
| `form-action 'self'` | `security-headers.ts:74` | ✅ serveras på `/(.*)` |
| ADR 0018-invarianten | `0018-…md` "Backend trust model" | ✅ *"any relaxation of this topology … invalidates this CSRF analysis"* |
| Redis nere → 503 | `Program.cs:304-314` (`SessionStoreUnavailableException` → 503) | ✅ mönstret finns redan |
| `CreateProtector(` i src | `grep -rn "CreateProtector(" src/` | ⚠ **noll träffar** — D1 blir förste direktkonsumenten |
| DataProtection-keyring | `DependencyInjection.cs:1593-1611` | ⚠ persisteras **bara** om `DataProtection:KeyPath` är satt |
| `TryEnqueue` signalerar full kö | `PasswordResetDispatchChannel.cs:77-90` | ⛔ **nej** — `DropWrite` gör att `TryWrite` returnerar **true** vid full kö; droppen syns bara i `itemDropped`-loggen |
| `SITE_HOST` i src | repo-grep | ⚠ finns **bara** i `deploy/` — backendens publika-URL-SSOT är `EmailOptions.BaseUrl` (`docker-compose.yml:176` härleder den ur `SITE_HOST`) |
| `AuthProvider`/`ProviderUserId` läses någonstans | repo-grep | ✅ **oanvända** — bara deklaration, EF-konfiguration och migrationer |
| `IReauthenticatingRequest` bär sin egen motivering | filens XML-doc | ✅ *"a hijacked long-lived session (persistent login is now 180d) must not be able to run a sensitive operation without the password"* |
| `proxy.ts:93` dokumenterar "200 + dokument" p.g.a. SameSite | `proxy.ts:88-96` | ⛔ **nej** — den raden handlar om `Referer`-läckage vid page-level `redirect()` efter påbörjad streaming, inte om SameSite=Strict |

---

# D8 — OAuth-formen

### Beslut
**Variant B** — handrullad authorization-code + PKCE via `HttpClient` bakom Application-porten `IExternalIdentityProvider`, Next-route-handlers för start/callback. **Men motiveringen "inget JWT-paket" stryks ur ADR 0142** och ersätts (se nedan), och fem bind tillkommer.

### Motivering mot principer
- **ADR 0018:s invariant är inte en preferens utan en skriven rand.** Backend är inte browser-nåbar och sätter inga cookies som når browsern. Variant A:s remote-auth-handlers *kräver* båda: handlern äger callback-URL:en och sätter `.AspNetCore.Correlation.*`/nonce-cookies på svar som måste nå browsern. Att proxa callbacken genom Next räddar det inte — Next skulle då vidarebefordra backendens `Set-Cookie`, vilket är exakt det ADR 0018 förbjuder. A är alltså en topologiändring som kräver en **ny** CSRF-analys, inte en kodbesparing.
- **DIP + dependency rule (Martin 2017, kap. 11, 22).** B lägger porten i Application och HTTP-adaptern i Infrastructure. Precedensen finns redan mätt: `ScalewayEmailSender` är en handrullad HTTPS-API-arm utan SDK, och `NoAmazonReferenceTests` är den totalban som håller paketet borta. Samma form, andra domän.
- **CLAUDE.md §9.2 / BUILD.md §3.1.** B adderar noll top-level-beroenden. A adderar tre (`…Authentication.Google`, `AspNet.Security.OAuth.GitHub`, `.LinkedIn`) och binder oss till aspnet-contribs release-takt per .NET-major.
- **Rätt motivering för userinfo.** "Ingen id-token-validering → inget JWT-paket" **håller inte som skäl**: paketet finns redan (`Infrastructure.csproj:13`) och ADR 0017-amendment 2026-07-25 säger att det stannar för att det är det enda som ger Infrastructure dess `Microsoft.AspNetCore.App`-frameworkreferens. Vad userinfo *faktiskt* köper är **ingen JWKS-hämtning, ingen nyckelrotationscache och ingen egen signaturvalideringsväg** — en reell minskning av säkerhetskritisk yta. Skriv den meningen i ADR 0142; den gamla förfaller i samma stund någon mäter den (§5 `Comments:` — ett faktiskt fel motiv är en defekt).

### Avvisade alternativ
**Variant A (aspnet-contrib remote-auth):** bryter ADR 0018:s uttryckliga invariant (backend sätter browsernående cookies + äger en browsernåbar callback). Tre nya top-level-beroenden utan §9.2-motivering. Framework-kopplingen är dessutom fel riktning: handlern vill äga hela HTTP-dansen i en process som per design inte talar med browsern.

**Variant C (Arctic/oslo eller Auth.js-adapter i Next):** ADR 0017 avvisade Auth.js/Better Auth; C är samma flytt med mindre bibliotek. Den flyttar klienthemligheten och **identitetspåståendet** till transportlagret — backend skulle lita på sin egen proxys påstående om *vem* användaren är. Det gör Next till auktoritet över identitet i stället för transport, och `IExternalIdentityProvider` får inget att implementera. Avvisas på dependency rule, inte på biblioteksval.

### Bind (går in i ADR 0142, annars skeppar 6a trasigt)
1. **`/api/auth/oauth/{p}/start` får bara initieras av en `<a href>`-navigering — aldrig ett `<form>`, aldrig en Server Action.** `form-action 'self'` (mätt, `security-headers.ts:74`) hävdas av Chromium/Firefox **tvärs redirects**: en form-submit vars 302 pekar på `accounts.google.com` blockeras av CSP. Detta pinnas i 6a:s Playwright-test, inte i en kommentar.
2. **"200 + dokument"-formen är rätt, men citatet är fel.** `proxy.ts:88-96` handlar om `Referer`-läckage, inte om SameSite. ADR 0142 skriver SameSite-skälet i egna ord och citerar ADR 0018:s cookie-tabell.
3. **Ett hem för den publika bas-URL:en.** `redirect_uri` läser **samma** konfigurerade värde som `EmailOptions.BaseUrl` via en accessor — ingen ny `OAuth:RedirectBaseUrl`. Två nycklar garanterar att `redirect_uri` en dag avviker från IdP-konsolen, vilket felar hårt och tyst hos IdP:n.
4. **`state` i Lax-cookien och verifier i Redis är inte dubbellagring** — cookien binder *browsern* till flödet, Redis-posten är *uppslagsnyckeln* för PKCE-verifier + provider + `next`. Skriv ut det, annars "förenklar" någon bort den ena.
5. **GitHub:** endast `/user/emails` med `primary && verified` duger för e-postlänkning. `/user`.`email` är profilens publika fält och kan vara overifierat.

### Trade-offs accepterade
Vi skriver och testar tre adaptrar själva (ca 3 × 150 rader + kontraktstest) i stället för att konfigurera tre paket. Acceptabelt: alternativkostnaden är en ny CSRF-analys av hela auth-topologin.

---

# D1 — lagringsplats

### Beslut
**Som skrivet: utmaning, grant och OAuth-state i Redis (TTL ≤15 min) bakom `ILoginChallengeStore`; samtyckesstämpel på `job_seekers` och providerlänkar i `AspNetUserLogins`.** Tre bind tillkommer.

### Motivering mot principer
- **KISS/YAGNI.** Artefakternas hela semantik *är* utgång. En Postgres-tabell kräver en reaper-jobbform (ännu en `StrandedMatchReaperJob`), ett `expires_at`-index och en retentionsregel — persistens uppfunnen för data vars mening är att försvinna.
- **Invarianten matchar primitiven.** `INCR`-före-jämförelse, `GETDEL` vid succé, `SET NX` vid claim är atomiska *i kraft av* Redis. Postgres-motsvarigheten är `UPDATE … RETURNING` plus villkorlig delete i en transaktion — ett handbyggt samtidighetsprotokoll där det finns en primitiv. Martin 2017 kap. 22: verktyget är en detalj, men välj den detalj vars primitiv bär invarianten.
- **CCP/CRP (Martin 2017, kap. 13).** Redis är redan sanningskällan för sessioner och alltså redan tillgänglighetsberoende för varje autentiserad request. Postgres för utmaningen skulle göra inloggning beroende av **båda** lagren — en breddad felyta, inte en smalare. Det uniforma 503:et finns redan mätt (`Program.cs:304-314`) och är exakt det svar utmaningsvägen vill ha.
- **GDPR-inversionen är viktig att skriva ut.** Redis är här det **dataminimerande** valet (Art. 5(1)(e)): DataProtector-krypterad adress i ≤15 min som raderar sig själv, mot samma PII durabelt i Postgres med DEK-kuvert, retentionsplikt och en reaper. Det som ska in i behandlingsregistret är en ny *nyckelklass*, inte ett nytt risktagande.
- **Samtycke och providerlänkar är per definition Postgres.** `terms_accepted_at`/versionerna är ett ansvarsutkrävande-underlag (Art. 7(1)); `AspNetUserLogins` är Identitys egen tabell i `AppIdentityDbContext` (ADR 0013) med unikhetsvillkor och många-till-en, vilket de enkolumnsfälten aldrig kunde uttrycka.

### Avvisade alternativ
**Postgres-tabell för utmaning/grant:** breddar felytan (login kräver då två lager uppe), kräver reaper + index + retention för data med 15 minuters livslängd, byter tre atomära primitiver mot ett handbyggt transaktionsprotokoll, och flyttar PII från flyktig till durabel lagring. Avvisas på samtliga fyra.

### Bind
1. **Cookiens e-postadress är en ren visningsekó och får aldrig vara indata.** D2 lägger både `challengeId` och den inskickade adressen i `__Host-jobbliggaren_login`; D1 säger korrekt att `complete` måste använda den **bevisade** adressen ur posten. Pinna det: ett test som skickar en cookie vars adress skiljer sig från postens och hävdar att kontot skapas på **postens** adress. Utan pinnen är cookien ett andra hem för samma faktum.
2. **Egen kanalinstans för login-utskicket.** Mätt: `PasswordResetDispatchChannel` använder `BoundedChannelFullMode.DropWrite`, där `TryWrite` returnerar **true** vid full kö; droppen syns bara i `itemDropped`. Nu när mail är ett *hårt* beroende av inloggning (D10) får en forgot-password-flod inte tyst kasta inloggningar. Generalisera porten (DRY på orakel-stängande form), men **registrera två instanser** med var sin kapacitet och var sin dropplogg. Och: ingen endpoint får grena på `TryEnqueue`:s returvärde — det betyder inte "köad".
3. **Keyringen mäts före 1a går live.** `CreateProtector(` har noll anropsplatser i `src/` i dag; `AddDataProtection` persisterar bara när `DataProtection:KeyPath` är satt (`DependencyInjection.cs:1609-1611`). Förlorad keyring gör varje levande `emailEncrypted` oläsbar — felet degraderar till det uniforma "utgången"-svaret, vilket är rätt beteende, men 1a:s DoD ska innehålla en mätning av att `DataProtection__KeyPath` är satt på lådan.

### Trade-offs accepterade
Redis nere = ingen kan logga in, uniformt 503. Det är redan sant för varje autentiserad request, så D1 ökar inte beroendet — det gör det bara synligt på inloggningssidan.

---

# D5 — byt-e-post-kredentialen

### Beslut
**Alternativ (ii) — re-auth-kod till NUVARANDE adressen (`purpose=reauth`) OCH en kod till den nya — plus (i):s notis till den gamla adressen.** Två koder, två inkorgar bevisade, en notis som detektionskanal. Alternativ (i) och (iii) avvisas.

### Motivering mot principer
- **Hotmodellen är den D4 själv skapar.** Efter D4 är sessionscookien den *enda* kredentialen och den lever 180 dagar som default. Under (i) räcker en stulen session för att peka om återställningsvektorn till angriparens egen inkorg — angriparen tar emot koden själv. Det är permanent kontoövertagande, och offrets enda försvar är att hinna läsa en notis. Under (ii) måste angriparen dessutom läsa den **nuvarande** inkorgen; har han den äger han redan kontot. (ii) köper alltså exakt det (i) ger bort: skydd mot *sessions*-kompromiss.
- **(i) skulle tyst upphäva en skriven invariant.** `IReauthenticatingRequest`:s egen dokumentation säger ordagrant att en kapad 180-dagarssession *inte* ska kunna köra en känslig operation. Väljer vi (i) körs `ReauthenticationBehavior` fortfarande, heter fortfarande re-auth, och re-autentiserar ingenting. Att behålla en klass vars namn ljuger är sämre än att ta bort den (Martin 2008, "Meaningful Names"; LSP-kontraktet på pipelinen).
- **DRY som kunskapsstycke (Hunt/Thomas 1999).** "Bevisa att du äger en inkorg" ska ha **ett** mekanism-hem efter denna epik. (iii) behåller DataProtector-token-vägen parallellt med kodlagret — två hem, två attackbudgetar, två utgångsregler, och #706 (token i URL:en) stängs inte utan flyttas.
- **"Så få klick som möjligt" gäller inte här, och det är en kategoriförväxling att tillämpa det.** Direktivet är uttalat om inloggnings- och registreringstratten ("Med minsta möjliga uppgifter", punkt 1 och 6). Att byta återställningsvektor är en sällan utförd, hög-konsekvent handling. Minimum för två inkorgar är två utskick — det är aritmetik, inte design.

### Avvisade alternativ
**(i) en kod till NYA + notis till gamla:** enda skyddet mot sessionsstöld blir att offret läser notisen i tid. Det är detektion sålt som prevention. Avvisas.

**(iii) re-auth-kod + bekräftelselänk (dagens DataProtector-token):** korrekt säkerhetsmässigt men behåller två inkorgsbevis-mekanismer och lämnar #706 öppet. Avvisas på DRY, inte på säkerhet.

### Bind
- **Ordning:** re-auth mot NUVARANDE adressen körs **först** och avvisar innan något mintas — samma skäl som dagens handler har för att lägga `CanDeliver` före cooldown-bränningen (`ChangeEmailCommandHandler`, #1087-blocket).
- **`IReauthenticatingRequest.Password` byter namn** till en purpose-scopad grant (t.ex. `string? ReauthGrant`), och `ReauthenticationTripwireTests` behålls oförändrad i sin tvingande roll. `DeleteAccountCommand` följer med — den bär redan markören.
- **Grant-bindning i två led:** reauth-granten binds till `(userId, purpose)`; den nya adressens kodpost binds till `(userId, newEmail)`. `complete` hävdar båda, annars är granten replaybar mot en *annan* ny adress än den som visades.
- **Notisen till gamla adressen behålls** — den kostar ingenting och är den enda kanal som når en användare vars session redan är stulen.

### Trade-offs accepterade
Två mail och två kodinmatningar för ett e-postbyte. Accepterat: åtgärden är sällsynt och dess felutfall är permanent.

---

# Delningen

### Beslut
Tabellen är i huvudsak rätt skuren. **Ordningen binds med fyra namngivna ändringar:**

**0.5 före 1a — BINDS som skrivet.** Den avgörande mätningen: bootstrappen kan byggas **i dag**, mot `IUserAccountService.CreateUserAsync`, `JobSeeker.Register` och `ISessionStore.CreateAsync`, som alla finns vid HEAD. Den har alltså noll beroende på 1a. Skälet att den ska ligga före är SRP på PR-nivå (Martin 2017, kap. 7): en ren refaktorering med 187 anropsplatser måste landa mot grön main utan semantisk ändring i flykten. Landar den efter 1a rör *samma* 187 filer en PR som också ändrar beteende, och ingen granskare kan då skilja "harnesset flyttade" från "auth ändrades". 0.5 har dessutom noll hotspot och ingen migration — den mergar snabbt och blockerar inget.

**Ändring 1 — 1b flyttas FÖRE 1a.** 1b (samtyckessäte + migration) är oberoende av 1a: nullbara kolumner plus en `JobSeeker.Register(TermsAcceptance)`-överlagring rör inget 1a behöver. Att lägga den först frigör `ef-migration`-hotspoten tidigast möjligt i stället för att hålla den medan den L-stora 1a löper, och 1b stänger #1484 på egen hand. Migrationsordningen är oförändrad (1b var redan först). Sekvensen blir **0 → 0.5 → 1b → 1a → 1c → 2 → …**

**Ändring 2 — 6d avblockeras och flyttas fram till 1b:s migrationsfönster.** Mätt: `AuthProvider`/`ProviderUserId` läses **ingenstans** — endast deklaration, EF-konfiguration och migrationer. De är alltså död kod i dag, och det enda som gav dem en framtid var ADR 0017 punkt 7, vars syfte ADR 0142 (del 0) uttryckligen ersätter med `AspNetUserLogins`. Att parkera en triviell städning bakom tre nyckelblockerade delar betyder att den står `blocked` tills Klas skaffar API-nycklar — en backlog-rad som mäter ingenting. Trade-off namngiven: om 6a mot förmodan behöver en providerkolumn är återinförandet en nullbar kolumn och en migration.

**Ändring 3 — 4a/4b behålls som två PR:er, men av rätt skäl.** "Nullable-first, sedan drop" är mekanismen, inte skälet — och som skäl är den vana. Det bärande skälet är att **4b är oåterkallelig och 4a inte är det**. 4a ändrar semantik på bred yta (Resume-domänen, `AutoPromoteGate`, B3:s kanoniska arm, `PersonnummerInAccountName`-armen); 4b släpper kolumnen och dess data. Läggs de ihop kan en revert av det semantiska felet inte återställa namnen. **Bind:** 4b:s PR öppnas först när 4a är mergad **och** mätt live på `dev.jobbliggaren.se`.

**Ändring 4 — del 5 delas i 5a och 5b, av exakt samma skäl.** Som tabellen står buntar 5 (L, BE+FE, `di`-hotspot) tre ändringsskäl, varav ett är oåterkalleligt: (a) riv lösenordsytorna — endpoints, UI, `RequireEmailConfirmation`, runbooks, BUILD.md-sanning, #734 ompekad; (b) **nolla `password_hash`**; (c) dokumentsynk. (b) är det enda steget som gör en återgång till lösenordsinloggning omöjlig. Att lämna den i samma PR som rivningen är samma defekt jag just avvisade på 4a/4b, och min egen dom måste vara konsekvent. **5a** = rivningen och sanningssynken; **5b** = nollningen, som öppnar först när 5a är mergad och mätt live. Migrationsordningen blir **1b → 4a → 4b → 5b → 6d***(6d flyttad enligt ändring 2, dvs. 1b → 6d → 4a → 4b → 5b)*.

**En formuleringsrättelse, inte en ordningsändring:** 1a beskrivs som "persistent default". Backenden utfärdar `Persistent` i 1a, men `setSessionCookie(id, true)` och cookiepolicy-copyn ligger i del 2. Mellan dem finns ett fönster där backend säger 180 d och cookien saknar `Max-Age`. Ofarligt (backend är SSOT för utgång), men 1a:s issue ska inte påstå att persistent-by-default är levererad — det är det först i 2, och copyn måste ligga i **samma** PR som flippen.

### Avvisade alternativ
**Slå ihop 4a+4b (och 5a+5b) till en PR var:** enda vinsten är en PR-runda mindre; priset är att en destruktiv, oåterkallelig migration reser tillsammans med den semantiska ändring den skulle kunna behöva backa. Avvisas.

**Låta 6d ligga kvar sist "för säkerhets skull":** att hålla mätt död kod vid liv bakom en nyckelblockering är uppskjutning utklädd till försiktighet (Regel 3 — scope-storlek och bekvämlighet är inte längre grunder). Avvisas.

### Trade-offs accepterade
17 PR:er i stället för 15. Antalet PR:er är ingen designaxel; reverterbarhet på de två oåterkalleliga stegen är det.

---

### Eskalering till Klas

**Skriv detta ordagrant till Klas — sessionen bär det vidare, jag kan inte fråga honom:**

> **D10 gör e-post till ett hårt beroende av att kunna logga in — och efter del 5 finns ingen väg tillbaka.**
>
> I dag bryter ett mailavbrott (Scaleway nere, utgången API-nyckel, en avvisad avsändardomän) bara registrering och lösenordsåterställning — inloggning fungerar ändå, eftersom lösenordet finns. Efter den här epiken är kodmailet den **enda** inloggningsvägen: OAuth är blockerat på nycklar du inte har ännu, och del 5 nollar `password_hash`. Då gäller: **ingen kan logga in alls, inklusive du själv, och det finns ingen reservväg.** Adminseedern löser upp konton via e-post och hjälper inte här.
>
> Tre frågor, och jag behöver ditt svar innan ADR 0142 skrivs:
>
> 1. **Accepterar du att ett mailavbrott = totalt inloggningsstopp?** (Detta är ett tillgänglighetsbeslut med produktkonsekvens, inte ett tekniskt val — därför frågar jag.)
> 2. **Vill du ha en break-glass?** De realistiska formerna är (a) en Development/ops-gated engångskod som skrivs ut i loggen på lådan, (b) en andra mailprovider som fallback, eller (c) att lösenordet behålls för ditt eget adminkonto och bara för det.
> 3. **Ska del 5 (nollningen av `password_hash`) över huvud taget köras före lansering?** Alternativet är att låta lösenordsvägen ligga kvar inaktiv men intakt tills OAuth är live — det kostar att BUILD.md beskriver två auth-vägar ett tag till, men det gör steget reverterbart.
>
> Jag rekommenderar inget här: valet beror på hur mycket driftavbrott du tål på `jobbliggaren.se` under introduktionen av de första testanvändarna, och det är din bedömning. Frågan rör också #734 (go-live-grinden), som är din.

Inget annat i D2–D4, D6, D7, D9 är ett fasstrategiskt val — D4 och D7 är redan avgjorda av dina svar 2026-09-16/17 och återöppnas inte.

---

### Referenser
- Robert C. Martin, *Clean Architecture* (2017) — kap. 7 (SRP), kap. 11 (DIP), kap. 13 (REP/CCP/CRP), kap. 22 (frameworks as details)
- Robert C. Martin, *Clean Code* (2008) — "Meaningful Names" (en klass som heter re-auth måste re-autentisera)
- Hunt/Thomas, *The Pragmatic Programmer* (1999), kap. 7 — DRY som ett hem per kunskapsstycke
- Fowler, *Refactoring* 2nd ed (2018) — Parallel Change / expand-contract (4a→4b, 5a→5b)
- Repo: `docs/decisions/0017-frontend-auth-pattern.md` (Amendment 2026-07-25), `0018-cookie-and-csrf-strategy.md` ("Backend trust model"), `0013`, `0023`, `0083`, `0103`
- Spec: `CLAUDE.md` §2 (providerpaket), §6.5 (ef-migration single-owner), §9.2 (nya beroenden), §11; `AGENTS.md` §5 (`Comments:` — fel motiv är en defekt)
- MDN / CSP Level 3 `form-action`: hävdas tvärs redirects i Chromium och Firefox — grunden för bind 1 i D8

**Inga filer redigerade. Rapporten är ett beslut, inte en mätning av allt — det jag lutar mig på står i mättabellen överst, med fil och rad.**
