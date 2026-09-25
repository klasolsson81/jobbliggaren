# security-auditor — 5b (#1857) form round, report-only, against `41a49394`

Transcribed verbatim from the agent's SubagentHandback payload (extracted from its transcript), 2026-09-25.
Brief: `docs/sessions/2026-09-25-1857-form-brief.md`. This report carries her signature on the backup decision (Q5).
Where she writes "Amendment (11)", the amendment landed as ADR 0142's Amendment 2026-09-25 (13): the number is
set at write time.

## Security-audit: 5b (#1857, epik #1732), formrond före kod, ingen PR ännu
**Status:** ✓ Godkänd med villkor. Jag signerar backupbeslutet (Q5) med fyra ändringar. Två Major-villkor (M-1, M-2) ska in i formen, och byggs PR:en utan dem blockerar jag den. Signaturen i Q5 vilar på M-1.
**Auktoritet:** GDPR art. 5(1)(a)(c)(e)(f), 5(2), 12(1), 13, 24(1), 30(1), 32(1)(b), 35(3) · ADR 0142 D10, "Attempt budget" (trigger 6), "Open — Klas decides" svar 2–3, Amendment 2026-09-19 (:873-879), Amendment (10) · `vps-deploy-stack.md` §3a (:286-289), §3c (:453-537) · CLAUDE.md §9.6 · AGENTS.md §5 (`Tests:`, `Comments:`), §8 p. 8.
**Mätt mot:** worktree `41a49394`, 2026-09-25. Registret är gitignorerat och lästes i huvudkopian (mtime 2026-09-25 16:41). Via `gh`: #197 OPEN, #1857:s kropp (m-2 står där), #734:s villkorstabell, och repot är PUBLIC. Jag har inte rört någon databas eller låda och inte ändrat något.

**Brief-fakta jag vilar på, omätta:**
- **Fakta 5 ✓:** `IdentityInboxProofRecorder.cs:26-27`, och `RemovePasswordAsync` är metodens enda sparning.
- **Fakta 6 ✓:** `git grep` i `src/` hittar stämpeln bara i `UserAccountService.cs:174-175` (mint och konsumtion i ett anrop), och ingen `SignInManager` används.
  - Tillägg: `AddDefaultTokenProviders()` (`DependencyInjection.cs:1701`) registrerar tre TOTP-providers vars nyckel är stämpeln. Inget flöde anropar dem.
- **Laddning-sedan-sparning av `AspNetUsers`:** `UserAccountService.cs:156,175`, recordern `:27` och `IdempotentAdminRoleSeeder.cs:140`.
- **Fakta 9 ✓:** `bootstrap` applicerar hela den väntande mängden och har inget mål (`Migrate/Program.cs:365,374`).
- **Fakta 11 ✓:** #197 är OPEN, och rad 29–31 har tom datumcell (`vps-deploy-stack.md:910-912`).
- **Fakta 10 ✓:** `content-legal.json` har exakt en rad per språk som nämner lösenord eller hash (`grep -c` = 1/1).

### Svar Q5–Q11

**Q5. Backupbeslutet: SIGNERAT, med fyra ändringar.**

Sessionens fem grunder håller:
- Ingen route har tagit emot lösenord sedan 5a B. Det gäller i koden nu, och på lådan från grind 0(a).
- En återställning ger en kredential som ingenting läser, och den återskapar det Klas har beslutat att radera (svar 3).
- En dump vore en klartextkopia.
- Offsite-backupen körs inte, och det är mätt.
- Grund 5 (ägarskapet) kan upphöra. Därför gör jag den till ett lapse-villkor i stället för en grund.

Jag lägger till den grund som bär mest. Migrationens enda irreversibla effekt är den avsedda: ett `UPDATE` i EF:s transaktion, byggt från samma konstant som testet kör, ensamt i sin körning.

Ändringarna:
- **(a) Ingen kopia av något slag:** ingen dump, ingen Netcup-snapshot och ingen kopierad tabell. En snapshot är lika mycket en kopia av hasharna som en dump är.
- **(b) 5b delar aldrig körning.** GO:t namnger exakt `{5b}`. Skälet är att `bootstrap` applicerar allt som väntar, och en reverserbar migration i samma körning går inte att rulla tillbaka förbi en `Down` som kastar.
  - Därför appliceras 6d innan 5b mergar (grind 0(b)).
  - Väntar 6d ändå när 5b redan ligger i `latest`, körs 6d från en pinnad revision före 5b-mergen (≥ `93a85abe`) genom enheten (§3b). Aldrig som `{6d, 5b}`.
- **(c) Beslutet gäller bara medan varje konto är den ansvariges.** Det enda felet beslutet inte kan reparera är en sats som träffar fel kolumn på en tredje persons konto. På sina två egna konton kan den ansvarige reparera ett sådant fel själv.
  - Upphör villkoret väntar 5b på en ny signatur. Den blir troligen en avgränsad förkopia av de kolumner 5b inte rör, aldrig `password_hash` eller `security_stamp`.
- **(d) Migrationen kör i EF:s transaktion, utan `suppressTransaction`.** Först då är "a failed apply rolled back" sant för 5b.
  - Jag föredrar en C#-`throw` i `Down`: vägran sker innan något SQL genereras, så inget skript som liknar en återställning kan finnas. Formen är db-migration-writers, och texterna nedan fungerar med båda.

**Var beslutet registreras:**
- Beslut, grund och lapse-villkor har ett enda hem: ADR 0142 Amendment (11), med texten nedan.
- §3c bär den operativa regeln och pekar dit.
- Den här rapporten är signaturen. Den promotas med `git add -f` i 5b:s PR, på samma sätt som ADR 0142:s övriga rapporter.
- Körningsposten följer §3c:s sista rad.
- Ingen text nedan innehåller ` run --rm `, så `BootstrapRunbookParityTests` påverkas inte. ⟨…⟩ fylls av sessionen.

Förvillkor 4, läggs till efter 6d:s förläsning:
~~~markdown
   For 5b (`⟨5b id⟩`), two counts, recorded with the UTC time:

   ```bash
   docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -tAc \
     "select count(*) from identity.\"AspNetUsers\";"
   docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -tAc \
     "select count(*) from identity.\"AspNetUsers\" where password_hash is not null;"
   ```

   Call them T and N. No value of N blocks the run: N = 0 only means nothing is left to null. In the same
   visit, read who owns the accounts, by count and never by printing a row. Precondition 5 needs every one
   to be the controller's.
~~~

Förvillkor 5, ersätter hela punkten:
~~~markdown
5. **Reversibility, or a signed decision that there is none.** Every migration in the set has a `Down` that
   restores the state the pre-read measured, and for those the `Down` script is the backup. The one exception
   is 5b (`⟨5b id⟩`), whose `Down` throws. Its backup decision, signed by `security-auditor` and recorded in
   ADR 0142 Amendment (11), is that **no copy of any kind is made for it** — no dump, no Netcup snapshot, no
   copied table — because the hashes are what it exists to destroy. The decision holds only while every
   account is the controller's (precondition 4's reading). If one is not, 5b does not run until
   `security-auditor` signs again. 5b never shares a run: its GO names exactly `{⟨5b id⟩}`, so every
   reversible migration before it has been applied and read back first. **No dump is taken for any
   migration:** `pg_dump -n identity` would be a second mechanism and a plaintext copy of every address.
~~~

Återläsning, läggs till efter 6d:s block:
~~~markdown
For 5b:

```bash
docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -tAc \
  "select count(*) from identity.\"__EFMigrationsHistory\" where migration_id = '⟨5b id⟩';"
docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -tAc \
  "select count(*) from identity.\"AspNetUsers\" where password_hash is not null;"
docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -tAc \
  "select count(*) from identity.\"AspNetUsers\";"
docker ps --filter name=jobbliggaren-api --filter name=jobbliggaren-worker --format '{{.Names}} {{.Status}}'
```

Expect `1`, `0`, the T of the pre-read, and both containers `(healthy)`.
~~~

Rollback, läggs till som tredje punkt:
~~~markdown
- **5b has no rollback after a successful apply, by decision** (ADR 0142 Amendment (11)). Its `Down`
  throws. Never generate, edit or apply a script for it, and never rebuild a pre-image by hand. A failed 5b
  apply is the first bullet: read back, expect N unchanged, and stop.
~~~

ADR 0142 Amendment (11), stycket för beslutet (ordagrant):
> **The backup decision for 5b's throwing `Down`** (`vps-deploy-stack.md` §3c precondition 5), **signed by security-auditor in 5b's form round, 2026-09-25** (`docs/reviews/2026-09-25-1857-form-security-auditor.md`): **no copy of any kind** — no dump, no Netcup snapshot, no copied table. No route has accepted a password since 5a PR B, so a restored hash is a credential nothing reads, and Klas's answer 3 is to delete it. A copy would keep, past its purpose, exactly what this part destroys (Art. 5(1)(c), (e)). The offsite backup has never run (#197). And the migration's only irreversible effect is the intended one: one `UPDATE` inside EF's migration transaction, built from the constant its test runs, alone in its §3c run, so a failed apply leaves nothing to restore. **The decision lapses if any account on the box is not the controller's when §3c runs**; 5b then waits for a new signature. Home of that check: §3c precondition 5. Reader: the operator running §3c.

**Q6. Strykningen, exakta strängar för `privacy.sections[2].list[0]`.**
- sv: `"E-postadress och kontoidentifierare, samt identifierare från en extern inloggningstjänst om du loggar in via en sådan. Dessutom tidpunkten då du godkände användarvillkoren och vilka versioner av villkoren och integritetspolicyn som gällde då. Används för att skapa och säkra ditt konto, identifiera dig i tjänsten och kunna visa att villkoren har godkänts."`
- en: `"Email address and account identifier, as well as an identifier from an external login service if you log in via one. Also the time you accepted the terms of use and which versions of the terms and the privacy policy applied then. Used to create and secure your account, to identify you in the service and to show that the terms were accepted."`

Övrigt om strykningen:
- **Det är en ren strykning, utan ersättningsmening.** "Vi lagrar inget lösenord" vore ett nytt påstående, och det vore falskt om den ansvariges konton tills §3c har kört (Q7).
- **"Säkra ditt konto" håller fortfarande.** Varje inloggningskod, inloggningslänk och säkerhetsmeddelande går till adressen (:48 och inloggningsstycket i samma avsnitt), så adressen bär nu kontots säkerhet ensam. Sessionerna och re-auth-budgeten binds till kontoidentifieraren.
- **Satsen om extern inloggningstjänst är 6a:s** och ska stå orörd.
- **Kontrollmätning:** `grep -c -i "lösenord\|hash"` på sv-filen och `grep -c -i "password\|hash"` på en-filen ska ge 0 och 0 (i dag 1 och 1). Den som mergar sist av 5b och 6a kör samma grep.
- **Samma dag:** blir mergedagen 2026-09-25, alltså samma dag som dagens `privacy.updated`, skiljer versionen inte längre två texter åt. Det är ofarligt bara så länge ingen stämpel skrivs. `AcceptCurrent` har en enda anropare (`AccountRegistrar.cs:35`), och den ligger bakom `RegistrationsOpen=false`. Skriv i så fall in det i Amendment (11).

**Q7. Fönstret mellan text och data: icke-fynd.** Det finns två grunder, och den andra räcker ensam.
- **(1) Sidan har ingen läsare.** `basic_auth` står villkorslöst i sajtens enda `handle` (`Caddyfile:176,212`), och sessionen mätte 401 2026-09-24T05:15:55Z.
- **(2) Texten är falsk bara om rader som ingen skrivare har skapat sedan 5a B.**
  - Varje konto som någon annan kan skapa föds utan hash (`UserAccountService.cs:26-38`), och `NoPasswordSymbolTests` pinnar att ingen skrivare tillkommer.
  - Texten är alltså sann för alla konton utom den ansvariges egna, och där är han både bärare och författare.
  - Grunden gäller därför även den dag `basic_auth` tas bort, och den fyrar inte §9.6 (3)(iii): en blivande registrerad läser en sann text.
- **Det motsäger inte mitt M9(d) från 2026-09-17.** M9(d) flyttade strykningen till den del som nollar datan. Den här frågan gäller fönstret inuti den delen, och ordningen där är tvingad av förvillkor 2.
- Ingen Caddyfile-not och ingen #734-rad behövs. Fönstret slutar vid §3c:s återläsning, och Klas svar 3 lägger 5b före lansering.
- Villkor: grund (2) gäller bara om 5b:s diff inte inför någon skrivare av `PasswordHash`. `NoPasswordSymbolTests` med Infrastructure är mätningen.

Stycke till Amendment (11), ordagrant:
> **The copy precedes the data by one interval, and misinforms no one.** The struck line reaches the box with the images, and §3c applies 5b later, on its own GO; precondition 2 forbids the reverse order. In between, the policy does not name the hashes the box still stores. Every account anyone else can create is born without one, and `NoPasswordSymbolTests` pins that nothing writes one, so the new text is true of every account except the controller's own (security-auditor, 5b's form round).

**Q8. Registerdeltat. Skrivs EFTER lådkörningen, som ett enda delta.** Trailens recorder-rad är inaktuell under fönstret. Det är tolerabelt: det är ett internt art. 30-register, och den enda bäraren är den ansvarige.

Datafält :1424-1425. Ersätt `` `password_hash` (ASP.NET Identity-hashning, aldrig klartext)`` med:
> `password_hash` (**NULL på varje rad sedan ADR 0142 del 5b**: migrationen `⟨5b id⟩` satte kolumnen till NULL och roterade `security_stamp` och `concurrency_stamp` i samma sats, på de rader som hade ett värde. Applicerad på lådan ⟨UTC⟩ och återläst med `count(*)` till 0 rader med ett värde. Kolumnen ligger kvar i schemat och ingen kod skriver den. De gamla värdena ligger kvar i döda radversioner tills autovacuum tar tillbaka utrymmet, och WAL påstås inte raderad. Ingen kopia togs före körningen (ADR 0142 Amendment (11)). Om en Netcup-snapshot från tiden före körningen finns bär den hasharna; det är inte mätt, se `master-key-ops.md`)

Trail :1599-1600. Ersätt "första inkorgsbeviset bekräftar adressen och tar bort lösenordet i en Identity-skrivning och skriver `User.InboxProvenByLogin` i `audit_log` (#1735)" med:
> första inkorgsbeviset bekräftar adressen och roterar säkerhetsstämpeln i en Identity-skrivning, återkallar tidigare sessioner och skriver `User.InboxProvenByLogin` i `audit_log` (#1735; skrivningen tog också bort lösenordet fram till ADR 0142 del 5b)

Efter punkten som slutar med ":1603 … #1743)", infoga:
> · `password_hash` NULL på varje rad, med säkerhetsstämpeln roterad i samma sats (ADR 0142 del 5b, applicerad på lådan ⟨datum⟩)

Datakategori, Syfte och Rättslig grund ändras inte, eftersom ingen av dem nämner lösenordet efter 5a (mätt).

**Q9. DoD 8, ordagrant för Amendment (11):**
> **DoD 8.** No new personal data, processing, recipient or transfer, and no DPIA: nothing in Art. 35(3) changes. One stored item ends: every account's password verifier. The migration sets `password_hash` to NULL and rotates `security_stamp` and `concurrency_stamp` on the rows that held one; the column stays in the schema and nothing writes it (`NoPasswordSymbolTests`, Infrastructure now included). The old values stay in dead row versions until autovacuum reclaims them, and WAL is not claimed erased. No copy was made for the run, by the decision above; the offsite backup has never run (#197), so no backup of record holds a hash; whether a Netcup snapshot from before the run exists is not measured. The migration writes no `audit_log` row: §3c's run record, here and in the session log, is the trail of an operator's bulk change, and it outlives `audit_log`'s 90 days. The privacy line "lösenord (hash)" is struck in this PR, with `privacy.updated` and `CurrentPrivacyPolicyVersion` moved together to the merge day; the register's `password_hash` field follows the data, after the run.

**Q10. Lapse-trigger 6, ordagrant för Amendment (11).** Sessionen fyller bara ⟨…⟩, från sin egen avläsning samma dag som mitt verdikt. Avläsningen från 15:52:07Z ärvs inte.
> **Lapse trigger 6 fires in this part and is re-measured** (security-auditor, 5b's form round 2026-09-25; re-read against the final diff). The trigger reads "5b lands (no password fallback remains)". It names an event that happens once, so it is spent here and not re-armed; the acceptance lapses from now on triggers 1–5 and 7.
> - **The arithmetic is unchanged.** 5b touches neither code length, attempts per challenge, challenge TTL nor the mint budget, so 30 guesses per address per day, ≈ 0.003 % per day and 1.089 % per year under sustained attack stand as written above.
> - **"Mail is the only way in" did not start here.** It has held for every account since 5a PR B (`41a49394`) removed every route that accepts a password, measured live on ⟨UTC, gate 0(a)⟩, and it holds until an IdP goes live (trigger 4). What 5b changes is reversibility: once §3c's read-back shows no row holding a hash, reverting 5a restores no way in, because no account has a password to present.
> - **Evicting a session won by a guessed code** (security-auditor's #1743 form-round m-2). The owner can end every session through a confirmed address change, which invalidates all and re-issues one, or through account deletion; logout ends the current session only. A password holder could also do it by changing or resetting the password until 5a; that path went with 5a and 5b does not bring it back. Otherwise the owner contacts the controller, as the terms say. No self-service "log out everywhere" is proposed, and none is a #734 gate: Klas declined the session list on 2026-09-20. m-2 stays a Minor.
> - **Bearer reading, re-taken for this part:** ⟨UTC⟩: ⟨N⟩ accounts, all the controller's, by count; `Auth__RegistrationsOpen=false` in the running api container. Every consequence above is the controller's alone.
>
> **The acceptance continues for the product as it is today.** The other triggers, read for this part: 1: the compose default `${AUTH_REGISTRATIONS_OPEN:-false}` is unchanged. 2, 3: no account is added; the migration updates existing rows only. 4: no IdP in this diff; if 6a (#1744) merges first, security-auditor re-reads triggers 4 and 6 against the merged base before this part's verdict. 5: code length, attempts and the mint budget are unchanged. 7: the request path and its budget branch are untouched; the recorder change sits after proof. No other acceptance in this ADR lapses: 5b opens nothing and adds no account.

- **Andra triggers:** ingen annan fyrar (se stycket ovan).
- **Residualen :877-879 avskrivs vid återläsningen, inte vid mergen.** Rättelsen, ordagrant:
  > **Corrections above.** Amendment 2026-09-19's residual — *a squatter's password on an account its owner already confirmed survives a code login until 5b* — lost its harm when 5a PR B removed every route that accepts a password (measured live ⟨UTC, gate 0(a)⟩). It is discharged, together with its *Major at the flip* clause, at 5b's §3c read-back on the box, and not by this merge: until the run, the box still holds the hashes.
- **Två meningar i ADR:n blir inaktuella och följer med:**
  - Punkt 6 i "Attempt budget" får tillägget `*(Fired and re-measured in Amendment (11); spent.)*`.
  - Consequences "seven lapse triggers" blir "seven lapse triggers, one of them spent (6, Amendment (11))".

**Q11. Recordern: invarianten håller, omformulerad, och grenen står kvar.**
- **Invarianten Q21/Q-S3 har fyra delar:**
  - (1) Flaggan, lösenordets borttagning och stämpeln skrivs i en Identity-skrivning.
  - (2) En skrivning som inte persisterade kastar, och inget följer efter den.
  - (3) Tidigare sessioner återkallas innan den nya skapas.
  - (4) En audit-rad skrivs.
- **Efter 5b är lösenordsledet i (1) tomt.** Del (1) blir flaggan och stämpeln i en skrivning (M-2). Del (2)–(4) står oförändrade.
- **Kvar av squatter-hotet är en äldre obekräftad rad** med en session som öppnades med lösenord före 5a. Det är del (3) som bär det nu.
  - Grenen står därför kvar. Att ta bort den kräver att tillståndet deklareras onåbart (AGENTS.md §5 `Tests:`).
  - "Ingen levande skrivare skapar en obekräftad rad" gäller nya rader, inte äldre.
- **Stämpelrotationen dödar ingen levande token i dag.** Den behålls som paritet med migrationen och för den dag en stämpelbunden väg tillkommer.
- **Audit-raden står kvar.** Adressen bekräftas och sessioner återkallas, och det är en ändring av säkerhetstillståndet på ett känt user-id.
- **Fönstret mellan image och §3c är ofarligt.** En hash som överlever ett första bevis där läses inte av någon route, och migrationen nollar den.
- **ADR-meningen :873-877 blir:** *"For an account with `EmailConfirmed=false`, the first proof sets the flag and rotates the stamp in one Identity write (until 5b the same write also removed the password); a write that did not persist throws and nothing follows it; earlier sessions are revoked before the new one is created; and a `User.InboxProvenByLogin` `audit_log` row is written."*
- **Dokumentpåståenden som blir fel och ska rättas i samma PR** (en felaktig kommentar är en defekt, AGENTS.md §5):
  - `IInboxProofRecorder.cs:4-9` och `InboxProof.cs:9-12`.
  - Recorderns docblock `:7-13`.
  - `PasswordlessSessionGrant.cs:32` ("A credential changed") och `:46` ("the removed password").
- **Premissen för test 2 efter 5b (test-writer):**
  - Startläget är en obekräftad rad med hash, som den pensionerade `/auth/register` skapade.
  - Kör sedan migrationens `internal const` mot raden. Det är aktören som ger "obekräftad, ingen hash".
  - Skriv aldrig `password_hash = NULL` för hand.

**Till CTO (Q13): säkerhetens krav på skopredikatet, oavsett form.**
- **Hela Infrastructure skannas, utom två kredentialklasser som inte tillhör någon registrerad:**
  - Genererad modellmetadata (`…Identity.Migrations`, där 5b:s SQL ingår). En ny migration triggar både mig och db-migration-writer, så undantaget har en läsare.
  - Tjänstekredentialer (Redis `ConfigurationOptions` och design-time-fabrikerna), på samma grund som Migrate.
- **Undantaget binds till typ eller namnrymd, aldrig till fil:rad.**
- **En mutant som återinför `RemovePasswordAsync`, `CheckPasswordAsync` eller `PasswordHasher` i `Infrastructure.Auth` ska ge rött.**
- **Form:** (b) missar en framtida adapter i en annan namnrymd. Jag föredrar (a). Beslutet är CTO:s (F3).

### Major
1. **M-1. `concurrency_stamp` roteras i samma sats som nollningen, med ett CSPRNG-värde per rad.** Fil: 5b:s migration (ny), `UserAccountService.cs:156,175`, `IdentityInboxProofRecorder.cs:27`, `IdempotentAdminRoleSeeder.cs:140`
   Nuvarande: briefens Q1 lämnar `concurrency_stamp` öppen (fakta 6 ⚠).
   Krävs:
   - Satsen: `SET password_hash = NULL, security_stamp = <per rad>, concurrency_stamp = <per rad> WHERE password_hash IS NOT NULL`.
   - Båda värdena kommer från `gen_random_uuid()` (Postgres-kärnan, `pg_strong_random`) eller en likvärdig CSPRNG, utvärderad per rad. Aldrig en konstant, och aldrig härlett ur radens data.
   - Pinne (test-writer): ladda en användare med `UserManager`, kör konstanten och spara den laddade instansen. Förvänta `ConcurrencyFailure` och att `password_hash` fortfarande är NULL. Aktören är `UserManager`s egen laddning följd av sparning, och den går att anropa i testet (AGENTS.md §5 `Tests:`).
   Motivering:
   - `UserStore.UpdateAsync` skriver om hela raden, `password_hash` inräknad, under ett villkor på `concurrency_stamp` (fakta 6).
   - En skrivare ovan som laddade raden före migrationens commit och sparar efter den skriver tillbaka den gamla hashen. Postgres omvärderar villkoret mot den nya radversionen, så bara en roterad `concurrency_stamp` gör skrivningen till en vägrad `ConcurrencyFailure`, som recordern redan kastar på.
   - Utan det kan återläsningens `0` bli falsk i efterhand utan att någon märker det, och Q5:s signatur vilar på den avläsningen (art. 5(1)(f), 32(1)(b)).
   - Stämpeln är dessutom nyckeln för de tre registrerade TOTP-providerna. En förutsägbar stämpel vore en förutsägbar kod den dag något anropar dem.
   Delegera till: db-migration-writer (satsen), test-writer (pinnen).

2. **M-2. Recordern behåller exakt en persisterad Identity-skrivning, och grenen `FirstProofRecorded` står kvar.** Fil: `IdentityInboxProofRecorder.cs:23-39`
   Nuvarande: `RemovePasswordAsync` är metodens enda sparning (fakta 5, mätt). Raderas bara raden försvinner bekräftelsen och rotationen tyst.
   Krävs:
   - Flaggan och stämpeln sparas i en skrivning, till exempel `UpdateSecurityStampAsync(user)` efter `EmailConfirmed = true`. Formen är architects.
   - `Succeeded`-kontrollen och kastet står kvar.
   - Ingen invalidering, session eller audit-rad följer på en skrivning som inte blev av.
   Motivering: utan sparning svarar varje inloggning av en äldre obekräftad rad `FirstProofRecorded`. Då återkallas alla sessioner och en audit-rad skrivs vid varje inloggning, medan flaggan aldrig sätts. Det bryter invarianten som ADR 0142 binder (:873-877). Planen uppfyller villkoret redan. Det skrivs ändå ut, så att en omformning inte bryter det.
   Delegera till: dotnet-architect.

### Minor
1. **m-1. Värdena i `access_failed_count` och `lockout_end` är inte mätta sedan lockout-vägen revs.** Fil: registret :1426-1427, `identity."AspNetUsers"`
   Nuvarande: registret säger att kolumnerna saknar skrivare sedan 5a men ligger kvar. Om någon rad bär ett värde är inte mätt, och min 5a-delta 5 frågade inte efter det.
   Krävs: i avläsningen vid grind 0(a), kör `select count(*) from identity."AspNetUsers" where access_failed_count <> 0 or lockout_end is not null` (räkning, aldrig rader).
   - 0: avläsningen med datum förs in i registret, och fyndet håller inte.
   - Över 0: CTO routar, antingen till en egen idempotent sats i 5b:s migration eller till en issue. Nollningssatsen ändras inte.
   Motivering: ett räknat misslyckat inloggningsförsök knutet till kontot har inget ändamål sedan lockout revs (art. 5(1)(c), (e)). Bäraren är i dag den ansvarige.
   Delegera till: den drivande sessionen (mätningen), senior-cto-advisor (routing om värdet är över 0).

### Praise
- Fakta 5 mätte före kod att en ensam radering av raden tappar bekräftelsen tyst. ✓
- En enda idempotent sats (`WHERE password_hash IS NOT NULL`) och ingen dump. Satsen bär sin egen upprepningsläsning. ✓
- Briefen namnger fönstret mellan text och data i stället för att dölja det. ✓

### Sammanfattning
0 blockers, 2 major (formvillkor, där M-1 bär Q5:s signatur), 1 minor. #1743:s m-2 står kvar som Minor och är disponerad i trigger 6-texten. Omgranskning efter fix: samma agent, report-only, avgränsad till fix-deltat (CLAUDE.md §9.6).

**Mätningar före mitt panelverdikt.** De är villkor, inte fynd, och ingen av dem ärvs:
- 5a mätt live (grind 0(a)).
- 6d applicerad på lådan före merge (grind 0(b)), läst med `count(*)` i `identity."__EFMigrationsHistory"`.
- Bäraravläsningen samma dag: antal konton, ägarskap genom räkning och `Auth__RegistrationsOpen=false` i den körande api-containern. Den fyller ⟨…⟩ i Q10.
- Räkningen för m-1.
- Om 6a mergar först: triggers 4 och 6 läses om mot den mergade basen.

**Eskalering till Klas:** nej. §3c-körningen kräver hans GO per körning (förvillkor 1). Det är procedur, inte ett beslut jag lyfter.

Relevanta filer:
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\github-ci-audit-optimize-039bb7\docs\runbooks\vps-deploy-stack.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\github-ci-audit-optimize-039bb7\docs\decisions\0142-passwordless-auth-one-page-code-or-link-oauth-ready.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\github-ci-audit-optimize-039bb7\src\Jobbliggaren.Infrastructure\Auth\LoginChallenges\IdentityInboxProofRecorder.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\github-ci-audit-optimize-039bb7\src\Jobbliggaren.Application\Auth\LoginChallenges\PasswordlessSessionGrant.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\github-ci-audit-optimize-039bb7\src\Jobbliggaren.Infrastructure\Auth\UserAccountService.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\github-ci-audit-optimize-039bb7\src\Jobbliggaren.Migrate\Program.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\github-ci-audit-optimize-039bb7\web\jobbliggaren-web\messages\sv\content-legal.json`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\github-ci-audit-optimize-039bb7\web\jobbliggaren-web\messages\en\content-legal.json`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\github-ci-audit-optimize-039bb7\deploy\caddy\Caddyfile`
- `C:\DOTNET-UTB\JobbPilot\docs\runbooks\gdpr-processing-register.md`
