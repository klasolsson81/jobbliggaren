# Auth epic #1732 — part 0 decision round (dotnet-architect, 2026-09-17)

> Transcribed verbatim by the driving session from the agent's returned report. Brief the agent
> answered: the session's scratch files `1733-brief-context.md` + `1732-epic.md`, and the CTO report
> `docs/reviews/2026-09-17-auth-epic-cto.md` (whose verdicts it forms ports for, never re-grades).
> Worktree `C:/tmp/jbl-1733` at HEAD `081e4c67`. The session re-ran the load-bearing measurements
> before transcribing: `RedisCooldownGate.cs:26-27` (read-then-write, "no atomic SETNX"),
> `AccessFailedAsync` at `UserAccountService.cs:133` and `TryPreparePasswordResetAsync` at `:346`
> (**the file lives at `src/Jobbliggaren.Infrastructure/Auth/UserAccountService.cs`, not under
> `Identity/`** — the report's line numbers are correct, the folder is not named in it),
> `Worker/Program.cs:50` `ValidateOnBuild = false`, `InstanceName = "jobbliggaren:"` at
> `DependencyInjection.cs:1711`, `ReauthenticationTripwireTests.cs` mentions `Password` only in prose
> (lines 26/31/56/58/76) and in no assertion, exactly one `JobSeeker.Register(` call site in `src/`
> (`RegisterCommandHandler.cs:113`), `HasConversion<string>()` + the named index in
> `ApplicationUserConfiguration.cs:14/36`, `ICooldownGate.cs:16` says "lower-invariant" while
> `RedisCooldownGate.cs:67` does `ToUpperInvariant()`, `content-legal.json` `updated` at lines 8 and
> 231 — all confirmed. No escalation to Klas in this report.

## Arkitektur-analys

### Sammanfattning
Designgranskning av D1–D10 mot koden i `C:/tmp/jbl-1733` @ `081e4c67`. Formen håller, men elva portar/signaturer behöver bindas innan ADR 0142 skrivs — sex av dem kritiska, varav tre är hål som inte syns förrän de skeppas (en icke-atomär claim, ett återarmerbart lösenord, en Worker-felregistrering utan detektor).

### Rekommendation

---

**[Kritiskt]** `ILoginChallengeStore` — en `Consume`, aldrig ett läs/skriv-par
**Vad:** `INCR`-före-jämförelse, `GETDEL` och konstant-tids-compare är Redis-*medlemmar* (CLAUDE.md §2 axel 3). Application får se invarianten, inte verben.
**Varför:** två metoder (`GetAsync` + `BurnAsync`) låter vilken handler som helst bygga den icke-atomära sekvensen porten finns för att förbjuda. Precedens: `ICooldownGate` (`ICooldownGate.cs:20-28`) exponerar *ett* test-and-set och skriver ut att policyn bor hos anroparen.
**Föreslagen åtgärd:**

    public readonly record struct ChallengeId(string Value);          // ≥128 bit, Base64Url
    public enum ChallengeOutcome { Verified, Wrong, Burned, Missing } // expired == Missing
    public sealed record ChallengeVerdict(ChallengeOutcome Outcome, LoginChallenge? Record);

    Task PutAsync(LoginChallenge c, TimeSpan ttl, CancellationToken ct);   // bränner ev. levande post för samma adress
    Task<ChallengeVerdict> ConsumeAsync(ChallengeId id, string presentedCode, CancellationToken ct);

`ConsumeAsync`-kontraktet (i XML-doc, inte i en kommentar hos anroparen): räknaren inkrementeras **före** jämförelsen, posten raderas atomärt vid träff, `Record` är icke-null **endast** vid `Verified`, och en dummy-jämförelse betalas även när ingen post finns. Hashning + jämförelse bor i adaptern — samma placering som `RedisCooldownGate` valde för sin hashning (`RedisCooldownGate.cs:65-70`). Att kollapsa `Wrong/Burned/Missing` till ett svar är **handlerns** policy, precis som `ICooldownGate` skriver om sin `false`.

---

**[Kritiskt]** `SET NX`-claimen kan inte återanvända `ICooldownGate` — den är inte atomär
**Vad:** gaten ser ut som rätt primitiv (`TryBeginAsync(scope, subject, window)`), men implementationen är read-then-write och säger det själv: *"IDistributedCache exposes no atomic SETNX, but the tiny race … at worst allows one extra send"* (`RedisCooldownGate.cs:26-31`).
**Varför:** för en throttle är racet ett extra mejl; för `complete` är det två konton på samma adress. Att återanvända gaten vore att ärva en medvetet accepterad race in i en plats där den inte är accepterad.
**Föreslagen åtgärd:** egen atomär `Task<bool> TryClaimAsync(string subjectKey, TimeSpan ttl, CancellationToken ct)` på `ILoginChallengeStore` (adaptern kör `StringSet(..., When.NotExists)` via `IConnectionMultiplexer`, som redan är registrerad — `DependencyInjection.cs:1720`). Ändra **inte** `RedisCooldownGate` — den är skeppad och dess semantik är motiverad. Skriv också ut i ADR 0142 att claimen inte är unikhetens hem: det är `RequireUniqueEmail` (`DependencyInjection.cs:1656`) plus indexet, så D3:s "registrerad under tiden"-arm måste hantera dubblettfelet även när claimen vanns.

---

**[Viktigt]** Grant-lagret — **en** port, `purpose` som enum, bindningen hävdad inuti `Redeem`
**Vad:** `login-complete`, `reauth`, `change-email` delar exakt en mekanism: opak engångsbärare, kort TTL, bunden till ett subjekt, vägrad tvärs purpose. Det är en port, inte tre.
**Varför:** CTO:s D5-bind 3 (granten replaybar mot en annan ny adress) stängs bara om bindningen hävdas där den inte kan glömmas. En `purpose`-sträng vore dessutom magic strings (§5) — `CooldownScopes.cs` visar husets form för det, men här finns en sluten mängd, så enum är rätt.
**Föreslagen åtgärd:**

    public enum GrantPurpose { LoginComplete, Reauthentication, ChangeEmail }

    Task<GrantId> IssueAsync(GrantPurpose purpose, string subjectKey, string? payloadProtected,
                             TimeSpan ttl, CancellationToken ct);
    Task<AuthGrant?> RedeemAsync(GrantId id, GrantPurpose expected, string expectedSubjectKey,
                                 CancellationToken ct);   // GETDEL + båda hävdandena INUTI

`RedeemAsync` returnerar `null` för allt — okänd, utgången, fel purpose, fel subjekt — så ingen handler kan jämföra själv och ingen kan glömma. `subjectKey` är `userId` för `Reauthentication`, den **bevisade** adressen för `LoginComplete`, och `(userId, newEmail)` för `ChangeEmail` (CTO:s två led). Separat port från `ILoginChallengeStore`: utmaningen är en kodverifiering med attempt-budget, granten en bärarkapacitet utan. En Redis-adapter får bära båda.

---

**[Kritiskt]** Auth-mail-dispatchern — **två portar**, och den nya returnerar `void`
**Vad:** CTO binder två kanalinstanser med egen kapacitet och egen dropplogg. Varken `IAuthMailDispatcher<T>` eller en diskriminerad union uttrycker det: en union är per definition **en** kö och därmed en kapacitet.
**Varför:** generikan ger en `IOptions<...<T>>`-bindning och **en** `LoggerMessage` med ett typnamn som parameter i stället för två event-id:n — svagare signal än den som redan finns (`PasswordResetDispatchChannel.cs:102-105`). Två portar ger två kapaciteter, två sektioner, två event-id:n och ett typfel om en inloggning någonsin hamnar i reset-kön.
**Föreslagen åtgärd:** lämna `IPasswordResetDispatcher` orörd (skeppad, och dess doc är sann) och lägg till:

    public sealed record LoginChallengeDispatch(
        ChallengeId ChallengeId, string Email, string? IpAddress, string? UserAgent);

    public interface ILoginChallengeDispatcher { void Enqueue(LoginChallengeDispatch dispatch); }

DRY:n bor i Infrastructure som en `internal abstract BoundedDispatchChannel<T>` — det är där dubbleringen faktiskt finns. **`void`, inte `bool`:** CTO:s bind *"ingen endpoint får grena på `TryEnqueue`:s returvärde"* blir då sann av konstruktion i stället för av ett test. Det gamla `bool`-värdet har ett skrivet skäl (shutdown, `PasswordResetDispatchChannel.cs:82-87`) som den nya porten inte delar — dess anropare svarar uniformt 202 oavsett.

---

**[Viktigt]** `IExternalIdentityProvider` — "provider asserts verified" bärs av **typen**, inte av en bool
**Vad:** kontraktet får aldrig returnera `(string Email, bool EmailVerified)`. En anropare läser strängen och glömmer boolen.
**Varför:** D8 säger "länka till befintligt konto på e-post ENDAST när providern hävdar den verifierad", och CTO-bind 5 säger att GitHubs `/user.email` inte duger. Båda blir oöverträdbara om det inte finns något fält som kan bära en overifierad adress.
**Föreslagen åtgärd:**

    public readonly record struct VerifiedEmail(string Value);
    public sealed record ExternalIdentity(string ProviderKey, string Subject, VerifiedEmail? Email);

    string ProviderKey { get; }                                  // == AspNetUserLogins.LoginProvider
    AuthorizationRequest CreateAuthorizationRequest(string? next);
    Task<Result<ExternalIdentity>> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct);

`redirect_uri` är **ingen parameter** — adaptern bygger den själv ur `EmailOptions.BaseUrl` (`Infrastructure/Email/EmailOptions.cs:29`), som ligger i Infrastructure och därför aldrig når Application. Det verkställer CTO-bind 3 (ett hem, ingen ny `OAuth:RedirectBaseUrl`) utan en ny port. `GET /auth/oauth/providers` = `IEnumerable<IExternalIdentityProvider>.Select(p => p.ProviderKey)`, så en provider utan nycklar helt enkelt inte registreras — fail-closed, ingen flagga. Nice-to-have: läs `BaseUrl` genom en Infrastructure-intern `IPublicBaseUrl` så namnet `Email` slutar ljuga när två konsumenter läser det.

---

**[Viktigt]** `IReauthenticatingRequest.Password` → grant: vad tripwiren faktiskt pinnar
**Vad:** jag läste testet. `ReauthenticationTripwireTests.cs:36-88` nämner **aldrig** medlemmen `Password` i någon assertion — den pinnar tre saker: (1) varje `IAuthenticatedRequest` vars namn matchar `SensitiveOp`-regexen (rad 30-34) måste implementera markören, (2) varje implementation måste ha en FluentValidation-validator, (3) `ReauthenticationBehavior<,>` bor i `Application.Common.Behaviors`.
**Varför:** CTO:s bind *"testet behålls oförändrat"* håller alltså mekaniskt — namnbytet till `string? ReauthGrant` bryter ingen assertion. Men rad 58-59 och 76-77 är prosa som säger "Password" och "NotEmpty(Password)", och en faktiskt felaktig kommentar är en defekt (AGENTS.md §5 `Comments:`). Ordet byts; det testet *pinnar* ändras inte.
**Föreslagen åtgärd:** byt medlemsnamn i `IReauthenticatingRequest.cs:21` och rätta två prosarader i tripwiren samt XML-docens *"must re-prove their password"* (`IReauthenticatingRequest.cs:3-18`). `ReauthenticationService` (ligger i **Application**, `Auth/ReauthenticationService.cs:12-17`) byter kropp: `VerifyCurrentUserPasswordAsync` → `VerifyCurrentUserGrantAsync`, som redeemar mot `(userId, GrantPurpose.Reauthentication)`. Behåll soft-delete-grinden (rad 79-99) ordagrant — den är oberoende av kredentialen och bär #1349:s rättning. `ValidateCredentialsAsync`-anropet (rad 46) faller bort, och med det `EmailNotConfirmed`-normaliseringen (rad 54-56) — den meningen ska **raderas**, inte skrivas om.

---

**[Kritiskt]** `JobSeeker.Register` — ersättning, inte överlagring; versionerna bor i Domain
**Vad:** ersätt `Register(Guid, string?, IDateTimeProvider)` (`JobSeeker.cs:127-148`) med `Register(Guid userId, string? displayName, TermsAcceptance acceptance, IDateTimeProvider clock)`. `displayName` stannar tills 4a/4b.
**Varför:** en överlagring lämnar den samtyckeslösa vägen anropbar, och invarianten "en ny seeker bär ett samtyckesbevis" blir då en konvention i stället för en konstruktion (§2.2). Kostnaden är mätt: **ett** anropställe i `src/` (`RegisterCommandHandler.cs:113`) plus 0.5:s bootstrap-helper — observera att 0.5 därmed inte är helt orörd av 1b, men det är en fil, inte 187.
**Föreslagen åtgärd:** VO i Domain, `JobSeekers/TermsAcceptance.cs`, `readonly record struct` med factory som returnerar `Result<TermsAcceptance>` (AcceptedAt ≠ default, båda versionerna icke-tomma och ur den kända mängden). Egenskapen är `TermsAcceptance?` — nullbar för de två befintliga raderna, obligatorisk i factoryn (D6). EF: `builder.OwnsOne(js => js.TermsAcceptance).IsRequired(false)`, paritet med `Preferences` (`JobSeekerConfiguration.cs:24`) — tre nullbara kolumner, **inte** `ToJson`: ett Art. 7(1)-underlag ska vara frågebart.
**Versionernas hem:** konstanter på `TermsAcceptance` i **Domain** (Domain läser inga filer, och aggregatet är det enda som kan vägra en gammal version). Värdet är ISO-datumet som redan står i copyn. Pariteten pinnas med en mätning, och precedenten finns: `ContactAddressMatchesPublishedContactTests.cs:111-127` går uppåt till repo-roten och läser `web/jobbliggaren-web/messages/{sv,en}/content-legal.json`. Kopiera den formen och hävda att `terms.updated` (`content-legal.json:231`) och `privacy.updated` (rad 8) **slutar med** Domain-konstanten — prosa, så parse, inte likhet. Pinna **båda** språken, som precedenten gör.

---

**[Kritiskt]** `CreatePasswordlessUserAsync` — och det återarmerbara lösenordet
**Vad:** signaturen är enkel; hålet ligger i grannvägarna. Mätt mot `UserAccountService`:
- `ValidateCredentialsAsync` (rad 100-155): Identitys `VerifyPasswordAsync` svarar `Failed` på null-hash, så det blir `InvalidCredentials` — **men rad 133 kör `AccessFailedAsync`**, så vem som helst som känner adressen kan låsa ett lösenordslöst konto i 15 min (`DependencyInjection.cs:1691-1692`). Ofarligt bara så länge kodvägen aldrig konsulterar `IsLockedOutAsync`. **Bind det i ADR 0142:** utmaningsvägens anti-automation är 3-försöksbränningen + mint-budgeten, aldrig Identity-lockouten — annars är varje känd adress DoS-bar via den endpoint 5a ändå river.
- `ChangePasswordAsync` (rad 53-74): `PasswordMismatch` → 400. Ofarligt; 3b tar kortet.
- **`TryPreparePasswordResetAsync` (rad 346-372) + `ResetPasswordAsync` ger ett lösenordslöst konto ett lösenord** (och sätter `EmailConfirmed`). Från 1c till 5a är "passwordless" alltså inte en invariant utan ett starttillstånd, och 5b:s nollning vore reverserbar av en produktionsväg.

**Föreslagen åtgärd:** signatur `Task<Result<Guid>> CreatePasswordlessUserAsync(string email, CancellationToken ct)` — samma `Result<Guid>` och samma dubblettkollaps som `CreateUserAsync` (rad 42-48), eftersom 1c:s "registrerad under tiden"-arm behöver känna igen dubbletten. Implementation: `userManager.CreateAsync(user)` (parameterlösa överlagringen) med `EmailConfirmed = true` satt före anropet; notera i porten att inga password-validators körs, så `PwnedPasswordValidator` (`DependencyInjection.cs:1704`) medvetet inte är engagerad. Och stäng hålet **i 1c, inte i 5a**, med en rad på 358:

    if (user is not { Email: { } accountEmail, PasswordHash: not null })
        return null;

Ingen ny orakel-yta: portens egen doc säger redan att varje icke-berättigat fall är oskiljbart för anroparen.

---

**[Kritiskt]** Api-only-registreringen — och att ingen befintlig vakt fångar en felregistrering
**Vad:** konsumenten registreras inuti `AddIdentityAndSessions` (`DependencyInjection.cs:1621`), intill #1171-blocket på rad 1771-1783. Worker anropar `AddCoreIdentityForWorker` (`Worker/Program.cs:61`) och aldrig den metoden.
**Varför (två strukturella skäl, båda mätta):** konsumenten behöver `IDataProtectionProvider` för `emailEncrypted`, och bara `AddApiDataProtection` registrerar en (anropad på rad 1633; rad 1577 säger att Worker medvetet saknar den). Den behöver också Redis/`ISessionStore` (rad 1798-1800). En Worker-registrering kan alltså inte ens resolvas.
**Men det finns ingen detektor.** `WorkerLayerTests.cs:24-31` skannar **Worker-assemblyn** efter ASP.NET-beroenden; konsumenten bor i Infrastructure, så en handskriven `AddHostedService<LoginChallengeDispatchService>()` i `Worker/Program.cs` passerar varje arkitekturtest. Worker kör dessutom `ValidateOnBuild = false` (`Worker/Program.cs:50`), och om den nya konsumenten kopierar `PasswordResetDispatchService`:s medvetna lat-resolve inuti `try` (rad 73-81) blir en felregistrering **en warning per kö-item, för alltid** i stället för en boot-krasch.
**Föreslagen åtgärd:** lägg till exakt testparet från `AuthOptionsValidatorTests.cs:220-241` för den nya porten — en positiv kontroll (`AddIdentityAndSessions` registrerar `ILoginChallengeDispatcher`) och en negativ (`AddCoreIdentityForWorker` gör det inte). Den positiva är obligatorisk: filens egen kommentar på rad 223-225 skriver ut varför två frånvaron utan kontroll inte mäter något. Namnge i ADR 0142 att restposten — en handskriven rad i `Worker/Program.cs` — inte fångas av något test, bara av att någon läser den filen.

---

**[Viktigt]** Redis-nyckelklasserna — ingen kollision, men fel form, och budgeten behöver en egen primitiv
**Vad:** mätt namnrymd: `IDistributedCache` autoprefixar `jobbliggaren:` (`DependencyInjection.cs:1711`); `RedisSessionStore` speglar prefixet manuellt (`RedisSessionStore.cs:19-22`) och äger `session:{b64url(sha)}` (rad 480), `user:{id}:sessions|revoked|deleted` (487/499/506) och `{session}:rotating` (492); `RedisCooldownGate` äger `cd/{scope}/v1/{hex}` (rad 69). `challenge:`, `grant:`, `oauth:` kolliderar alltså inte.
**Varför ändå ändra:** tre nya toppnivånamn bredvid `session:` är tre nya rotsegment ingen äger. Cooldown-gatens form löste redan problemet — `{namn}/v{n}/{nyckel}` — och `CooldownScopes.cs:4-9` skriver ut varför: ett värde får aldrig ändras efter skepp, eftersom levande fönster då nollställs.
**Föreslagen åtgärd:** `auth/challenge/v1/{id}`, `auth/grant/v1/{id}`, `auth/oauth-state/v1/{state}`. `v1`-segmentet är det som ska kopieras: en postformsändring kostar då ett nytt segment i stället för en avkodningskrasch på levande poster.
**Mint-budgeten är inte en cooldown.** `ICooldownGate.TryBeginAsync` är ett-per-fönster; 3/10 min och 10/24 h är räknare. Lägg en egen liten port `IRateBudget.TryConsumeAsync(scope, subject, limit, window)` (Redis `INCR` + `EXPIRE`, atomärt), nyckel `budget/{scope}/v1/{hex}`. **Kritiskt villkor:** den nya hashningen måste använda **exakt** `RedisCooldownGate.Key`:s normalisering — `Trim().Normalize().ToUpperInvariant()` (rad 67) — och skälet står i rad 43-64: U+017F och NFD ger annars 2^k oberoende fönster för samma konto. En ny hashningsplats som glömmer det återöppnar den bypassen tyst. Nice-to-have: `ICooldownGate.cs:16` påstår *"trim + lower-invariant"*, vilket implementationen motbevisar — en felaktig kommentar i den port ADR 0142 kommer citera som mönster.

---

**[Viktigt]** Migrationerna — vilken kontext, och vad 5b faktiskt är
**Vad:** två migrationsförsamlingar, båda i Infrastructure men skilda mappar och skilda historiktabeller: `Persistence/Migrations` (`AppDbContext`, publikt schema) och `Identity/Migrations` (`AppIdentityDbContext`, `HasDefaultSchema("identity")` + `MigrationsHistoryTable("__EFMigrationsHistory", "identity")`, `DependencyInjection.cs:1640`; snake_case på rad 1642).
**Föreslagen fördelning:**
- **1b** (`terms_accepted_at`, `terms_version`, `privacy_policy_version` nullbara på `job_seekers`) → **Persistence**.
- **4a** (`display_name` nullbar; i dag `.IsRequired()`, `JobSeekerConfiguration.cs:22`) och **4b** (drop) → **Persistence**.
- **6d** → **Identity**. Trivialt: `provider` lagras som **sträng** med `HasConversion<string>()` (`ApplicationUserConfiguration.cs:13-17`), så ingen Postgres-enumtyp behöver städas. Två `DropColumn` + `DropIndex` på det namngivna `ix_asp_net_users_provider_provider_user_id` (rad 36).
- **5b** → **Identity**, och det är en **data**migration, inte en schemaändring: `password_hash` är redan nullbar i Identitys egen modell. Alltså `migrationBuilder.Sql("UPDATE identity.asp_net_users SET password_hash = NULL")`.
**Två saker att binda i ADR 0142:** (1) 5b bör rotera `security_stamp` i samma sats, annars överlever varje utestående reset-token nollningen; (2) 5b:s `Down` kan inte återställa hashar — den ska vara en explicit `throw`/dokumenterad no-op, aldrig en tom metod som ser reverserbar ut. Överlämna exakt SQL-formen till `db-migration-writer` (§9.2) — det är hans bord, inte mitt.

### Eskalering till Klas
Nej. (CTO:ns break-glass-eskalering står kvar obesvarad och är hans, inte en ny.)

### Referenser
- `AGENTS.md` §2.1 (EF-beroenderegeln, axel 3 — providermedlemmar bakom port), §2.2 (invarianter i aggregatet), §2.3, §3.6, §5 (`Backend:`, `Comments:`)
- `CLAUDE.md` §6.5 (ef-migration single-owner), §9.2 (mandatoriska agenter), §11 (dev-boot-kontrakt: varje ny `ValidateOnStart`-nyckel till `appsettings.Local.json.example` + `local-dev-setup.md` i samma PR — gäller `Auth:LoginChallengeDispatch:Capacity` och budgetfönstren)
- ADR 0009 (`IAppDbContext`-porten), ADR 0013 (separat `AppIdentityDbContext`), ADR 0023 (Worker utan `ISessionStore`), ADR 0018 (backend sätter inga cookies)
- Mätta filer: `JobSeeker.cs:108-148` · `UserAccountService.cs:100-155, 346-372` · `IReauthenticatingRequest.cs:19-22` · `ReauthenticationService.cs:12-107` · `ReauthenticationTripwireTests.cs:30-88` · `RedisCooldownGate.cs:26-70` · `RedisSessionStore.cs:19-22, 480-506` · `PasswordResetDispatchChannel.cs:61-105` · `PasswordResetDispatchService.cs:67-113` · `DependencyInjection.cs:1590-1614, 1621-1826` · `Worker/Program.cs:49-61` · `ApplicationUserConfiguration.cs:13-36` · `JobSeekerConfiguration.cs:22-24` · `EmailOptions.cs:29` · `AuthOptionsValidatorTests.cs:204-241` · `WorkerLayerTests.cs:17-36` · `ContactAddressMatchesPublishedContactTests.cs:111-127` · `content-legal.json:8, 231`

**Inga filer redigerade.**

---

## Scoped re-check (report-only, 2026-09-17, against commit `9d591eb5` on PR #1748)

> Transcribed verbatim by the driving session. Scope: the agent's own eleven recommendations against ADR 0142 + the 0017/0018 amendments. No files edited by the agent.

### Sammanfattning
OK — alla elva rekommendationer bärs av ADR 0142 (kommit `9d591eb5`, 4 filer / +675). Tre bärs med namngiven avvikelse, alla tre motiverade och ingen av dem försvagande. Inga nya Kritiskt/Viktigt i deltat.

| # | Rekommendation | Status | Var i ADR:en |
|---|---|---|---|
| 1 | `ILoginChallengeStore.ConsumeAsync` — en `Consume`, aldrig läs/skriv-par | **Stängd** (avvikelse A) | D1, kodblocket + "`ConsumeAsync`'s contract, in its XML doc" |
| 2 | `TryClaimAsync` som egen atomär `SET NX`, inte `ICooldownGate` | **Stängd** | D1, "**`TryClaimAsync` is its own atomic `SET NX`**" (inkl. att claimen inte är unikhetens hem) |
| 3 | Grant-lagret: en port, `purpose` som enum, bindningen inuti `Redeem` | **Stängd** | D3, "**Grants are ONE port with `purpose` as an enum**" (TTL 10 min tillagt) |
| 4 | Två dispatcher-portar, den nya returnerar `void` | **Stängd** | D2, "**The dispatcher is a second port and a second channel, and it returns `void`**" + `internal abstract BoundedDispatchChannel<T>` |
| 5 | `VerifiedEmail`-typen; `redirect_uri` ur `EmailOptions.BaseUrl`, ej parameter | **Stängd** (avvikelse B) | D8, "**Contract:**" |
| 6 | `IReauthenticatingRequest.Password` → grant; tripwiren pinnar inte namnet | **Stängd** | D5, stycket "Order: re-auth … runs first" (soft-delete-grinden ordagrant, `EmailNotConfirmed`-meningen **raderas**) |
| 7 | `JobSeeker.Register` ersätts; `TermsAcceptance` + versionerna i Domain; paritetstest | **Stängd** | D6 i sin helhet (`OwnsOne(...).IsRequired(false)`, ej `ToJson`; paritetstest båda locales, "ends with") |
| 8 | `CreatePasswordlessUserAsync` + de två hålen i `UserAccountService` | **Stängd** | Signaturen i D10; hålen i D3 ("**1c closes two holes**" — lockout-bindningen, `AccessFailedAsync`-ordningen, `TryPreparePasswordResetAsync` → `null`) |
| 9 | Api-only-registrering + testparet, och att ingen vakt fångar felregistreringen | **Stängd** | D2, "**The consumer registers in the Api composition only**" (residualen namngiven) |
| 10 | Redis-nyckelformen, `IRateBudget`, en normaliseringsplats | **Stängd** (avvikelse C) | D1, "**Keys** follow the delivered cooldown form" + "**The mint budget is a counter, not a cooldown**" |
| 11 | Migrationerna per kontext; 5b `security_stamp` + `Down` | **Stängd** | "Implementation status" → "**Migration order (single-owner)**": 1b/4a/4b `Persistence`, 6d + 5b `Identity`, 5b roterar `security_stamp` i samma sats, `Down` en explicit `throw`, SQL:en till `db-migration-writer` |

**Namngivna avvikelser**
- **A (#1):** koden DataProtector-skyddas i stället för att hashas (security Major 2). **Ingen kollision.** Min mening band *placeringen* — "hashning + jämförelse bor i adaptern", dvs. Application ser verdicten, aldrig verben — inte algoritmen. ADR:en behåller meningen ordagrant ("Hashing and comparison live in the adapter"), hashar länktoken och skyddar koden; motivet (10⁶-preimage gör osaltad hash verkningslös mot samma läsare adressen krypteras mot) är korrekt och starkare än mitt. Invarianterna jag skyddade — INCR före jämförelse, atomär radering vid träff, `Record` icke-null endast vid `Verified`, dummy-jämförelse alltid — står kvar. Adaptern måste förstås `Unprotect`:a före konstanttidsjämförelsen (DataProtection är icke-deterministisk); det är implementationsform för 1a, inte ett ADR-hål.
- **B (#5):** min nice-to-have `IPublicBaseUrl` togs inte. Ej ett fynd — den var nice-to-have och ADR:en skriver ut "the one home".
- **C (#10):** starkare än rekommenderat. Jag bad varje ny hashningsplats kopiera `RedisCooldownGate.Key`:s normalisering; ADR:en *lyfter* den till en delad intern funktion som den skeppade gaten delegerar till. Det rör skeppad kod, men bevarar semantiken och tar bort divergensrisken helt — bättre än mitt förslag, och min "ändra inte `RedisCooldownGate`" gällde dess SETNX-semantik, inte normaliseraren. `ICooldownGate.cs:16`-kommentaren rättas i samma PR.

**Nya-i-deltat Kritiskt/Viktigt: inga.**
