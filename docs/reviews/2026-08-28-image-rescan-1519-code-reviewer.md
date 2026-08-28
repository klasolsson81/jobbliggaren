# code-reviewer — PR #1545 (`fix/image-rescan-1519`), #1519

**Datum:** 2026-08-28
**HEAD vid granskning:** `5a08b927` (PR:ens head på GitHub var då `d5a43800` — senaste committen opushad)
**Status:** ⚠ Changes requested — **0 Blocker, 4 Major, 6 Minor**
**Auktoritet:** CLAUDE.md §1, §5 `Comments:`, §6, §7, §9.6, §12
**Skop:** CI/infra — bash + GitHub Actions + runbook. Områdena 1–3 (Clean Arch/DDD/CQRS) ej tillämpliga; inget under `src/` ändras.

> Transkriberad av den anropande sessionen; `code-reviewer` körde i rapport-läge och skriver inga
> filer. Alla mutationer kördes på kopior i scratchpad — repot orört.

## Major

**M1. Fixturen "an empty repository slug" faller i CI — `GITHUB_REPOSITORY` läcker in i SUT:en**
`resolve-published-digests.test.sh:323` (+ `run_sut`, 118–128) mot `resolve-published-digests.sh:81`

`run_sut` skickar `RESOLVE_DIGESTS_REPO=""` men rensar inte miljön. SUT:81 är
`slug="${RESOLVE_DIGESTS_REPO:-${GITHUB_REPOSITORY:-}}"` — **satt-men-tom faller tillbaka** på
`GITHUB_REPOSITORY`, som varje Actions-runner sätter.

Mätt 2026-08-28 i worktreet: `GITHUB_REPOSITORY=klasolsson81/jobbliggaren bash …test.sh` →
**`passed: 26   failed: 1`**, fallet får exit 0 och fem rader på stdout där 2 väntades. Sviten
avslutar non-zero → det blockerande `scripts`-jobbet blir rött.

*"Detta är exakt samma defektform som `5a08b927` just lagade ett fall bort — fixturen antar en
miljöegenskap som gäller på utvecklarmaskinen men inte på runnern."*

**M2. Registry-data interpoleras in i ett `run:`-block** — `rescan-images.yml:143-148`, särskilt `147`

`created` kommer ur image-configen som **registryt** svarar, och SUT:163 kontrollerar bara
icke-tom/≠`null` — till skillnad från `name` och digesten. `${{ }}` substitueras textuellt före
skalet. Mätt: detta är det **enda** värdet i repots workflows med ursprung utanför repot (övriga
`${{ }}` i run-block är `github.sha`, `matrix.name`, step outputs). Efter `docker/login-action`
ligger `GITHUB_TOKEN` i `~/.docker/config.json`.

Förutsättning namngiven: en angripare måste redan kunna pusha till repots GHCR-namespace. Graden
är Major för att fixen är tre rader och inkonsistensen är intern i PR:en.

**M3. Kommentaren säger "symlinks", koden skriver wrappers** — `…test.sh:226` mot `232-240`

Stycket som motsäger är just det som förklarar att symlinks **mättes** gå sönder under MSYS.
§5 `Comments:` — faktafel är en defekt.

**M4. "an empty field shifts every later field left" är falskt** — `…sh:164`; `…test.sh:293`

Raden byggs `${name}<TAB>${img}@${digest}<TAB>${created}` (mätt med `cat -A`: `^I`). `created` är
**sista** fältet och separatorn skrivs alltid, så ett tomt `created` ger `NF=3` och `["name","ref",""]`.
Ingenting förskjuts, och det finns inga senare fält att förskjuta. Refusalen är rätt, motiveringen
är fel.

## Minor

**m1. Två mutanter till överlever** (mätt 2026-08-28 på scratchpad-kopior, ankare verifierade):
(a) `…sh:75` `[ -f "$WORKFLOW" ] || refuse` struken → sviten **27/0**. Fallet "a missing workflow
file is refused" passerar på **awks egen fatala exit 2** (`awk: fatal: cannot open file`), inte på
vakten. (b) `…sh:163` `[ -n "$created" ] &&` struket → **27/0**; ingen stub avger ett enda fält.
Båda är klassen filens egen header dömer. (`[ -n "$name" ]` inne i loopen är en tredje, men
genuint onåbar-defensiv — `matched == declared > 0` garanterar icke-tomma namn.)

**m2. Levande mätt tal utan datum** — `rescan-images.yml:101`: *"Eighteen of the twenty scripts …
are committed 100644"*. Sant i dag (mätt: 20 `.sh`, 18 × 100644, 2 × 100755) och falskt vid nästa
skript. Argumentet behöver inte talet.

**m3.** `…sh:153` *"the suite stayed 24/0"* — formen är daterad proveniens, men talet räknar
filens egen utdata, som nu är en annan siffra.

**m4.** `build.yml:590-598` — ny kommentar på **svenska**. §1 säger nya kommentarer på engelska,
och närmaste granne (#1518) är engelsk. Proportionen är rätt och varje sakpåstående verifierat sant.

**m5.** *"the artefacts this box actually pulls"* och *"THE ARTEFACT THE BOX RUNS"* — sant bara
medan `IMAGE_TAG` är osatt; runbookens rollback (rad 244, 440) sätter den. Ej Major eftersom
påståendet är sant i dagens läge.

**m6. Två lösa formuleringar** — *"so it never races `release-images.yml`"* (det mätta är "startar
inte samma minut") och *"(it says so itself in `.github/dependabot.yml`)"* (den filen dokumenterar
att GitHub inte kör security updates för `docker` alls, inte floating-tag-poängen). Kontrollmätt:
`/web/jobbliggaren-web` och `/deploy/caddy` **ligger** i docker-entryts `directories` — "utanför
Dependabots skop" hade varit ett felaktigt fynd.

## Bra gjort

- Loopen matas av en here-doc, inte en pipe, så `refuse` inne i den avslutar skriptet på riktigt,
  och workflowet sätter `set -euo pipefail` före `| tee`. Repots två gånger mätta "ett rörs status
  läst som ett verktygs" finns **inte** här.
- Ackumulera-och-skriv-sist gör en kort scan-matris omöjlig, och `assert_stdout_empty` nålar det;
  0/1/2-splitten återanvänder `nocache-stage-guard.sh`s exakta konstanter och `refuse()`.
- Trivy-paritetspåståendet är mätt sant; uppercase-fixturen är 64 hex-tecken så den testar
  versalkänslighet och inte längd; runbook-punkten sitter hos sin granne utan att klyva något
  resonemang.

## Sifferdomen (fråga 4)

`24/0` och `Eighteen of the twenty` är **levande tal** → m2/m3. `measured 2026-08-28`
(advisory-events), `Docker 29.0.1 / buildx v0.29.1`, `Eight mutations / seven killed`,
`run 33167528101`, trivy-db var sjätte timme, `packages public today … not a contract` är
**daterad §1.6-proveniens** och ska stå kvar.

## Fråga 5

`timeout-minutes: 5` sitter på **jobbet** (`build.yml:339`), inte på steget — budgeten delas med
fem andra sviter, men den fallerade körningen tog 44 s, så marginalen räcker.

## Sammanfattning

4 Major (merge-blockerande, §6/§12 — in-block eller följd-PR, **aldrig** en issue) + 6 Minor.
m1 bör fixas in-block eftersom sviten *är* oraklet; m2–m6 är strykningar eller namngivna skips.
Efter merge behövs **ett `workflow_dispatch`** på `rescan-images` — ett nytt workflow går inte att
dispatcha innan det ligger på default-branchen, och det är den enda proven på jq-steget,
matris-fanouten och trivy-anropet (§8 punkt 4).
