# dotnet-architect — PR #1438 (#1349)

- **Agent:** `dotnet-architect` (§9.2, mandatory: >5 filer)
- **Datum:** 2026-08-22 · **Worktree:** `c:/tmp/jbl-orphan`
- **Runda 1 skope:** `git diff origin/main...HEAD`, HEAD `17ba6a4c`, bas `076851fd` (trepunkt, färsk bas)
- **Omkontroll skope:** fix-delta `d8179433`, rapport-only
- **Status runda 1:** Behöver åtgärdas — **0 Kritiskt, 2 Viktigt, 2 Nice-to-have**
- **Status omkontroll:** alla fyra stängda, 0 new-in-delta
- ⚠ Sveptäckning: `Grep`-verktyget hoppar över gitignorerat. Svepet täcker `src/` + `tests/` + spårade
  `docs/`; gitignorerade `docs/reviews/`, ADR 0005 och ADR 0071+ är **inte** mätta.

## Svar på de fem axlarna

| # | Axel | Verdikt |
|---|---|---|
| 1 | `AccountHardDeleter`s nya mening | **Arkitektoniskt sann.** Precisionsanmärkning → N-t-h 1 |
| 2 | Bounded context / ADR 0013 | **Bekräftas, och stärks.** `EmailTemplates` bar ett domänantagande den inte fick bära; diffen tar bort det |
| 3 | Clean Arch / §2.1 | **Orörd, mätt.** 0 csproj, 0 nya `using`, 0 paket |
| 4 | Producentuppräkningen | **Platserna kompletta, triggarna inte** → Viktigt 2 |
| 5 | §3.6 / EF-porten | **Orörd, mätt.** 0 rader query-yta i diffen |

**Axel 1, underlaget:** raderingen committar domänsidan i **en** transaktion och kör Identity-DELETE som
separat boundary efter den; registreringen committar Identity på egen hand och `JobSeeker` senare via
`UnitOfWorkBehavior`. Överlevaren är i **båda** riktningarna Identity-raden. Adresslösheten mätt mot
**aktuell** `AppDbContextModelSnapshot.cs`: 36 entiteter, **noll** `email`-kolumner.

**Axel 4, platsuppräkningen:** Identity-user skapas på **exakt en** plats; `JobSeeker.Register` har **exakt
en** anropare; `JobSeekers.Remove` finns på **exakt en** plats. `IdempotentAdminRoleSeeder` skapar ingen user.
Soft-delete rör inte Identity. **Ingen producentplats saknas.**

## Viktigt

**1. Diffens sex nya docstring-rader sköt varje rad under 28 exakt +6 och slog sönder fem levande radpekare
som var korrekta på main** — `OrphanedIdentityActivationTests.cs:27` och `:104`,
`RegisterCommandHandler.cs:132`, `ReauthenticationServiceTests.cs:274`, `LoginCommandHandlerTests.cs:298`.
Ytterligare två (`:302-309` i `OrphanedIdentityActivationTests.cs:191` och `LoginCommandHandlerTests.cs:306`)
var **redan fel** på main och vidgas. Mätt: `:74-78` innehåller nu den materialiserande queryn; `:302-309`
innehåller `ResumeFiles`-deletet, inte Steg 2 h. §5 `Comments:`.

**Åtgärd: strykning, inte uppdatering** — sju hem för ett kunskapsstycke är DRY-brott, och radintervallet är
den enda halvan som ruttnar. Fyra av sju citerar redan meningen ordagrant.

**2. Producent 1:s trigger är för smal** — `OrphanedIdentityActivationTests.cs:184-188` + klass-docstringen.
`UnitOfWorkBehavior.cs:15-17` anropar `SaveChangesAsync` **ovillkorligt** utan att läsa resultatet. Varje fel
där ger identisk forward orphan: `DbUpdateException`, connection-/timeout-fault, eller ett kast ur
`FieldEncryptionSaveChangesInterceptor`. Cancellation är den *minst* sannolika i drift. PR:en smalnar post 3
på kriteriet "ingen trigger den verkliga adaptern producerar" — **det kriteriet binder åt andra hållet också.**

## Nice-to-have

**1. "no address at all"** — kolumnmässigt sant, men en reverse orphan behåller `resume_versions.content_enc`
och `parsed_resumes.raw_text` **plus** DEK:en som låser upp dem. Bindets egen grund är att re-identifiering
är **osund**, inte omöjlig; den starka läsningen är svagare än grunden den ersätter.

**2. Literal- vs formbaserad vakt** — assertionerna är exakta strukna formuleringar. "har skapat ett nytt
konto" eller "logga in när du bekräftat adressen" passerar. Mutationsaxeln kan inte upptäcka underräckvidden:
den återställde *den exakta strukna meningen*. Vakt måste vara FORM-baserad, inte NAMN-baserad.

## Omkontroll (delta `d8179433`)

1. **STÄNGD.** Svepet på radpekarmönstret ger **exakt 1 träff**, den korrekta `:19-28`, ommätt: rad 19–28 är
   fortfarande "Atomicitet-modell"-blocket. Ersättningssymbolen är greppbar och entydig; förbudet
   `RegisterCommandHandler` refererar ligger inuti just det blocket. Ingen pekare pekar under rad 33.
2. **STÄNGD.** Grunden ommätt i källan; nya formuleringen är korrekt och strikt bredare, i båda hemmen.
3. **STÄNGD.** Nollsvep på alla tre strukna attributen.
4. **STÄNGD, ingen skip behövs.** Kollisionsfriheten mätt: den nya literalen ger 6 träffar, **alla i andra
   mallar**, noll i `EmailConfirmation`. Att vakten är literal står nu utskrivet.

**New-in-delta:** inga.

## Ograderat, utanför deltat

- `LoginCommandHandlerTests.cs:302` — ett **tredje** hem för producent 1, fortfarande "A cancelled request".
- Samma fil `:317` — påståendet att den kompenserande raderingen kastar sitt resultat är nu **falskt**
  (#1410 gjorde den rapporterande). Pre-existerande.
- `OrphanedIdentityActivationTests.cs:208` — pekaren till `ApplicationUser`-konstruktionen mätt **korrekt**.

## Eskaleringar

Inga. Inget fynd är en §12-klass, och inget berör GDPR-Blocker-marken.
