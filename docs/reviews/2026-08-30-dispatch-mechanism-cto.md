# senior-cto-advisor — remedieval för dispatch-mekanismen (PR #1592, Viktigt 1)

- **Datum:** 2026-08-30
- **Trigger:** §9.2 multi-approach — `dotnet-architect` routade valet explicit
  (`docs/reviews/2026-08-30-closeout-dispatch-dotnet-architect.md`, Viktigt 1)
- **Läge:** read-only. Inga filer ändrade utöver denna rapport.
- **Mätningar gjorda om, ej ärvda:** `gh run list --workflow=release-images.yml`
  (2026-08-30), `deploy/systemd/jobbliggaren-reconcile.timer`,
  `.claude/hooks/worktree-reaper.sh` block (3), `release-images.yml` rad 1-60,
  `docs/runbooks/parallel-sessions.md` §8.1, `gh pr view 1592`.

## CTO-rekommendation

### Beslut

**(a) ensam — en fjärde detektor i `.claude/hooks/worktree-reaper.sh` block (3).**
**(b) avvisas som remedie. Inget tredje alternativ finns.**

### Motivering mot principer

- **Repots egen skrivna regel om reconcilers** (`release-images.yml:22-23`,
  `delete-merged-branches.yml:24-27`): *"the only mechanism whose trigger survives the
  condition it exists to repair."* (b):s trigger **är** det som mäts fallera — GitHubs
  schemaläggare. En mekanism som triggas av det trasiga är per definition ingen
  reconciler. (a):s trigger (SessionStart) är helt oberoende av GitHub-schemat.
- **Felformen, samma två headers:** *"green, silent and inert — the worst failure shape
  available, since it reads as working."* (b) gör bortfallet **mindre sannolikt men lika
  osynligt** — samma felform, lägre frekvens. Att sänka frekvensen på en tyst miss utan
  att göra den synlig gör den svårare att upptäcka, inte lättare. (a) angriper tystnaden.
- **Täckning av hela felmängden.** Checklistesteget #1592 kodifierar täcker inte merges
  utan ägande session (Dependabot via `dependabot-automerge.yml`, babysittern). (a)
  täcker dem deterministiskt vid nästa sessionsstart, oavsett vem som mergade. (b)
  täcker dem probabilistiskt, med en sannolikhet ingen har mätt.
- **REP/CCP/CRP (Martin 2017, kap. 13).** Block (3):s tre detektorer och §6.5:s
  close-out-uppräkning ändras av **samma skäl** — close-out-hygien som reaper-loopen
  strukturellt inte kan se. Det fjärde close-out-steget hör i samma block som de tre
  andra: samma `gh`-idiom, samma fail-safe-tystnad, samma rapport-utan-mutation-kontrakt,
  och samma utdataform finns redan levererad på rad 345.
- **SRP (Martin 2017, kap. 7).** (a) inför **ingen ny change-reason** i filen. Block (3)
  har redan sin, uttryckligen skriven på rad 275-287.

### Avvisade alternativ

**(b) Tätare cron.** Tre skäl, i fallande styrka:

1. *Trigger-argumentet ovan* — den ärver bortfallet den finns för att laga.
2. **Taket är mätt och sitter i nästa hopp.** `jobbliggaren-reconcile.timer` är
   `OnCalendar=*-*-* *:47:00` + `RandomizedDelaySec=180` — **en pull i timmen**. Mer än
   EN lyckad build per timme levererar noll extra till lådan. (b):s bästa möjliga utfall
   är alltså att återställa ~1 build/h, vilket är exakt vad den enda cron-raden redan
   ber om och inte får. (b) betalar för en kadens vars tak redan är satt av en timer som
   fungerar.
3. **Oberoende-antagandet är ostött, och konkurrensgruppen talar emot det.**
   `concurrency: group: release-images` med `cancel-in-progress: false` mot en
   fem-cells matris med `timeout-minutes: 30` per cell: fyra cron-rader i timmen kan
   köa och tränga ut varandra. "4x cron = 4x chans" förutsätter oberoende bortfall som
   ingen mätt. Dessutom är bortfallet **icke-stationärt**: 08-25/26 ≈ full leverans,
   08-27→ ≈ 14 %, med oförändrad cron-rad. Att multiplicera en oförklarad, rörlig
   felfrekvens med fyra ger en oförklarad, rörlig felfrekvens.

**Push-trigger** och **`repository_dispatch` från `arm`** — avvisade på mätning i
`release-images.yml:5-19`. Jag öppnar dem inte.

**`workflow_run`-kedja på `ci`** (övervägd av mig, ej av arkitekten) — utesluten:
`push: main` fyrar inte alls här (app-merge, headern rad 6-8), så det finns ingen
main-körning att kedja från. Ingen tredje väg existerar.

**PAT som merge-identitet** — instämmer med arkitekten om blast radius, och den är
dessutom en säkerhetsyta (§9.2 `security-auditor`-trigger), inte en mekanismfix.

### Trade-offs accepterade

- **Detektionslatens blir "nästa sessionsstart", inte "nästa timme".** Accepterat: felets
  kostnad realiseras när någon öppnar `dev.jobbliggaren.se`, och sessionsstart är det
  ögonblicket. Vi köper determinism mot latens — rätt riktning för en tyst miss.
- **Hooken får en fjärde detektor i en fil vars toppkommentar säger "Two jobs".** Den
  säger det redan felaktigt sedan #900 (block (3) är jobb tre); en fjärde detektor ökar
  inte antalet change-reasons. **Gränsen jag sätter (OCP, Martin 2017 kap. 8):** en femte
  detektor som **inte** rör close-out bryter ut block (3) till en egen hook. Detta är
  inte den.
- **Vi lagar inte GitHub-schemat.** Avsiktligt: vi äger det inte och har ingen mätning
  som säger att vi kan påverka det.

### In-block-fixar (på #1592, före `agents-done`)

1. **Nytt i deltat, skapat av `4bb31934` och sett av ingen granskare:**
   `docs/runbooks/parallel-sessions.md:427` citerar **ordagrant** *"the owning session
   dispatches manually when it wants the image now"* — en mening samma branch just strök
   ur `release-images.yml:27`. Tracked fil, felaktigt citat.
   **Jag graderar den inte** — severity tillhör rapporterande agent (§9.6). Den går till
   `code-reviewer` i den scopade omkontrollen. **Fixformen är strykning, aldrig
   omformulering:** korta citatet till *"the owning session dispatches manually"*, eller
   ersätt citatet med pekaren till headern.
2. **Head har flyttat två gånger sedan båda verdicts** (`996a540a` → `4bb31934`). Båda
   panelerna vänts in mot `4bb31934` innan `agents-done` (§6). Mätt: PR #1592 är
   `BLOCKED` + `automerge` **utan** `agents-done` — det är #836:s armed-but-gated-by-design,
   inte en fastnad PR. Ingen `update-branch` är svaret här.
3. Nice-to-have 2 (PR-kroppens §13-skäl) och code-reviewer Minor 2 (radhänvisningen
   `:405-409` → `:405-409, :416`) in i #1592:s **enda** tillåtna kroppsedit (§9.2).

### Följd-PR

**En PR, ett change-reason:** *close-out-steget får en detektor, och mekanismens egna
kommentarer slutar beskriva den fel.*

- **Fjärde detektorn** i block (3). **Bindande implementationskrav:** main-spetsen läses
  från **fjärren** (`git ls-remote origin main`, eller
  `gh api repos/{owner}/{repo}/commits/main --jq .sha`) — **aldrig lokal `origin/main`**.
  En worktrees `origin/main` är bara så färsk som senaste fetch, och en stale ref
  rapporterar "i synk" när den inte är det: green, silent, inert — exakt felformen
  detektorn finns för att avskaffa.
- Jämför mot senaste **`success`**-körningens `headSha`; en pågående körning räknas som
  ej levererad. Fail-safe-tyst utan `gh`/nät/auth, som de tre andra (rad 288-290).
- Utdataformen finns redan levererad på rad 345 — återanvänd den med
  `gh workflow run release-images.yml`.
- **Nice-to-have 1** (`release-images.yml:24-25`): slutsatsen *"no trigger is
  load-bearing — a missed schedule self-heals on the next one"* är motbevisad. Premissen
  (SHA-idempotens) står. **Stryk slutsatsledet** — skriv inte in ett nytt mätt tal i en
  tracked fil (§5 `Comments:`: ett levande mätt tal ruttnar; publicera kommandot, som
  §8.1 redan gör).
- **Nice-to-have 3** (`worktree-reaper.sh:317`): tvåstegs → fyrastegs close-out-text.

### Nu eller senare — dom

**I den här sessionen, som tredje PR, efter axel-PR:en.**

1. §9.6:s förbud mot att en Major blir en backlog-rad **genom underlåtenhet** biter på
   *att den inte byggs*, inte på *när*. Att lämna den till en framtida session gör
   åtagandet beroende av att startprompten bär det — och sessionen som just gjort
   mätningen är den billigaste byggaren.
2. **Serialisera.** #1592 och axel-PR:en är båda redan dömda och närmare mål. Tre
   parallella paneler från en session är precis den form där "panelen körde" påstås utan
   att ha körts.
3. **Ingen kollision:** följd-PR:en rör `.claude/hooks/` + `.github/workflows/`;
   axel-PR:en rör `/jobb`-FE. Ingen hotspot, ingen migration.

**Om sessionen tar slut först** hålls åtagandet vid liv av två saker, och inget av dem är
ett issue: (i) **denna rapport och arkitektens är båda gitignorerade** (`.gitignore:160`)
— de måste `git add -f`:as in i följd-PR:en, annars är de osynliga för nästa session och
för GitHub (§9.2:s `.gitignore`-undantag); (ii) en explicit uppgiftsrad i nästa
startprompt. **Inte** ett issue (§9.6 tillåter ingen issue-route för en Major). **Inte**
enbart en mening i en PR-kropp — den har ingen läsare.

### Issues att fila

**Inga — netto 0 mot filningstaket.** Majoren får aldrig issue-route (§9.6); alla tre
Minors viks in i följd-PR:en eller i #1592:s kroppsedit och lämnas alltså inte ofixade,
så ingen namngiven skip behövs.

### Om Klas överridar mot (b) — vad som då måste mätas

`dotnet-architect` mätte inte att (b) hjälper, och det går inte att veta utan detta:

- **Baslinje, minst 7 dygn före ändringen:**
  `gh run list --workflow=release-images.yml --limit 200 --json event,createdAt,conclusion`
  → levererade schedule-körningar mot förväntat antal, samt glappfördelningen.
- **Efter, samma fönsterlängd, samma kommando.** **Utfallsmåttet är MAX-GLAPPET, inte
  antalet körningar** — maxglappet är det som orsakade missen. Fyrdubblat antal körningar
  med oförändrat maxglapp betyder att bortfallen är korrelerade och att (b) inte gjorde
  någonting.
- **Kontrollvariabel:** utträngning i `concurrency`-gruppen — räkna `cancelled` och
  köade körningar som aldrig startade. Ökar de, äter (b) sig själv.
- **Konfundern som gör mätningen svag:** bortfallet ändrades 08-26 → 08-27 **utan att vi
  rörde något**. Ett bättre efter-fönster kan vara GitHub som återhämtade sig. Utan ett
  parallellt kontroll-workflow med oförändrad cron är efter-mätningen inte attribuerbar.
  Det är i sig ett argument för (a) först: **(a) mäter utfallet** (main ≠ byggd main)
  i stället för proxyn (schemaleverans), och utfallet är det spec:en faktiskt lovar.

### Referenser

- Robert C. Martin, *Clean Architecture* (2017) — kap. 7 (SRP), kap. 8 (OCP),
  kap. 13 (Component Cohesion: REP/CCP/CRP)
- `CLAUDE.md` §9.6 — Blocker/Major → in-block eller följd-PR, aldrig issue; filningstaket;
  scopad omkontroll hos utfärdande agent; strykning som fixform
- `CLAUDE.md` §9.2 — multi-approach → senior-cto-advisor; PR-kroppen skrivs två gånger
- `CLAUDE.md` §6.5 / `AGENTS.md` §6 — close-out; `agents-done` faller på innehållsbärande push
- `AGENTS.md` §2.5 (mekanism utan mätning), §5 `Comments:` (levande mätt tal i tracked fil)
- `.github/workflows/release-images.yml` rad 5-19 (två avvisade triggar), 22-25
  (reconciler-regeln + den motbevisade slutsatsen), 34-36 (cron), `concurrency`
- `.github/workflows/delete-merged-branches.yml` rad 24-27 — "green, silent and inert"
- `deploy/systemd/jobbliggaren-reconcile.timer` — `*:47:00`, `RandomizedDelaySec=180`
- `.claude/hooks/worktree-reaper.sh` rad 271-318 (block 3), rad 345 (utdataidiomet)
- `docs/runbooks/parallel-sessions.md` §8.1 rad 416-431
- Mätning 2026-08-30, reproducerbar:
  `gh run list --workflow=release-images.yml --limit 40 --json event,createdAt,conclusion,headSha`
