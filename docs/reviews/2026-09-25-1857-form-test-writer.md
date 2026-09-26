# test-writer — 5b (#1857) form round, report-only, against `41a49394`

Transcribed verbatim from the agent's SubagentHandback payload (extracted from its transcript), 2026-09-25.
Brief: `docs/sessions/2026-09-25-1857-form-brief.md`.

## test-writer: formrond 5b (#1857, epik #1732, ADR 0142), Q15–Q17

**Läge:** endast rapport. Worktree `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\github-ci-audit-optimize-039bb7`, HEAD `41a49394` är verifierad. Jag har inte skrivit någon fil och inte kört något som muterar (inga tester, inget bygge, eftersom ett bygge skriver `bin/`). Allt jag säger om IL bygger därför på källkod plus Cecil-semantik, inte på en körning.

---

### Ommätt (det planen vilar på)

- **Fakta 3 stämmer.** Bara `DropAuthProviderColumnsMigrationTests.cs:200` kör `IMigrator` mot Identity-kontexten. Steg 1 (`:188`) går till head, så 5b:s `Down` skulle köras vid `:200`. De övriga fem migrationstesterna i Worker och `SchemaAheadSubstrateTests` kör `AppDbContext`.
- **Fakta 5 stämmer** (`IdentityInboxProofRecorder.cs:26-27`, unit-pins `:35` och `:43`). **Nytt:** i dag finns ingen unit-pin alls på success-grenen. Bara E2E-test 2 ser den.
- **Fakta 8 stämmer.** Tillägg till stale-listan: `LoginChallengeProofTests.cs:267` säger också "survive until 5b", och `PasswordlessSessionGrant.cs:45-46` säger "a session opened with the removed password". Efter Q12 tar Identity-skrivningen inte längre bort något lösenord.
- **Fakta 10 stämmer.** `ebe12103` rörde 3 filer och 0 tester. **Nytt:** orden `lösenord`/`password` och `kryptografisk hash`/`cryptographic hash` finns i `content-legal.json` bara på rad 33, i båda språken. Inget test läser den raden.
- **Aktören som skrev hashade rader är pensionerad.** Mätt i `41a49394^:src/Jobbliggaren.Infrastructure/Auth/UserAccountService.cs:24-37`: `CreateUserAsync` anropade `userManager.CreateAsync(new ApplicationUser { UserName, Email }, password)` och lämnade `EmailConfirmed` som false.
  - Den pensionerade `RegisterCommandHandler` (`41a49394^`, `:144ff`) skapade en session för ett obekräftat konto bara på instant-login-vägen, alltså när `RequireEmailConfirmation` var av.
  - Den pensionerade `LoginCommandHandler` vägrade `EmailNotConfirmed` bara när flaggan var på.
- **En pin på den nuvarande skrivaren finns:** `LoginChallengeCompleteTests.A_new_address_that_accepts_the_terms_gets_a_passwordless_account_and_a_persistent_session` (`:100-101`: `EmailConfirmed` true, `PasswordHash` null).
- **Inget läser lockout eller bekräftelse utom recordern.** `git grep` efter `LockoutEnd|IsLockedOut|AccessFailed|EmailConfirmed` i `src/` utanför Migrations ger bara recordern och `CreatePasswordlessUserAsync`. Att `email_confirmed` överlever migrationen är alltså lastbärande för recorderns `FirstProofRecorded`-gren och för inget annat.
- **Nytt: 10 testseeds skapar Identity-rader med lösenord**, via `CreateAsync(user, "<…>Pass123!")`:
  - 9 i Worker.IntegrationTests: HardDeleteAccountsJobIntegrationTests ×4, BackupRestoreDrillTests, FollowedCompanyDigestIntegrationTests, DigestDispatchJobIntegrationTests och CryptoErasureHardDeleteTests ×2.
  - 1 i `Application.UnitTests/IdentityBootstrap/IdempotentAdminRoleSeederAuditEvidenceTests.cs:135`.
  - Ingen av filerna läser `PasswordHash` (grep). Lösenordet är alltså incidentellt för deras assertions (se F-6).
- **`OwnContainerPerTestTests` gäller bara Api-sviten** (den läser `ThisAssembly`). I Worker.IntegrationTests bryter en ny klass med egen container ingen likhet, men varje `[Fact]` får en egen container. Därför ska allt ligga i **en** journey-metod.
- **Fakta 7 stämmer på källnivå**, samma träffar. Två av dem går inte att avgränsa med nuvarande instrument (se F-2):
  - den anonyma typen från `InitialIdentity.cs:44` ligger i global namespace;
  - `ConfigurationOptions::get_Password` ligger i modulens MemberRef-tabell, som inte säger vilken typ som refererar.

---

### Q15: journey-testet

**Klass:** `NullPasswordHashesMigrationTests` (namnet följer Q3).
**Projekt:** `tests/Jobbliggaren.Worker.IntegrationTests/Migrations/`.
**Fixtur:** egen `PostgreSqlBuilder("postgres:18")` plus `TestDatabaseProvisioner.ProvisionAndGetAppConnectionStringAsync(..., includeIdentitySchema: true)`, samma form som syskonen. **En enda `[Fact]`**, eftersom varje metod är en egen container.
**Namn:** `NullPasswordHashes_NullsEveryHash_RotatesOnlyThoseStamps_IsIdempotent_AndRefusesToReverse`.

**Komposition för seeds:** `new ServiceCollection().AddLogging().AddCoreIdentityForWorker(<in-memory config: ConnectionStrings:Postgres = app-strängen>)` plus `IDbExceptionInspector → DbExceptionInspector` (internal, men IVT till Worker.IntegrationTests finns i `Jobbliggaren.Infrastructure.csproj:222`). Det ger en riktig `UserManager` över den riktiga `UserStore`.

**Rå `INSERT` duger inte här.** `DropAuthProviderColumnsMigrationTests.InsertUserAsync` skriver NULL-stämplar, och "stämpeln roterad bara där en hash fanns" vilar på stämplar som `CreateAsync` sätter.

| Seed | Tillstånd | Aktör (AGENTS.md §5 `Tests:`) | Pin |
|---|---|---|---|
| **R1** | hash, `email_confirmed = false` | Pensionerad `POST /auth/register` → `CreateUserAsync` → `UserManager.CreateAsync(user, password)` (`41a49394^ UserAccountService.cs:24-37`). Testet gör exakt det ramverksanropet. Lösenordet genereras vid körning och uppfyller Worker-kompositionens `PasswordOptions`; hashen kommer från `PasswordHasher` inne i `CreateAsync`, aldrig från en literal. | Den nuvarande skrivaren producerar inte formen: `LoginChallengeCompleteTests.A_new_address_…` (född bekräftad, ingen hash). |
| **R2** | hash, `email_confirmed = true` | Som R1, sedan pensionerad `POST /auth/verify-email` → `ConfirmEmailAsync`. Den skrivningen = `SetEmailConfirmedAsync` + `UpdateUserAsync`, så testet gör `user.EmailConfirmed = true; userManager.UpdateAsync(user)`. Token-kontrollen hör till routens behörighet, inte till radens form. | Samma som R1. |
| **R3** | ingen hash, bekräftad | Levande: `UserAccountService.CreatePasswordlessUserAsync`, anropad direkt. | Behövs inte, producerad av `src/`. |
| **Stale R2** | en `UserManager`-instans som laddade R2 före 5b och sparar efter | Levande: `UserStore.UpdateAsync` skriver hela raden under `concurrency_stamp`-kontrollen (fakta 6 ⚠). Anropare: recordern efter Q12, `SwapConfirmedAddressAsync` (`SetUserNameAsync`/`ChangeEmailAsync`) och `IdempotentAdminRoleSeeder` (`AddToRoleAsync`). Tillståndet uppstår eftersom §3c kör med api/worker igång (villkor 2). | Namnges i docblocken. |

**Två hashade rader, varav en bekräftad, är nödvändiga.** Utan dem överlever mutant 3 (en stämpel för alla) och mutant 5 (bara obekräftade). R1 obekräftad är det tillstånd som LoginChallengeProofTests test 2 citerar.

**Stegen (allt i en metod, i den här ordningen):**

- **J0** `GetMigrations()` innehåller `ThisMigration` och `20260917195454_DropAuthProviderColumns`. Kör `IMigrator.MigrateAsync(PreviousMigration)`.
- **J1** Skapa R1, R2 och R3. Ladda R2 via `FindByIdAsync` i ett **eget** DI-scope som hålls öppet.
- **J2** Förläsning, samma predikat som §3c: `select count(*) … where password_hash is not null` = 2. Fånga `to_jsonb(u)::text` per rad samt kolumnkatalogen för `AspNetUsers`.
- **J3** Utan container: `new <Migration>().UpOperations` är exakt `SqlOperation`(er) med `Sql == <const>` (plus eventuell guard). Ingen schemaoperation. `DownOperations` kastar Q2:s typ om Q2 väljer C#-formen.
- **J4** `IMigrator.MigrateAsync(ThisMigration)`; `GetAppliedMigrationsAsync().Last() == ThisMigration`.
- **J5** På R1 och R2:
  - `password_hash` är NULL.
  - `security_stamp` är inte null, skiljer sig från värdet före, och R1:s nya stämpel skiljer sig från R2:s.
  - `concurrency_stamp` skiljer sig från värdet före (om Q1 binder det, se F-1).
  - `to_jsonb(u) - 'password_hash' - 'security_stamp' - 'concurrency_stamp'` är lika med J2. Uttryckligen också: R1 har `email_confirmed = false`, R2 har `true`.
  - Aktörens eget predikat godtar stämpeln: `userManager.GetSecurityStampAsync(<färsk R1>)` returnerar den nya stämpeln. Det är den medlem token-providern anropar, och den kastar på NULL.
- **J6** R3:s **hela** `to_jsonb(u)` är lika med J2, båda stämplarna inräknade.
- **J7** Återläsning: `count(*) where password_hash is not null` = 0, och kolumnkatalogen är oförändrad.
- **J8** Idempotens: kör konstanten via `ExecuteSqlRawAsync`. Alla tre radernas `to_jsonb` är lika med läget efter J5/J6.
  - Är konstanten en bar `UPDATE` ska returvärdet dessutom vara 0.
  - I `DO $$`-form returneras -1, och då bär bara effektjämförelsen.
- **J9** Den stale R2-instansen från J1 sparas med `userManager.UpdateAsync(stale)`. `Errors` ska innehålla `nameof(IdentityErrorDescriber.ConcurrencyFailure)`, och R2:s `password_hash` på disk ska fortfarande vara NULL. Villkorat av Q1, se F-1.
- **J10** `IMigrator.MigrateAsync(PreviousMigration)` kastar Q2:s typ. Assert på typ, eller vid SQL-formen `PostgresException.SqlState == PostgresErrorCodes.RaiseException`, aldrig på meddelandetext. Därefter är `Last() == ThisMigration` och R1–R3:s `to_jsonb` oförändrade.
- **J11** §3c:s eget rollback-kommando: `IMigrator.GenerateScript(ThisMigration, PreviousMigration)`. Med C#-formen kastar det samma typ, så operatören får inget skript alls. Med SQL-formen ska skriptet bära vägran före `DELETE` ur historiktabellen. Det här är ett underlag till Q2/Q5, ingen dom.
- **J12** En **färsk** `AppIdentityDbContext` kör `MigrateAsync(ThisMigration)` och blir klar, med `Last() == ThisMigration`. Det bevisar att vägran inte lämnar kvar migrationslås eller halvtillstånd. Npgsql:s standardtimeout på 30 s (fakta 4) gör ett läckt lås till ett rött test i stället för en hängande CI.
- **J13** (rekommenderad; villkorad av att Q12 behåller grenen) Den levande aktören godtar den migrerade legacy-raden: `new IdentityInboxProofRecorder(userManager).RecordAsync(R1)` i ett färskt scope ger `FirstProofRecorded`. R1 har då `email_confirmed = true`, `password_hash` NULL och en stämpel skild från J5:s. Det är den pin som test 2:s seam citerar.

**Retarget av `DropAuthProviderColumnsMigrationTests`** (precedensen är `UnmapJobSeekerDisplayNameMigrationTests:28-29`):
- Steg 1 (`:188`) och steg 4 (`:225`) blir `db.GetService<IMigrator>().MigrateAsync(ThisMigration, ct)`.
- `:229` `GetPendingMigrationsAsync().ShouldBeEmpty()` blir `GetAppliedMigrationsAsync().Last().ShouldBe(ThisMigration)`, eftersom 5b ligger väntande där.
- Docblocken (`:19-21`, "head → previous → head") blir "ThisMigration → previous → ThisMigration, aldrig assemblyns head, eftersom 5b:s Down vägrar". Kommentaren "Head:" vid `:187` följer med.
- Seed och assertions i övrigt är oförändrade. Den raden har NULL-hash och berörs aldrig av 5b.

---

### Q16: LoginChallengeProofTests test 1 och 2, plus recorderns unit-pins

**Båda testerna retargetas; inget raderas.** `SeedLegacyPasswordAccountAsync` (`:447-463`) och kommentaren på `:267` raderas. `IdentityRow` tappar `PasswordHash` och får `ConcurrencyStamp`: varje `UserStore.UpdateAsync` sätter en ny, så likhet på den är en exakt pin på att ingen Identity-skrivning skett.

**T1** → `A_confirmed_account_logs_in_by_code_and_its_identity_row_is_unchanged`
- **Seed:** `AuthTestHelpers.RegisterAndGetSessionIdAsync`, alltså den levande skrivaren `CreatePasswordlessUserAsync` plus en Persistent-session.
- **Guard:** `before.EmailConfirmed` är true. Det är det tillstånd `AlreadyConfirmed` vilar på.
- **Assertions:**
  - Efter en felaktig kod och sedan rätt kod är raden lika med `before`. Det vilar på att varken `AlreadyConfirmed`-grenen eller fel-kod-vägen skriver till Identity. `ConcurrencyStamp`, `AccessFailedCount` och `LockoutEnd` är de fält som ser det.
  - Den befintliga sessionen ger 200. Det vilar på att `AlreadyConfirmed` inte återkallar något.
- **Ingen pin krävs:** tillståndet produceras av `src/`.

**T2** → `A_first_proof_of_an_unconfirmed_inbox_confirms_it_rotates_the_stamp_and_revokes_every_earlier_session`. Villkorat av att Q12 behåller `FirstProofRecorded`-grenen; tas grenen bort raderas T2 och `LoginProofTests:393-428`.
- **Seed:** `SeedUnconfirmedLegacyAccountAsync`: `UserManager.CreateAsync(user)` med `EmailConfirmed = false`, **utan hash**, plus en Persistent-session. Det uppfyller acceptanskravet "legacy seed gone".
- **Docblocken namnger aktörerna:**
  - Den pensionerade `POST /auth/register` skrev raden obekräftad (`41a49394^`).
  - 5b:s `Up` nollade hashen och lämnade `email_confirmed = false`. Det pinnas på exakt den raden i `NullPasswordHashesMigrationTests` J5, och J13 visar att recordern godtar den migrerade raden.
  - Den nuvarande skrivaren föds bekräftad: `LoginChallengeCompleteTests.A_new_address_…`.
  - Den tidigare sessionen mintades av pensionerad register (instant-login) eller login före `41a49394`. Hur länge en sådan kan leva begränsas av den persistenta `AbsoluteTtl` i `SessionStoreOptions`. Citera optionsnamnet, inte talet (§5 `Comments:`).
- **Assertions och vad de vilar på:**
  - `after.EmailConfirmed` är true, och `after.SecurityStamp` skiljer sig från `before`. Aktör: recorderns enda sparning.
  - Den tidigare sessionen ger 401 och den nya 200. Aktör: `PasswordlessSessionGrant.cs:47-48`.
  - Exakt en `InboxProvenAuditEventType`-rad.
  - `after.PasswordHash.ShouldBeNull()` raderas. NULL→NULL är vakuöst.

**Unit-pins i `IdentityInboxProofRecorderTests`** (förutsätter att Q12 väljer `UpdateSecurityStampAsync`):
- **U1 (`:35`, retarget):** gör den metodagnostisk:
  `_users.ReceivedCalls().Select(c => c.GetMethodInfo().Name).Where(n => n.EndsWith("Async", StringComparison.Ordinal)).ShouldBe([nameof(UserManager<ApplicationUser>.FindByIdAsync)])`.
  Suffixfiltret håller undan eventuella anrop från basklassens konstruktor. En `DidNotReceive` på en enskild metod släpper igenom en annan skrivmetod.
- **U2 (ny):** `An_unconfirmed_address_is_confirmed_and_its_stamp_rotated_in_one_save`.
  - Stubba `UpdateSecurityStampAsync(user)` till Success med `.AndDoes(ci => confirmedAtSave = ci.Arg<ApplicationUser>().EmailConfirmed)`. Flaggan måste fångas vid anropet: `Arg.Is` utvärderas vid assert-tid mot det muterade objektet och ser inte ordningen.
  - Assert: `FirstProofRecorded`, `confirmedAtSave` är true, och de asynkrona anropen är exakt `[FindByIdAsync, UpdateSecurityStampAsync]`.
- **U3 (`:43`, retarget):** stubben flyttas från `RemovePasswordAsync` till `UpdateSecurityStampAsync`. Assertions oförändrade.

---

### Q17: mutantlista (fakta som ska bli röda)

**Migrationen, Up**
1. `WHERE password_hash IS NOT NULL` tas bort → J6, J8
2. Ingen stämpelrotation → J5
3. En stämpel för alla rader (konstant eller beräknad en gång) → J5 (R1 ≠ R2)
4. `security_stamp = NULL` → J5 (inte null, `GetSecurityStampAsync`)
5. Predikatet begränsas till obekräftade rader → J5 (R2)
6. Predikatet begränsas till bekräftade rader → J5 (R1)
7. Sätter också `email_confirmed = true` → J5 (R1), J13
8. Rör en annan kolumn (lockout, `access_failed_count`, `user_name`) → J5 (resten av `to_jsonb`)
9. En schemaoperation läggs i Up → J3, J7
10. Up kör en annan sträng än konstanten (SPOT bruten) → J3
11. `concurrency_stamp` roteras inte (om Q1 binder rotation) → J9
12. Fel kontext, eller id som sorteras före `DropAuthProviderColumns` → J0/J4/J5
13. **Överlever:** hash och stämpel i två satser i samma migrationstransaktion. Det går inte att skilja utifrån, så det dödas bara av granskning mot ADR:ns "same statement".

**Migrationen, Down**
14. Tom `Down` → J10
15. Annan undantagstyp än Q2 binder → J3, J10
16. En `Down` som "återställer" och tar bort historikraden → J10
17. Vägran läcker lås eller halvtillstånd → J12
18. Ett rollback-skript genereras trots C#-formen → J11

**Recordern**
19. Raden raderas utan ersättning (fakta 5:s tysta förlust) → U2, T2, J13
20. `UpdateAsync` i stället (bekräftar, roterar inte stämpeln) → U2, T2, J13
21. Två sparningar → **bara U2**
22. Flaggan sätts efter sparningen → U2, T2
23. Resultatet ignoreras → U3
24. Skrivning på bekräftad-grenen → U1, T1 (`ConcurrencyStamp`)
25. `RemovePasswordAsync` behålls bredvid den nya sparningen → U2, `NoPasswordSymbolTests` (Infrastructure)
26. Grenen tas bort (alltid `AlreadyConfirmed`) → T2, J13

**NoPasswordSymbolTests-scopet** (G1–G5 definieras i F-2)
27. Infrastructure läggs aldrig till → G1 (egen `[Fact]` som läser Infrastructure)
28. `RemovePasswordAsync` läggs tillbaka → teoriraden för Infrastructure, men **bara** om G2 håller
29. Scope-predikatet läser `type.Namespace` rakt av → G2. Nästlade state machines (`…IdentityInboxProofRecorder/<RecordAsync>d__N`) har tomt namespace, och det är där anropet faktiskt ligger.
30. Undantaget vidgas från `…Identity.Migrations` till `…Identity` → G3, G1
31. Referensarmen behålls modulbred och undantas per måltyp → ingen test fångar det; det är en undantagslista i förklädnad (CTO F3)
32. Anonyma typer undantas i klump → en `new { password = … }` i scope överlever → G4
33. En avgränsad instruktionsgenomgång som tappar attributtyper (`[PasswordPropertyText]` på en medlem i scope) → G5

**Integritetstexten**
34. Bara sv stryks (eller bara en) → **bara T-P1**. Paritetstestet är grönt eftersom arrayens längd är densamma.
35. Ingen av dem stryks → T-P1
36. Hela `list[0]` tas bort i båda → T-P2 (valfri)
37. Datum och konstant flyttas inte ihop → `TermsAcceptanceVersionsMatchPublishedPolicyTests` (`:43-51`)
38. **Överlever:** varken datum eller konstant flyttas, när merge-dagen är senare än 2026-09-25 → F-5

---

### Fynd (min skala: **Blocker** = ett acceptanskrav som inte kan pinnas ärligt; **Major** = ett acceptanskrav eller en bunden invariant som ingen planerad test kan göra röd, eller en öppen väg förbi ett postvillkor; **Minor** = täckning/hygien som inte lämnar ett acceptanskrav opinnat)

**F-1 Major (underlag till Q1): `concurrency_stamp`.** Ett nytt hash-värde kan inte skrivas, men den gamla hashen kan skrivas tillbaka.
- Utan att `concurrency_stamp` roteras i samma sats kan `UserStore.UpdateAsync` göra det. Den gör `Attach` + `Update`, alltså alla kolumner, med `WHERE concurrency_stamp = <laddat värde>`, från vilken levande skrivare som helst vars laddning korsar satsen.
- Det bryter acceptanskravet "NULL for every row". Båda tidsordningarna stängs av rotationen.
- J9 är mätningen. Binder Q1 inte rotationen blir J9 röd, och det är fyndet.

**F-2 Major (underlag till Q13; CTO avgör enligt F3): instrumentet kan i dag inte avgränsa Infrastructure utan en undantagslista.**
- `module.GetMemberReferences()`/`GetTypeReferences()` säger inte vilken typ som refererar. Därför kan `ConfigurationOptions::get_Password` inte undantas via namespace eller typ.
- Den anonyma typen från `InitialIdentity.cs:44` ligger i global namespace.
- Alternativ (a) och (b) kräver därför en ändring av instrumentet, **och bara för Infrastructure**. Assemblyerna med fullt scope behåller modultabellerna, som fångar attributtyper som instruktioner inte ser. Ändringen:
  - Referensarmen blir en instruktionsgenomgång över metoderna i scope, där nästlade och kompilatorgenererade typer räknas till sin yttersta deklarerande typ.
  - Anonyma typer räknas till sina `newobj`-ställen.
  - Attributtyper på medlemmar i scope läses.
- Dessutom måste designtidsfabrikerna (`DesignTimeIdentityDbContextFactory` ligger i samma namespace som `ApplicationUser`) undantas via en deklarerad kategori, till exempel "implementerar `IDesignTimeDbContextFactory<>`", inte via namespace.
- Vakter oavsett alternativ:
  - **G1:** mängden som skannas innehåller deklarationen `…Auth.LoginChallenges.IdentityInboxProofRecorder`.
  - **G2:** en `UserManager\`1::…`-referens finns bland symbolerna i scope. Varje sådan ligger i en async `MoveNext`, så det bevisar att genomgången når state machines.
  - **G3:** en `[Theory]` över scope-predikatet, i samma form som `A_name_is_judged_…`: snapshot ut, `ApplicationUser` in, recorderns `<RecordAsync>d__` in, designtidsfabriken ut.
  - **G4:** en rad i predikatteorin för anonyma typer.
  - **G5:** attributtyper räknas med.

**F-3 Major: strykningen i integritetstexten saknar pin.** En strykning i bara ett språk passerar alla befintliga tester. Paritetstestet pinnar nyckelstruktur och längd, inte innehåll. Det är just det #880-fel som filens egna tripwires finns till för.
- **T-P1** (i `content-legal-parity.test.ts`, lokaliserad efter plats):
  - Lövet i `privacy` som bär `/kontoidentifierare/i` respektive `/account identifier/i` ska ligga på **samma sökväg** i sv och en, och vara minst ett.
  - Det lövet matchar varken `/lösenord/i` (sv) eller `/\bpassword\b/i` (en).
  - Hela `privacy` har noll träffar på `/kryptografisk hash/i` och `/cryptographic hash/i`.
- Avgränsningen till det lövet är medveten. 6a kan med goda skäl skriva "vi får aldrig ditt lösenord från Google".
- Locatorn gäller dagens text. Behåller Q6:s ersättning inte ordet "kontoidentifierare" följer locatorn Q6.
- **T-P2** (valfri): kontroll av att lövet finns i båda språken.

**F-4 Major: invarianten "en sparning" (Q-S3, `IInboxProofRecorder.cs:4-9`) har ingen pin som ser två sparningar.**
- Mutant 21 är osynlig för E2E-testet. Skadan: en `ConcurrencyFailure` mellan två sparningar lämnar bekräftelsen kvar medan recordern kastar. Nästa inloggning svarar då `AlreadyConfirmed`, och ingen audit-rad skrivs och ingen återkallelse görs, någonsin.
- Luckan fanns före 5b, men 5b skriver om exakt den raden. U2 stänger den i samma block.

**F-5 Minor: "flytta varken datum eller konstant" överlever.**
- Paritetstestet pinnar likhet, inte att datumet har flyttats. `ebe12103` bytte policytext och version utan test, alltså samma mönster som här.
- Det som skulle döda mutanten är en pin mellan fingeravtryck av texten och version. Det är ett nytt instrument; rutten avgörs enligt §9.6 (namngiven skip eller issue).

**F-6 Minor:** de 10 seeds som skapar Identity-rader med lösenord.
- Det är ingen §5-överträdelse, eftersom ingen assertion vilar på hashen (mätt med grep). Men de håller en radform vid liv i fixturerna som ingen väg i `src/` producerar efter 5a/5b.
- Mekanisk fix: ta bort lösenordsargumentet. Alternativt en namngiven skip. CTO avgör rutten.

**F-7 Minor: tillägg till stale-listan i fakta 12.**
- `PasswordlessSessionGrant.cs:45-46` ("the removed password": Identity-skrivningen tar efter Q12 inte bort något). Den bedöms av code-reviewer som en eventuellt felaktig kommentar.
- `LoginChallengeProofTests.cs:267`, som går bort med T1.
- Recorderns egen docblock `IdentityInboxProofRecorder.cs:7-13`.

**Ingen Blocker.**

---

### Eskaleringar

**Inget till Klas från test-writer.** Till senior-cto-advisor:
1. **F-2** (Q13, enligt F3).
2. **T2-sömmen:** den citerar J5 och J13 i stället för att köra 5b:s konstant i testet. En sådan körning skulle kräva en hash-seed, vilket acceptanskravet utesluter, och en global `UPDATE` i den delade Api-containern. Jag bedömer det som §5-förenligt ("the seam names the pin when it lives elsewhere"), men det är ett formval, inte ett fynd.
3. Rutten för **F-5** och **F-6**.

### Körs med

```
dotnet build Jobbliggaren.sln
dotnet test --project tests/Jobbliggaren.Worker.IntegrationTests -- --filter-class "*NullPasswordHashesMigrationTests"
dotnet test --project tests/Jobbliggaren.Worker.IntegrationTests -- --filter-class "*DropAuthProviderColumnsMigrationTests"
dotnet test --project tests/Jobbliggaren.Api.IntegrationTests -- --filter-class "*LoginChallengeProofTests"
dotnet test --project tests/Jobbliggaren.Application.UnitTests -- --filter-class "*IdentityInboxProofRecorderTests"
dotnet test --project tests/Jobbliggaren.Architecture.Tests -- --filter-class "*NoPasswordSymbolTests"
dotnet test --project tests/Jobbliggaren.Domain.UnitTests -- --filter-class "*TermsAcceptanceVersionsMatchPublishedPolicyTests"
cd web/jobbliggaren-web && pnpm test content-legal-parity
```

- Pre-commit bygger aldrig integrationsprojekten, därav bygget av hela solution först.
- Beviset är raden `total:` (och vitest-sammanfattningen), aldrig exit-koden.

### Nästa steg (Red-ordning)

1. **Journeyn:** skapa först migrationen med tomma `Up` och `Down`, skriv sedan journeyn. Den blir röd på J5 och J10, inte på kompilering (J3 refererar konstanten, som inte finns före scaffolden).
2. **Klar att skriva mot nuvarande kod:**
   - U2 är röd mot `RemovePasswordAsync`-formen.
   - T-P1 är röd mot dagens rad 33.
   - `NoPasswordSymbolTests` med Infrastructure är röd tills F-2 är löst.
3. **Retargets:** DropAuthProvider, T1 och T2 retargetas i samma commit som recorderändringen.
4. **Väntar:** allt ovan väntar på Q1 (F-1), Q2 (typen i J3/J10/J11), Q12 (U1–U3, J13, T2) och CTO:s beslut om Q13.
