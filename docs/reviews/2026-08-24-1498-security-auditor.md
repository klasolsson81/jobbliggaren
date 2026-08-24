# 2026-08-24 — PR #1498 — security-auditor

**Transcribed by the invoking session (CLAUDE.md §9.2).** `security-auditor`'s charter forbids
`Write`/`Edit` against the repo, so she returned the report and the session transcribed it. Capped
per her charter's Output format (max 25 lines per finding, max 3 under Praise; the
`Eskalering till Klas` line is exempt and is reproduced unabridged).

**HEAD reviewed:** `280b04ea` (base merge on `c160e511` + `5ebacc19`).

---

## Security-audit: CV-datumparsning, år-först slash-notation (PR #1498)

**Status:** ✓ Approved
**Auktoritet:** GDPR Art. 5(1)(c)(d), 12(1), 15, 16, 17, 22 · CLAUDE.md §5 (`CV & matching
engines`, `Security:`), §9.2, §9.6 · AGENTS.md §5 · ADR 0071, 0074 Invariant 1, 0090, 0136

### Blockers

Inga.

### Major

Inga.

### Minor

1. **`[GeneratedRegex]` utan `matchTimeout` på en backtracking-regex som körs över
   användarlevererad CV-text** — Fil: `src/Jobbliggaren.Infrastructure/Resumes/Parsing/DatePatterns.cs:190-193`
   (samma sak på `:115-118` och `:207-208`)
   Nuvarande: `DateRowRange()` deklareras med `RegexOptions.CultureInvariant | IgnoreCase` och
   inget `matchTimeout`. .NET:s källgenererade regex backtrackar, och grammatiken läses nu av tre
   konsumenter (`StripTrailingDate`, `StripDates`, `IsUnreadableDateRow`) över text som kommer ur
   en uppladdad fil.
   Krävs: en `matchTimeout` som övre gräns, alternativt ett skrivet beslut att avstå.
   Defense-in-depth, inte en åtgärd mot en mätt risk.
   Motivering: risken är mätt **icke-aktuell**. Komplexitetsprobe över `digits`/`slashpairs`/
   `dashes`/`nearmiss` vid n = 200→3200 och en 226 kB realistisk CV visar linjär skalning, och den
   nya grammatiken är inte långsammare än den gamla (226 kB: old 33,9 ms, new 26,3 ms). Ingen
   kapslad kvantifierare över en kvantifierad alternering finns. Fyndet är en gräns som saknas,
   inte en blowup som finns.
   **Pre-existerande och repo-brett — PR:en orsakade det inte**, den breddar ytan med en punktform
   och flyttar `StripDates` till den. Severity är hennes (§9.6); routingen är sessionens/CTO:ns.
   Delegera till: `dotnet-architect` om den ska åtgärdas.

### Ogradade observationer (utanför hennes skala — mätta, inte graderade)

- **Den breddade masken döljer nya mätbara siffror, men klassen är pre-existerande.** `StripDates`
  maskerar nu `2020/01 – 5000` helt, så `ContainsMeasurableDigit` faller `True`→`False` på 4 av 6
  konstruerade punkter. Axelkontroll: `2020 – 5000`, `2019 – 1800`, `2020-06 – 5000`,
  `01/2020 – 5000` och `maj 2020 – 5000` maskerades **redan på `origin/main`** (alla fem
  `old=False`). Widening:en gör den sjätte notationen konsistent med de fem andra; ingen ny
  defektklass. Tre av fyra flippar är själva fixen (`Levererade 2020/01 – 2024/12` var `True`
  före — det var #487-defekten).
- **ADR 0136 (publik) pekar på `docs/reviews/2026-08-03-1060-d3-widening-cto-round5.md`, som inte
  är spårad** (0 träffar i `git ls-tree -r HEAD`). En publik läsare får en död pekare — exakt den
  defektform ADR:n själv namnger om sina egna tidigare hem. Dokumentation, inte säkerhet.

### Praise

- `IsUnreadableDateRow`s tredje konjunkt (`!DateRange().IsMatch`) håller VÄRDE-grammatiken orörd —
  radgrammatiken producerar aldrig ett lagrat värde, så vetot kan inte bli en läckväg in i
  promoted CV ✓
- Fixtures använder `anna@example.com`, en RFC 2606-reserverad domän ✓
- Riktningen är dataminimering: mindre härledd persondata lagras, tre mätta felaktiga värden
  ersätts av ärlig frånvaro (Art. 5(1)(c)/(d)) ✓

### De fyra frågorna, besvarade med mätning

1. **Minskningen skapar inget GDPR-problem åt andra hållet.** Art. 15: ingen underrapportering —
   `ParsedExperienceDto` bär `RawText` (icke-nullbar, projicerad verbatim i
   `GetParsedResumeMapper.cs:55/58`); bara det **härledda** fältet är tomt. Art. 16: förbättras —
   `ExperienceDto` bär `StartDate`/`EndDate`/`RawPeriod`, alla skrivbara i CV-editorn, och ett tomt
   fält är trivialt att rätta medan det gamla beteendet lagrade ett **felaktigt** värde lyft ur en
   prosapunkt. Art. 17/retention: oförändrat (`Period` var redan `string?`, ingen migration).
   Art. 22/ADR 0090: mindre automatiserad slutledning, inte mer. Enda noteringen: på
   auto-promote-vägen blir `RawPeriod` null för notationen — Klas bekräftade produktbeslut,
   nedskrivet i ADR 0136.
2. **Personnummer-vakten orörd, och blind kan den inte bli — tre oberoende mätningar.** Diffen rör
   noll filer under `Domain/Privacy/` eller `Application/Resumes/Common/`. Vakten ligger
   **uppströms** varje `DatePatterns`-konsument (`ImportResumeCommandHandler.cs:82`,
   `ResumeContentPersonnummerGuard.cs:47`, `AutoPromoteGate.cs:132` — ingen läser `DatePatterns`-
   output; `StripDates` har exakt en konsument, `ReviewText.cs:273`, som returnerar en `bool`).
   Batteri på 19 pnr-former (inkl. samordningsnummer, OCR-glapp, en/em-dash): `DateRange` **0**
   träffar, `DateRowRange` **0** träffar. Vetot fyrar på **0** pnr-bärande rader.
3. **Loggning ren.** Noll `ILogger`/`Console.`/`WriteLine` i de fyra produktionsfilerna, före och
   efter. Noll log-anrop bland diffens tillagda rader. Svep över tillagda rader: 0 personnummer,
   0 telefonnummer, 0 nycklar/tokens, 0 `password=`-former, 0 infra-referenser — varje mönster
   verifierat mot positiv kontroll (ett mönster, `phone`, var **dött** mot sin kontroll i första
   körningen och lagades innan nollan lästes).
4. **Publiceringskontroll godkänd, med hennes eget mönsterset** — 15 axlar, positiv kontroll per
   axel. ADR 0136 rent (enda träffarna är issue-länkar och ADR 0071-referenser). CTO-binden nämner
   `dev.jobbliggaren.se` (redan i **13** spårade filer på `origin/main`) och två lokala
   Windows-sökvägar (**10** spårade filer under `docs/` bär redan `C:/`-sökvägar) — prejudikat åt
   båda hållen, höjs inte till fynd, men är ett medvetet val. Sessionens eget set var **inte**
   under-räckande på det det testade; axeln det saknade ("pekare in i undanhållet material") fyrade
   bara på ofarliga ADR 0071-referenser.

### Om själva anropet

**Rätt anropad.** Ingen namngiven trigger fyrade (mätt: noll filer under `Endpoints/`,
`Migrations/`, `Configurations/`, `appsettings*`, `.env`, `package.json`, `pnpm-lock`, `*.yml`),
men audit areas matchas mot **diffen**, inte mot trigger-listan, och diffen ändrar vad som
persisteras ur PII-bärande CV-text — area 1 rakt av. Anropet gav dessutom ReDoS-ytan, som ingen
annan panelagent hade letat efter.

⚠ **Area 8 kördes INTE, och det är ingen ren flagga.** Suppression-ytan är orörd, så området
matchar inte diffen. `audit-suppression-guard.sh` är en **icke-tagen** mätning i den här
granskningen, inte ett rent resultat.

### Sammanfattning

0 Blockers, 0 Major, 1 Minor (pre-existerande, ej orsakad av diffen), 2 ogradade observationer.
§12:s säkerhetsklausul gäller inte: ändringen har tester och en `security-auditor`-APPROVE mot
0 Blocker / 0 Major, så den rider normalt automerge-flöde (§6). Re-review efter fix: samma agent,
report-only, scopad till fix-deltat (CLAUDE.md §9.6) — men inget fynd här kräver en fix för att
merga.

**Eskalering till Klas:** nej.

---

**Reproducera:** probe-skripten ligger i sessionens scratchpad — `probe.py` (grammatik-rekonstruktion
+ personnummer-batteri), `redos.py` (komplexitet), `overmask.py` + `overmask2.py` (mask-delta och
dess axelkontroll), `pubcheck.sh` + `scan3.sh` (publicerings- och PII-svep). Varje skript bär sin
egen positiva kontroll.
