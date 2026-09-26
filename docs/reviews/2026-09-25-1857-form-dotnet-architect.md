# dotnet-architect — 5b (#1857) form round, report-only, against `41a49394`

Transcribed verbatim from the agent's SubagentHandback payload (extracted from its transcript), 2026-09-25.
Brief: `docs/sessions/2026-09-25-1857-form-brief.md`.

## Arkitektur-analys: 5b (#1857), formrond Q12–Q14

*Agent:* dotnet-architect · *Datum:* 2026-09-25 · *Bas:* `41a49394`, läst i worktreet `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\github-ci-audit-optimize-039bb7`. Sökvägarna nedan utgår därifrån om inget annat anges. · *Läge:* bara rapport, inga ändringar.
*Charter-avvikelse:* Jag gjorde två skrivskyddade Bash-anrop (`git log`/`git status` och `ls`/`find` under `bin/`) innan jag märkte att min charter inte tillåter Bash. Inget muterades, och inget fynd vilar på dem utöver HEAD-kontrollen.

### Sammanfattning
Behöver åtgärdas: 0 kritiska, 2 viktiga och 1 nice-to-have.
- **Q12:** grenen `FirstProofRecorded` behålls, och `RemovePasswordAsync` byts mot `UpdateSecurityStampAsync`.
- **Q13:** varken (a) eller (b) håller på IL-nivå. Jag föreslår (c): en rollregel per ägartyp och en målregel för medlemsreferenser i Infrastructure. Förslaget har ingen undantagslista, men det går från F3:s assemblynivå till typnivå, och därför avgör CTO.
- **Q14:** `concurrency_stamp` roteras i samma sats (Viktigt), och `Down` kastar `NotSupportedException` i C#.

### Rekommendation

**Q12: recordern** (`src/Jobbliggaren.Infrastructure/Auth/LoginChallenges/IdentityInboxProofRecorder.cs:14-40`)
- **Grenen behålls.** Mätt vid `41a49394`:
  - I `src/` läser bara recordern `EmailConfirmed` (`:23`).
  - Värdet `true` skrivs av `UserAccountService.cs:35` och av recordern. Ingen levande skrivare skriver `false`.
  - `false` är alltså ett legacy-tillstånd från pensionerade `/auth/register`, och grenen är enda vägen ut ur det.
  - Tas grenen bort ändras tre kontrakt (porten, enumen och grant) och ett audit-event. Det är en ändring av säkerhetsbeteende och hör till security-auditors Q11, inte till 5b.
  - Grenen kan tas bort först när tillståndet är uppmätt tomt och deklarerat onåbart (AGENTS.md §5 `Tests:`).
- **`UpdateSecurityStampAsync`, inte `UpdateAsync`.**
  - Båda går via `UpdateUserAndRecordMetricAsync` till `UserStore.UpdateAsync`. Det blir en UPDATE under `concurrency_stamp`, flaggan följer med och felytan är densamma (aspnetcore `main`, `UserManager.cs`, läst 2026-09-25).
  - Rotationen kostar ingenting.
  - Den håller `PasswordlessSessionGrant.cs:32` ("a credential changed") sann, och den stämmer med att 5b:s sats roterar stämpeln där en kredential tas bort.
  - `AddDefaultTokenProviders()` är fortfarande registrerad.
  - `ConfirmEmailAsync` roterar inte stämpeln och är token-bunden, så den passar inte här.
- **Metodkroppen jag binder** (bara rad 27 ändras):
```csharp
public async Task<InboxProof> RecordAsync(Guid userId, CancellationToken ct)
{
    // The caller resolved this id from the account table a moment ago, so absence is a race with a
    // hard delete, not a state to answer.
    var user = await userManager.FindByIdAsync(userId.ToString())
        ?? throw new InvalidOperationException($"User {userId} vanished between resolve and inbox proof.");

    if (user.EmailConfirmed)
        return InboxProof.AlreadyConfirmed;

    user.EmailConfirmed = true;
    var result = await userManager.UpdateSecurityStampAsync(user);

    // Nothing may follow a write that did not happen: no invalidation, session or audit row. A
    // ConcurrencyFailure is reachable — two first proofs racing on two live records. Codes only: an
    // Identity Description can interpolate the address.
    if (!result.Succeeded)
    {
        throw new InvalidOperationException(
            $"Inbox proof for user {userId} was not persisted: "
            + string.Join("; ", result.Errors.Select(e => e.Code)));
    }

    return InboxProof.FirstProofRecorded;
}
```
- **Dokumentationen.** Varje ändring är en strykning plus ihopfogning, och ingen lägger till ett påstående.
  - `IdentityInboxProofRecorder.cs:8-12` blir: *"The Identity side of a first passwordless inbox proof (#1735, security-auditor Q-S3). The flag and the stamp rotation leave in ONE `UPDATE`: `<see cref="UserManager{TUser}.UpdateSecurityStampAsync"/>` rotates the stamp in memory and then saves the whole user, the flag set just before included."* Meningen om "the pre-registered password still works" stryks och ersätts inte.
  - `IInboxProofRecorder.cs:4-7` blir: *"Records a first passwordless proof of an account's inbox (#1735, security-auditor Q21/Q-S3): the confirmation and the stamp rotation are ONE write. Reachable only from …"* Resten står kvar.
  - `InboxProof.cs:10-11` blir: *"The address was unconfirmed: it is now confirmed and the security stamp is rotated, in one Identity write."*
  - `PasswordlessSessionGrant.cs:45-46` blir: *"… from here on CancellationToken.None: every earlier session is revoked BEFORE the new one exists."* Raden saknas i briefen, se Fynd 2.
- **Enhetspinnarna** `IdentityInboxProofRecorderTests.cs:35,43` byter metod. Formen avgör test-writer i Q16. En mutant måste dö: att flaggan sätts efter sparningen. Den dödas genom att läsa `EmailConfirmed` i `.Returns(ci => …)` vid själva anropet. `Received(Arg.Is(...))` läser objektet i efterhand och ser då inte ordningen.

**Q13a: predikatet (c)**
- **Mätt:** jag körde ripgrep på binären och läste bara UTF-8-heaparna #Strings och #Blob.
  - Filen var `c:\tmp\jbl-1743-b\src\Jobbliggaren.Infrastructure\bin\Debug\net10.0\Jobbliggaren.Infrastructure.dll`, bygget i #1845:s worktree efter 5a. Den är inte ombyggd vid `41a49394`.
  - Worktreets egna `bin/` är från före 5a (de innehåller fortfarande `PasswordResetDispatchService`) och går inte att använda.
  - Namn som innehåller `password` efter strippen är exakt:
    - `RemovePasswordAsync` och `get_Password`;
    - den anonyma typens `<password_hash>j__TPar`, `<password_hash>i__Field` och `get_password_hash`;
    - två `password_hash` i en blob, vilket stämmer med typens `DebuggerDisplay`.
  - Varken `get_PasswordHash`, `PasswordHasher` eller `PasswordOptions` förekommer. Fakta 7 stämmer alltså för namnen.
- **Resonerat, inte kört:**
  - #US-literalerna är UTF-16, som mina verktyg inte läser. Enligt källan bär de `"PasswordHash"`/`"password_hash"` i snapshot och fem Designers, `"…Password=local"` två gånger, och den anonyma typens `ToString`-format.
  - Cecil ger nästlade typer (closures `<>c`) tomt `Namespace`.
  - Anonyma typer ligger på toppnivå i det globala namnrummet och är inte nästlade i `InitialIdentity`.
  - `GetTypeReferences()` och `GetMemberReferences()` är modulglobala och har ingen ägartyp.
- **Därför fungerar inte (a) som den är skriven.** En namnrymdsregel når inte den anonyma typen. Ingen typ- eller namnrymdsregel når Redis-referensen, eftersom referensen saknar ägare. Och att peka ut fabrikerna och Redis-kredentialen var för sig är en undantagslista i praktiken.
- **(b) fungerar inte heller.** En inkluderingslista över namnrymder släpper igenom allt i nya namnrymder utan skanning. Den behöver dessutom samma maskineri, eftersom `DesignTimeIdentityDbContextFactory` ligger i `…Infrastructure.Identity`.
- **Acceptansen behöver inte formuleras om.**
- **(c) bygger på två skäl som deklareras i testet. Ingen symbol och ingen av våra typer namnges:**
  1. **Roll, uniformt över alla fem assemblies.**
     - En typ bedöms efter sin yttersta deklarerande typ. Den ligger utanför scopet om den ärver `Migration` eller `ModelSnapshot` eller implementerar `IDesignTimeDbContextFactory<>`.
     - Sådana typer körs bara under Migrate eller `dotnet ef`, alltså samma skäl som för Migrate. De måste namnge `password_hash` så länge ADR 0142 behåller kolumnen.
     - En kompilatorgenererad toppnivåtyp hör till de ägare vars metodkroppar når den. Det mäts i samma instruktionsloop som Literal-armen redan går igenom.
     - Når ingen kropp typen stannar den i scopet, så regeln felar stängt.
     - För F3:s fyra assemblies ändrar regeln ingenting: alla 98 rolldeklarationer i `src/` ligger i Infrastructure (grep).
  2. **Mål, bara Infrastructure.**
     - En `MemberReference` räknas när dess deklarerande typ har scope hos oss eller i Identity.
     - En tjänsts kredential är en medlem på tjänstens options-typ. Ett användarlösenord når en Infrastructure-adapter bara via en Application-port och adapterns egna parametrar, och dem läser deklarationsarmarna.
     - `TypeReference` räknas alltid.
     - Att göra regeln uniform skulle lösa upp F3:s fyra utan att någon träff motiverar det. Jag avråder.
```csharp
private static TypeDefinition Owner(TypeDefinition type)
{
    while (type.DeclaringType is { } outer) type = outer;
    return type;
}

private static bool IsSchemaOrDesignTime(TypeDefinition owner) =>
    owner.BaseType?.FullName is "Microsoft.EntityFrameworkCore.Migrations.Migration"
                             or "Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot"
    || owner.Interfaces.Any(i => i.InterfaceType.GetElementType().FullName
                                 == "Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory`1");

// reachers: a first pass over every body. For each MemberReference operand (newobj, ldtoken, call …), take its
// declaring type's element type (.Resolve()); kept when that type is top-level and [CompilerGenerated];
// the value is Owner(the reaching type).
private static bool InScope(TypeDefinition type, ILookup<TypeDefinition, TypeDefinition> reachers) =>
    type.DeclaringType is null && IsCompilerGenerated(type) && reachers[type].Any()
        ? reachers[type].Any(owner => !IsSchemaOrDesignTime(owner))
        : !IsSchemaOrDesignTime(Owner(type));

private static bool IsUserCredentialScope(MemberReference reference) =>
    reference.DeclaringType?.GetElementType().Scope?.Name is not { } scope   // unknown: counts
    || scope.StartsWith("Jobbliggaren.", StringComparison.Ordinal)
    || scope.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal)
    || scope.StartsWith("Microsoft.Extensions.Identity.", StringComparison.Ordinal);
```
- **Så kopplas det in i `Symbols()`:**
  - Typloopen börjar med `if (!InScope(type, reachers)) continue;`.
  - MemberRef-loopen räknar en referens när `!serviceCredentialsOutOfScope || IsUserCredentialScope(r)`.
  - Flaggan följer med i TheoryData-raden (`TheoryData<Type, bool>`). Infrastructure använder `typeof(ApplicationUser)` som markör: typen är public och överlever recordern.
- **Vakuumvakter** i F3:s form:
  - Infrastructures deklarationer i scopet innehåller `.IdentityInboxProofRecorder`, alltså utesluter regeln inte adaptern som skanningen finns för.
  - De släppta referenserna innehåller en `UserManager`1`-medlem, alltså släpper målregeln igenom Identity.
- **Commit-ordningen följer F3.** Den röda körningen vid HEAD är inventeringen, och testet committas ihop med raderingen av `RemovePasswordAsync`.
- **Ny text för `NoPasswordSymbolTests.cs:16-18`:** *"**Scope, per assembly.** Domain, Application, Api, Worker and Infrastructure. Migrate is outside it: its master password is infrastructure, not the auth surface. A type is judged by its outermost declaring type, and a compiler-generated top-level type by the types whose bodies reach it. A migration, a model snapshot and a design-time context factory are outside the scope: they run only under Migrate or `dotnet ef`, and they name `password_hash` because the column exists. In Infrastructure a member reference counts when its declaring type is ours or ASP.NET Core Identity's: a backing service's credential is not a user's."* Meningen "There is no exception list" (`:22-23`) stämmer fortfarande.

**Q13b: varje träff i fakta 7**

| Träff | Arm | Utfall | Varför |
|---|---|---|---|
| `UserManager`1::RemovePasswordAsync` (`IdentityInboxProofRecorder.cs:27`) | Reference | **räknas**, rött tills raden går | scope `Microsoft.Extensions.Identity.Core` (resonerat; den röda körningen bevisar det) |
| `ConfigurationOptions::get_Password` (`RedisClientConfiguration.cs:31`) | Reference | utesluts | scope `StackExchange.Redis`, en tjänstekredential |
| `"…Password=local"` (`Persistence/DesignTimeDbContextFactory.cs:17`, `Identity/DesignTimeIdentityDbContextFactory.cs:19`) | Literal | utesluts | ägaren implementerar `IDesignTimeDbContextFactory<>` |
| `"PasswordHash"`/`"password_hash"`, snapshot och fem Designers | Literal | utesluts | en Designer är andra halvan av den partiella migrationsklassen; closures `<>c` har ägaren `Migration` eller `ModelSnapshot` |
| den anonyma typen från `InitialIdentity.cs:36-53` (`:44`): tpar, property, backing field, getter, ctor-param, `DebuggerDisplay`, `ToString` | Decl/Attr/Literal | utesluts | global typ som bara nås från `InitialIdentity/<>c` (`newobj`, och `ldtoken get_id` i `PrimaryKey`), så ägaren är `InitialIdentity` |
| 5b:s SQL (`internal const` och inlinad `ldstr` i `Up`), klassnamn och `[Migration]`-sträng | Const/Literal/Decl/Attr | utesluts | ägaren är migrationen. **Det kräver att konstanten deklareras på migrationsklassen** (Q14) |

Efter 5b fångar skanningen fortfarande:
- varje lösenords-API i Identity (`AddPasswordAsync`, `CheckPasswordAsync`, `ChangePasswordAsync`, `ResetPasswordAsync`, `GeneratePasswordResetTokenAsync`, `IPasswordHasher`, `PasswordOptions`, `PasswordSignInAsync`, `get_/set_PasswordHash`);
- varje namn, literal, konstant och attribut som vi deklarerar utanför de tre rollerna.

**Q14: migreringen och testerna**
- **Satsen (Q1):**
```sql
UPDATE identity."AspNetUsers"
SET password_hash = NULL,
    security_stamp = gen_random_uuid()::text,
    concurrency_stamp = gen_random_uuid()::text
WHERE password_hash IS NOT NULL;
```
  - `gen_random_uuid()` är core och volatile. Den beräknas per rad, och två anrop ger två värden.
  - Identity behandlar stämpeln som en opak sträng och kastar bara vid null.
  - En omkörning påverkar 0 rader och roterar inga stämplar igen.
  - `concurrency_stamp` behandlas i Fynd 1.
- **Formen** är en `internal const string` på migrationsklassen, som en vanlig `UPDATE` utan `DO $$`.
  - `ExecuteSqlRawAsync` returnerar antalet påverkade rader för en vanlig UPDATE men −1 för `DO`. Det talet är testets mätning av idempotensen.
  - §3c:s förläsning och återläsning är operatörens mätning, så `RAISE NOTICE` tillför inget.
  - Formen är db-migration-writers. Vid oenighet avgör CTO.
- **Lås:**
  - Radlås tas bara på träffade rader, och tabellen får ROW EXCLUSIVE. Läsare blockeras inte.
  - En samtidig `UserStore.UpdateAsync` på en träffad rad väntar på commit, missar sedan sin stämpel och får `ConcurrencyFailure`.
  - Standardvärdet på 30 s för `CommandTimeout` spelar ingen roll vid två rader.
- **Down (Q2):** `throw new NotSupportedException("…")` i C#.
  - `IrreversibleMigrationException` finns inte i EF Core. Kontrollerat i API-indexet för `Microsoft.EntityFrameworkCore.Migrations`, efcore-10.0, 2026-09-25.
  - C#-kastet sker när EF bygger `DownOperations`.
    - I migratorn görs det lazy, inne i loopen och före 5b:s kommandon, och undantaget kastas vidare oinslaget. Det bygger på en sammanfattad läsning av efcore `release/10.0` `Migrator.cs` 2026-09-25, och testet mäter det.
    - I `migrations script <5b> <prev>` görs det eagerly. §3c:s rollback-form vägrar alltså redan vid genereringen och rör inte databasen.
  - `RAISE EXCEPTION` skulle ge ett skript som ser körbart ut.
  - Journey-testet assertar `Should.ThrowAsync<NotSupportedException>` och att `GetAppliedMigrationsAsync().Last()` fortfarande är 5b. Formen är vägran i `DisplayNameNullableMigrationTests.cs:185-201`.
- **Scaffold (Q3):** `dotnet ef migrations add NullPasswordHashes --context AppIdentityDbContext -o Identity/Migrations`.
  - Modelldiffen bör vara tom.
  - Snapshoten ändras inte, utom `ProductVersion` på `AppIdentityDbContextModelSnapshot.cs:21` om den lokala `dotnet ef` inte är 10.0.10. Den ändringen noteras och återställs inte för hand.
  - `EnsureSchema` behövs inte, eftersom `Up` skrivs för hand med `migrationBuilder.Sql(...)`.
  - Namnet fungerar.
- **Retargeten av `DropAuthProviderColumnsMigrationTests`:**
  - `:188` och `:225` byts till `GetService<IMigrator>().MigrateAsync(ThisMigration, ct)`.
  - `:229` byts till `GetAppliedMigrationsAsync()).Last().ShouldBe(ThisMigration)`, som i `UnmapJobSeekerDisplayNameMigrationTests.cs:188`.
  - Docblocket `:20` ("head → previous → head … at head") och kommentaren `:187` följer med.
  - `:22-23` stryks: meningen "No other test … populated identity table" blir falsk.
- **Journey-testet:** `tests/Jobbliggaren.Worker.IntegrationTests/Migrations/NullPasswordHashesMigrationTests.cs`, med egen container och **en** `[Fact]`, provisionerad med `TestDatabaseProvisioner(includeIdentitySchema: true)`. Testet stannar vid `ThisMigration`, aldrig vid head.
  - En journey behöver styra databasens migreringsposition, och det kan ingen delad fixture på head göra.
  - `OwnContainerPerTestTests` pinnar bara Api.IntegrationTests.
  - Alla sex migreringssyskon har egen container.
- **§3c:** `BootstrapRunbookParityTests` pinnar exakt en rad med `" run --rm "`, så 5b återanvänder det enda kommandot. Per migrering ändras bara förvillkor 3–5, återläsningen och rollbacken. Rollbacken följer av kastet: det finns inget `Down`-skript att generera. Texten är security-auditors Q5.

### Fynd

**[Viktigt]** 5b:s sats (ännu inte skriven; Q1)
**Vad:** Utan rotation av `concurrency_stamp` kan en UserManager-skrivning som laddade användaren före migreringens commit skriva tillbaka den gamla hashen.
**Varför:**
- `UserStore.UpdateAsync` gör `Attach`, sätter en ny stämpel och gör `Context.Update(user)`. Alla kolumner markeras som ändrade, och WHERE kontrollerar bara `id` och den ursprungliga `concurrency_stamp` (aspnetcore `main` `UserStore.cs`, läst 2026-09-25).
- §3c:s förvillkor 2 förutsätter att api och worker kör, så recordern och change-email (`UserAccountService.cs:171-175`) kan vara igång mot en rad som har hash.
- En återskapad hash bryter "NULL for every row" och Klas "Ja, radera".
- Med rotation omvärderar Postgres (READ COMMITTED) WHERE efter commit. Då påverkas 0 rader, `ConcurrencyFailure` uppstår och recordern kastar, vilket är dess redan dokumenterade racegren (`IdentityInboxProofRecorder.cs:29-31`).

**Föreslagen åtgärd:** Använd satsen i Q14, där `concurrency_stamp = gen_random_uuid()::text` ingår i samma sats.

**[Viktigt]** Inventeringen i fakta 5 och 12 saknar rader som den här PR:en gör falska
**Vad:**
- `src/Jobbliggaren.Infrastructure/Auth/LoginChallenges/IdentityInboxProofRecorder.cs:8-12` (fakta 5 listar bara porten och enumen)
- `src/Jobbliggaren.Application/Auth/LoginChallenges/PasswordlessSessionGrant.cs:45-46` ("a session opened with the removed password")
- `docs/decisions/0142-passwordless-auth-one-page-code-or-link-oauth-ready.md:873-875` ("removes the password"; samma stycke som briefens `:878-879`)
- `tests/Jobbliggaren.Api.IntegrationTests/Auth/LoginChallengeProofTests.cs:267` ("password and stamp survive until 5b") och testnamnet på `:273`. Test-writers Q16 hanterar dem; de står här så att svepet hittar dem.
- `tests/Jobbliggaren.Worker.IntegrationTests/Migrations/DropAuthProviderColumnsMigrationTests.cs:20-23`, som blir falsk av retargeten och av journey-testet.

**Varför:** Enligt AGENTS.md §5 `Comments:` är en faktiskt felaktig kommentar en defekt. Mätt med grep på `until 5b|in 5b|removed password|InboxProof` vid `41a49394`.
**Föreslagen åtgärd:** Skriv om enligt Q12 och Q14. Varje rad stängs genom strykning, utan ny påståendemening (§9.6).

**[Nice-to-have]** `tests/Jobbliggaren.Architecture.Tests/NoPasswordSymbolTests.cs:191-196`
**Vad:** Skanningen ser inte `UserManager.CreateAsync(TUser, string)`.
- En MemberRefs FullName saknar parameternamn: `…::CreateAsync(!0,System.String)`.
- Den pensionerade aktörens form (`CreateAsync(user, pw)`) går igenom om adapterns egen parameter inte heter något med password.

**Varför:** Konsekvensen är begränsad. Varje *läsare* av en hash (`CheckPasswordAsync`, `PasswordSignInAsync`, `VerifyHashedPassword`) har namnet och fångas. En hash som ingenting läser är ingen kredential.
**Föreslagen åtgärd:** Två vägar. CTO routar enligt §9.6.
- För MemberRefs med Identity-scope: gör `Resolve()` och skicka de upplösta parameternamnen som Signature-symboler.
  - Resolvern får `AddSearchDirectory(Path.GetDirectoryName(typeof(UserManager<>).Assembly.Location)!)`, eftersom delade ramverket inte ligger i testets bin.
  - Om upplösningen misslyckas ska testet falla.
- Alternativt en namngiven skip.

### Eskalering till Klas
Ingen.
- Q13 går tillbaka till CTO enligt F3, eftersom predikatet ändrar F3:s deklarerade granularitet från assembly till ägartyp.
- Att grenen behålls i Q12 bekräftar security-auditor i Q11.

### Referenser
- AGENTS.md §2.1, §2.2, §3 (Errors), §5 (`Comments:`, `Tests:`); CLAUDE.md §9.6
- ADR 0142 `:873-879`, `:1285-1286`, `:1804-1814`
- `C:\DOTNET-UTB\JobbPilot\docs\reviews\2026-09-25-1743-form-cto.md` F3 (`:109-122`); `…\2026-09-25-1845-dotnet-architect.md` (NTH om attributarmen och Infrastructures inträde)
- `C:\DOTNET-UTB\JobbPilot\docs\sessions\2026-09-25-1857-form-brief.md`
- Externt, läst 2026-09-25:
  - dotnet/aspnetcore `main`: `src/Identity/Extensions.Core/src/UserManager.cs` och `src/Identity/EntityFrameworkCore/src/UserStore.cs`
  - dotnet/efcore `release/10.0`: `src/EFCore.Relational/Migrations/Internal/Migrator.cs` (sammanfattad läsning)
  - learn.microsoft.com, namnrymden `Microsoft.EntityFrameworkCore.Migrations`, moniker efcore-10.0
- Mätt IL: `c:\tmp\jbl-1743-b\src\Jobbliggaren.Infrastructure\bin\Debug\net10.0\Jobbliggaren.Infrastructure.dll` (bara UTF-8-heaparna)
