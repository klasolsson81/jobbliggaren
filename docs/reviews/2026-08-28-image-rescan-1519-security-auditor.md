# security-auditor — PR #1545 (`fix/image-rescan-1519`), #1519

**Datum:** 2026-08-28
**Skop:** `.github/workflows/rescan-images.yml` (ny), `.github/scripts/resolve-published-digests.sh` + `.test.sh` (nya), `.github/workflows/build.yml`, `docs/runbooks/vps-deploy-stack.md`
**Status:** ✓ Approved (0 Blocker, 0 Major, 2 Minor)
**Auktoritet:** CLAUDE.md §9.2 (obligatorisk: external integrations / supply chain), §9.6, AGENTS.md §5 `Security:`, §12; audit areas 2, 6, 7, 8.

> Transkriberad av den anropande sessionen. `security-auditor` skrev inte filen själv:
> hennes charter förbjuder verktyg med effekt på repot, och `docs/reviews/` ligger i repot.
> §9.2 lägger transkriberingen på den anropande sessionen.

## Blockers

Inga.

## Major

Inga.

## Minor

**1. `matrix.image.created` är ett ovaliderat registervärde som når ett `run:`-block via `${{ }}`-interpolation (GitHub Actions script injection)** — `.github/workflows/rescan-images.yml:147`; orsak i `resolve-published-digests.sh:150,163`

`created` plockas som `awk '{print $2}'` ur registersvaret, `"` strippas, och kontrolleras bara på icke-tomhet och `!= "null"`. Värdet interpoleras textuellt in i `echo "built:   ${{ matrix.image.created }}"`. Expression-expansionen sker **före** bash, så command substitution i värdet exekveras.

Mätt (probe i `mktemp -d`, 2026-08-28) — hela kedjan, inte en hypotes:

```
docker-stub returnerar: "sha256:1111..." "$(id)"
SUT exit=0, stderr tom
rad:   api\tghcr.io/...-api@sha256:1111...\t$(id)
jq:    {"name":"api","ref":"...","created":"$(id)"}
run:   echo "built:   $(id)"      <-- exekveras
```

Nyttolasten måste vara mellanslagsfri (`awk`-fältet trunkerar vid whitespace, radantalskontrollen dödar newline), men `$IFS`/pipe räcker. `name` och `ref` är **säkra by construction** — separat probe med `name: api$(id)` och `name: worker;whoami` gav `[api]` / `[worker]`: teckenklassen trunkerar i stället för att injicera, och digesten är ankrad.

Varför Minor och inte Major: den enda skrivaren till `ghcr.io/klasolsson81/jobbliggaren-*` är repots egen publiceringsväg plus principaler som redan har `packages: write` — de kan redan byta ut artefakten lådan deployar, vilket är strikt större skada än att köra ett kommando i ett read-only-jobb. Fyndet stänger en privilegiekorsning (registry-write → Actions-execute); det öppnar ingen ny exponering mot en obetrodd part. Men det tar udden av filens egen privilegie-argumentation (header rad 30–35).

**2. Namn-extraktorn trunkerar vid första tecknet utanför `[A-Za-z0-9_-]` — den enda omstavning räknekontrollen inte kan se** — `resolve-published-digests.sh:96-100`

`match($0, /name: [A-Za-z0-9_-]+/)`. Ett legalt GHCR-namn med punkt trunkeras tyst, och raden räknas ändå som matchad.

Mätt: matris med `- { name: api.v2, ... }` + fyra vanliga → **exit 0**, tom stderr, emitterad rad `name=[api] ref=...jobbliggaren-api@sha256:...`. `declared=5`, `matched=5`, dubblettkontrollen tiger. Resolvern skannar alltså en **annan** image än den `release-images.yml` publicerar, grönt.

Ej nåbart från dagens repo-tillstånd — de fem verkliga namnen är `api worker migrate web caddy`, alla `[a-z]+`, och sektion 6 läser den riktiga filen rent. Men det är exakt den vakuösa-täckning-klass filen är byggd för att stänga, i filens egen instrumentering.

## Praise

- Trepartsutfallet (0/1/2) håller under angrepp: **ingen** väg där en referens når skannern utan att vara välformad, och ingen väg där färre än fem images skannas utan att något blir rött.
- Parametrarna är byte-identiska mot **båda** befintliga skanningar, inklusive action-SHA:n `ed142fd0...`. Ingen `.trivyignore`, inga `TRIVY_*`, inga `scanners:`/`vuln-type:` någonstans i `.github/`.
- Behörighetsytan är genuint read-only, och `on:` är bara `schedule` + `workflow_dispatch` — ingen `pull_request_target`, vilket är det som håller fynd 1 på Minor.

## Svar på de sex ställda frågorna

1. **Behörighetsytan håller.** Ett enda `permissions:`-block (rad 63), inga job-nivå-block som kan vidga. Inget `push`/`tag`/`attest`/`upload`/`gh`/`curl`. `docker/login-action` med `GITHUB_TOKEN` ger inte mer än blocket — tokenens scope sätts av blocket, så login mot ghcr.io ger pull, inte push.
2. **Pariteten håller, byte-identiskt.** `diff` mot både `build.yml:1016-1022` och `release-images.yml:240-246` gav IDENTICAL på `uses`/`format`/`exit-code`/`severity`/`ignore-unfixed`. `ignore-unfixed: true` är ärvd repo-state som denna diff inte skapade och ska **inte** skärpas här — den är det som gör paritetsmeningen sann.
3. **Ingen suppressionsändring.** Noll träffar på `ignoreGhsas`, `auditConfig`, `audit-level`, `NuGetAudit`, `NU19xx`, `pnpm.overrides`, `ignoredBuiltDependencies`, `action-setup`, `trivyignore`, `continue-on-error`. Riktningen är exponerings-**minskande**. Area 8:s pnpm-probe är inte kopplad av diffen och kördes därför inte — uttryckligen sagt i stället för förtigat.
4. **Ingen tyst grön väg funnen.** `set -euo pipefail` gör `bash ... | tee` icke-maskerande; script-exit 2/1 fäller `resolve` och `scan` skippas; `[ length -gt 0 ]` fångar tom matris; `emitted == declared` och `matched == declared` fångar korta listor; tom `$names` refuseras; `jq -R` återescape:ar backslash; digesten är ankrad och skiftlägeskänslig. Enda resten är fynd 2.
5. **Injektionsytan bor i `created`, inte i `ref`/`name`** — se fynd 1.
6. **GDPR: noll.** Ingen PII, inget nytt biträde — GHCR är samma mottagare som `release-images.yml` redan använder, Trivy-databasen är en nedladdning. Art. 28/44 ej berörda.

## Observation utan grad

En `resolve`-refusal (exit 2) och en verklig röd skanning ger samma signal till samma läsare — skillnaden syns på körningssidan men inte i mejlrubriken. Designens kända enda läsare, redan protokollförd i filens header.

## Eskalering till Klas

Nej.

## Sammanfattning

0 Blocker, 0 Major, 2 Minor. Ingen §12-klass utlöst; PR:en rider normalflödet enligt §6.
