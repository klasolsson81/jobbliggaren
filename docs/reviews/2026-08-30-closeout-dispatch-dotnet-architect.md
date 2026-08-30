# dotnet-architect — PR #1592 (`docs/closeout-dispatch-step`)

- **Datum:** 2026-08-30
- **Diff:** `CLAUDE.md` +2/−1 (1 fil), base `origin/main` = `ea769d32`, HEAD `996a540a`
- **Roll:** §9.2 obligatorisk spec-edit-agent (dotnet-architect + code-reviewer)
- **Läge:** report-only — inga fixar applicerade

## Arkitektur-analys

### Sammanfattning
Behöver åtgärdas — 0 kritiska, 1 viktigt, 3 nice-to-have. Själva editen är korrekt
placerad, korrekt §13-läst och vilar på en premiss jag mätt och funnit sann; den bör
merga. Det viktiga fyndet gäller att den avslutar en mätt mekanismbrist med ett
manuellt processteg utan att någon äger mekanismen.

### Svar på de fyra ställda frågorna

**1. Placering — korrekt, inget fynd.** §6.5-bulleten är redan formen *regel + pekare*:
uppräkningen namnger vad close-out **består av** (fyra verb, noll mekanik), och nästa
mening delegerar `mergeStateStatus`, `gh pr update-branch` och nu `gh workflow run` till
playbook §8.1. Det är precis §13:s delning, inte en dubblering av den.

Avgörande: en **ofullständig uppräkning i en normativ fil är inte neutral — den är en
felaktig regel**. Incidenten var att sessionerna gjorde exakt vad §6.5 sade och stannade
där (playbook §8.1: *"Every session had watched its own PR to merge and stopped there,
which is what this section told them to do."*). Att lägga steget enbart i runbooken hade
lämnat spec:en kvar med en tre-stegs close-out som motsäger runbooken genom utelämnande.

Driftrisken är avgränsad och mätt: `grep` över tracked `*.md`/`*.sh`/`*.yml` ger **två**
normativa hem för uppräkningen — `CLAUDE.md:125` och `docs/runbooks/parallel-sessions.md`
§8.1 — plus sessionsloggar (historiska, ej normativa) och reaper-hookens hygienrad (se
Nice-to-have 3). CLAUDE.md-sidan bär inget kommando, ingen flagga och ingen härledning,
så den kan bara drifta om **stegmängden** ändras, vilket ändå är en spec-ändring.

**2. Ingen `spec-rationale.md`-post — rätt utfall, men PR:ns skäl är bredare än §13 ger.**
§13 lyder: *"Rules land here; derivations, incident history and dated measurements land in
`docs/spec-rationale.md`."* Det är en **routningsregel för härledningstext**, inte ett
mandat att författa en. Denna PR lägger noll härledning i `CLAUDE.md` — fyra ord — så
§13:s skyldighet utlöses aldrig. Utfallet är alltså rätt. Se Nice-to-have 2 för
precedensrisken i hur det motiveras.

**3. Felläget — se Viktigt 1.** Kort: "tre missar i rad" mäter **regelns frånvaro**, inte
en otillräcklig regel; steget har inte prövats och misslyckats. Men mekanismen bakom
missen är mätt trasig och äger ingen.

**4. Inga brott mot Clean Architecture, ADR-precedens eller skriven §-regel.** Diffen rör
ingen kod och ingen lagergräns. `agents-md-budget-guard.sh` bindar `AGENTS.md` (root +
kombinerat), **inte** `CLAUDE.md`-storleken — editen når inte den grinden. Inget nytt
`## §`-heading → §-index-disjunktionskontrollen (guardens rad 40-41) rörs inte.
Uppräkningen matchar playbook §8.1:s fyra steg. §6.5:s babysitter-mening (*"It does not
close issues — the owning session does"*) förblir konsistent: babysittern dispatchar inte
heller.

### Fynd

**[Viktigt]** `CLAUDE.md:125-126` (mekanismen bakom regeln, ej diffen själv)
**Vad:** Editen gör den manuella dispatchen till normativ close-out, men den underliggande
reconcilern är mätt trasig och ingen äger den. `release-images.yml`:s header (rad 24-25)
säger *"no trigger is load-bearing — a missed schedule self-heals on the next one"*.
Playbook §8.1 säger motsatsen (*"or the merge does not reach the box"*) och lämnar frågan
omätt — den ger kommandot men inte svaret.

**Jag körde mätningen** (`gh run list --workflow=release-images.yml`, 2026-08-30):

- Incidenten, på SHA-nivå: sista schedule-körningen före missarna var
  `2026-08-30T00:20:27Z` på `3ef482ce`. #1579 mergade 01:34:31Z, #1583 03:17:07Z, #1584
  03:39:14Z. **Nästa schedule-körning: 06:40:27Z — ett glapp på 6h20m mot en timcron.**
  Det som faktiskt byggde alla tre var en manuell `workflow_dispatch` 05:35:25Z på
  `a47cf8ad`. Diagnosen i #1589 är alltså bekräftad, och den dispatch PR:n kodifierar är
  bevisligen den väg som lagade det.
- Leveransgraden: fönstret 2026-08-27T00:00Z → 08-30T06:40Z (~79 h) skulle ge ~79
  timkörningar. **Faktiskt utfall: 11 schedule-körningar, ~14 %.** Glapp: 10h16m, 10h41m,
  8h54m, **13h18m**, 7h29m, 6h52m, 5h25m, 4h02m, 3h14m, 2h36m, 6h20m. Kontrast: 08-25/26
  låg på 50-105 min, alltså nära cron. Bortfallet är färskt och förvärras.

**Varför:** I det mätta regimet är den manuella dispatchen inte en latensoptimering utan
den **primära leveransvägen**, och den ligger bakom en punkt i en mänsklig checklista utan
mekanism. Det är felformen `delete-merged-branches.yml`:s egen header fördömer i samma
subsystem — *"green, silent and inert — the worst failure shape available, since it reads
as working"*. CLAUDE.md §2.5 samt precedensen i båda workflow-headrarna säger att en
mekanism vars trigger inte överlever villkoret den finns för att laga inte är en mekanism.

**Föreslagen åtgärd:** Merga denna PR — den är korrekt och steget fungerar bevisligen.
Öppna en **följd-PR** (ej issue: §9.6 tillåter inte issue-route för Major; och det är ett
genuint separat change-reason — mekanism, inte spec-uppräkning) som ger mekanismen en
ägare. Två kandidater, och **valet mellan dem är senior-cto-advisors, inte mitt** (§9.2,
multi-approach):

- *(a) Detektor i befintligt hem.* `.claude/hooks/worktree-reaper.sh` har redan tre
  `gh`-drivna hygiendetektorer i samma idiom — (a) döda lokala branches, (b) `BEHIND`/
  `DIRTY`-PR:er, (c) stale `wip`-claims. En fjärde — `git rev-parse origin/main` jämförd
  mot `gh run list --workflow=release-images.yml --json headSha` — gör glappet synligt.
  Den ärver **inte** schedulens bortfall, eftersom den triggas på session start.
- *(b) Tätare cron.* Flera `cron`-rader per timme. Bygget är SHA-idempotent och en no-op
  är gratis på publika repon (headerns egen text), så kostnaden är noll. Men GitHub
  droppar schemalagda körningar repo-brett — detta minskar sannolikheten, det garanterar
  ingenting, och jag har inte mätt att det hjälper. Namnger osäkerheten hellre än döljer
  den.

Ingen av dem öppnar någon av de två redan avvisade designerna (push-trigger,
`repository_dispatch` från `arm`). En tredje väg — byta merge-identitet till PAT så att
`push: main` fyrar — har blast radius över hela automerge-subsystemet och hör inte hit.

---

**[Nice-to-have]** `.github/workflows/release-images.yml:24-25` (befintligt repo-tillstånd)
**Vad:** *"The build is SHA-idempotent, so no trigger is load-bearing — a missed schedule
self-heals on the next one."* Slutsatsen är motbevisad av mätningen ovan: "nästa" ligger
empiriskt 2h36m–13h18m bort, och 2026-08-30 var den manuella dispatchen load-bearing.
Premissen (SHA-idempotens) står; den härledda slutsatsen gör det inte.
**Varför:** §5 `Comments:` — *"A factually wrong comment ... is a defect and is fixed"*.
Inte STOPP-klass (§12:s carve-out), och inte skapad av denna diff.
**Föreslagen åtgärd:** Vik in i samma följd-PR som Viktigt 1, med datumsatt mätning.
Inte in-block: den ligger utanför diffen och skulle ge PR:n ett andra change-reason.

**[Nice-to-have]** PR-kroppens §13-argument (inte spec-texten)
**Vad:** Skälet *"härledningen finns redan i workflow-headern och playbook §8.1, så en
tredje kopia är regrowth"* når rätt utfall via fel väg. §13 förbjuder härledning **i
`CLAUDE.md`**; den villkorar inte en `spec-rationale.md`-post på frånvaron av kopior
någon annanstans i repot.
**Varför:** Som precedens skulle formuleringen licensiera att hoppa över
`spec-rationale.md` närhelst någon härledning finns var som helst i repot — bredare än
§13 ger, och i en PR vars hela ämne är spec-governance väger precedensen tungt.
**Föreslagen åtgärd:** Formulera om skälet till det §13 faktiskt säger: *editen lägger
noll härledningstext i `CLAUDE.md`, så §13:s routningsklausul utlöses inte.* Ren
PR-kroppsändring; ingen spec-text rörs. **Obs §9.2:** PR-kroppen skrivs två gånger, aldrig
fler — vik in i den enda tillåtna editen efter sista verdict, inte som en egen edit.

**[Nice-to-have]** `.claude/hooks/worktree-reaper.sh:317`
**Vad:** Hygienraden lyder *"verify, then 'gh issue close' + drop wip"* — en två-stegs
close-out, nu när den normativa är fyra steg.
**Varför:** Hooken är enligt playbook §8.1 uttryckligen *"a report, not a close-out"*, så
detta är ingen konkurrerande normativ uppräkning och driftrisken är låg. Men den är det
naturliga hemmet för detektorn i Viktigt 1 (a), och de två ändringarna hör ihop.
**Föreslagen åtgärd:** Ta i samma följd-PR, inte här.

### Verdict

**Merga.** 0 Kritiskt (Blocker). 1 Viktigt (Major) med disposal = **följd-PR**, ej issue
(§9.6: Major får aldrig issue-route), remedievalet routat till senior-cto-advisor. 3
Nice-to-have (Minor), samtliga utanför denna diff → samma följd-PR eller namngiven skip
per §9.6. Inget §12-blockerande: ingen §5-anti-pattern, ingen Clean-Architecture-gräns,
inget bibliotek utanför BUILD.md §3.1, ingen design-token.

### Referenser
- `CLAUDE.md` §13 — update process, regel/härledning-delningen
- `CLAUDE.md` §6.5 — close-out, babysitter-avgränsningen, hotspot-reglerna
- `CLAUDE.md` §9.6 — Blocker/Major → in-block eller följd-PR, aldrig issue; Filing discipline
- `CLAUDE.md` §9.2 — multi-approach → senior-cto-advisor; PR-kroppen skrivs två gånger
- `AGENTS.md` §2.5, §5 `Comments:`, §12 (Comments-carve-out)
- `.github/workflows/release-images.yml` rad 1-28 — de två avvisade triggarna
- `.github/workflows/delete-merged-branches.yml` rad 1-30 — precedens: reconciler, och
  "green, silent and inert" som felform
- `docs/runbooks/parallel-sessions.md` §8.1 (rad 400-451, #1589 / `8c776da8`)
- Mätning 2026-08-30, reproducerbar:
  `gh run list --workflow=release-images.yml --limit 60 --json event,createdAt,conclusion,headSha`
