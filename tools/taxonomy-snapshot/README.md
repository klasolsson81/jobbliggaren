# taxonomy-snapshot-generator

Off-build, manuellt körd generator för `src/Jobbliggaren.Infrastructure/Taxonomy/taxonomy-snapshot.json`.

## Varför

Sök-ytans hierarkiska väljare (Län→Kommun, Yrkesområde→Yrkesgrupp) matas av en
**committad** taxonomi-snapshot — aldrig ett live-API på sök-vägen
([ADR 0043](../../docs/decisions/0043-taxonomy-acl-for-search-surface.md) Beslut A/B,
Anticorruption Layer). Snapshoten är referensdata under granskning som vilken annan
committad artefakt.

`taxonomy-snapshot.json` är **sanningskällan** i repot. Detta script är dess
reproducerbara, granskningsbara genereringsdokumentation — inte ett build- eller
runtime-beroende (hermetisk build, [ADR 0043](../../docs/decisions/0043-taxonomy-acl-for-search-surface.md) Beslut B;
senior-cto-advisor Beslut 1 = Variant C, `docs/reviews/2026-06-08-sok-paritet-b1-cto.md`).

## Vad det gör

Additivt: läser befintlig snapshot, hämtar kommun-noder (`municipality` → `broader` region)
och yrkesgrupps-noder (`ssyk-level-4` → `broader` occupation-field) från JobTech Taxonomy
GraphQL, nestar dem under matchande region / occupation-field, bumpar `taxonomyVersion`,
skriver tillbaka. Befintliga `regions` + `occupations` (occupation-name) rörs inte —
occupation-name bevaras som synonym-/recall-substrat
([ADR 0067](../../docs/decisions/0067-platsbanken-search-parity.md) Beslut 1).

- Kommun→län och yrkesgrupp→yrkesområde är båda **exakt 1:1** → ingen dedup behövs.
- Deterministisk sortering (`conceptId`, Ordinal) → ingen diff-brus oberoende av
  GraphQL-svarsordning.
- Fail-loud vid >1 parent eller parent som saknas i snapshoten.

Den delade GraphQL-fetchen (`gql`/`fetchChildren`/`byConceptId` + fail-loud-regeln
vid ≠1 parent) bor i **`lib.mjs`** och konsumeras av både `generate.mjs` och
`audit-parity.mjs` (DRY — en granskad fetch-väg, ingen divergerande kopia).

## Köra

```bash
node tools/taxonomy-snapshot/generate.mjs
```

Krav: Node 18+ (inbyggd `fetch`). Ingen npm-dependency.

## Efter körning

1. Granska diffen mot `taxonomy-snapshot.json` (ska vara additiv + version-bump).
2. Kör seeder-/snapshot-testerna:
   `dotnet test --project tests/Jobbliggaren.Application.UnitTests` (MapRows/LoadSnapshot)
   + `TaxonomyReadModelIntegrationTests` (seed mot Testcontainers).
3. Committa både snapshot och ev. script-ändring. Seedern (`TaxonomySnapshotSeeder`,
   idempotent + version-gated) re-seedar vid app-start eftersom `taxonomyVersion` bumpats.

## Versionshistorik

| Version | Datum | Ändring |
|---|---|---|
| 29 | 2026-05-17 | Initial — Län (region) + Yrkesområde→Yrke (occupation-name). |
| 30 | 2026-06-08 | + Kommun (municipality, ~290) + Yrkesgrupp (ssyk-level-4, ~400). ADR 0043-amendment / ADR 0067 Fas B1. |

## Occupation-name lineage (`generate-occupation-name-lineage.mjs`)

Writes `src/Jobbliggaren.Infrastructure/Taxonomy/occupation-name-lineage.json`: every JobTech
occupation-name, deprecated ones included, with its `replaced_by` edges and its occupation group
(`broader` ssyk-level-4). The /jobb occupation-name filter and its per-variant counts read it at host
start (#2199). About a quarter of active ads carry a deprecated occupation-name, and JobTech's own
filter folds such an ad into the name that replaced it, so ours must know the edges.

```bash
node tools/taxonomy-snapshot/generate-occupation-name-lineage.mjs
```

- **Versions:** it fetches each version in `VERSIONS` with `include_deprecated: true` and keeps the
  union of their edges. v31 dropped some of v30's edges and added others, so neither version alone
  carries them all. `currentIn` records the versions in which a name is not deprecated.
- **Fail-loud:** a GraphQL error, too few concepts, a current name without exactly one group, a group
  that changed between versions, an edge to an unknown or non-name concept, a self-edge, a cycle, or
  a multi-replacement terminal without a group or not current in the latest version.
- **Format:** one entry per line, sorted by `conceptId` (Ordinal), so a refresh diffs as one line per
  changed name.
- **Never** the frozen v30 crosswalk or the picker snapshot.

To refresh it, add the new version to `VERSIONS`, bump `FETCHED_AT`, run the script, review the
diagnostics it prints, and run the lineage tests (`OccupationNameLineage*` in
`tests/Jobbliggaren.Application.UnitTests`).

## Paritets-audit (`audit-parity.mjs`)

Granskningsbart bevis för **TD-100 item 3** ("Validering mot Platsbanken"): att
den committade snapshotens **yrkesgrupp-SET** (ssyk-level-4) är komplett &
aktuellt mot JobTech-taxonomin. Per [ADR 0067](../../docs/decisions/0067-platsbanken-search-parity.md)
Beslut 1 filtrerar Platsbanken yrke på **ssyk-level-4 ur samma JobTech-taxonomi**,
så list-paritet mot JobTech ÄR list-paritet mot Platsbanken.

```bash
node tools/taxonomy-snapshot/audit-parity.mjs
```

Re-pull:ar `ssyk-level-4 broader→occupation-field` (via samma `lib.mjs`-fetch som
`generate.mjs`), diffar id + label + parent mot snapshotens `occupationGroups`,
och id-set:et på yrkesområdes-nivå. Skriver `parity-audit-report.json`
(datumstämplad, committad artefakt). Exit 1 vid drift (fail-loud), 0 vid paritet.

**Samma hermetik-kontrakt som `generate.mjs`** (ADR 0043 Beslut B): off-build,
manuellt körd, INGEN CI-gate / runtime-extern-hop. En CI-gate som fetch:ar ett
live-API vore både ADR 0043-brott och flake-källa.

**Avgränsning:** auditen bevisar **list-paritet** (yrkesgrupp-mängden), INTE
**annons-träff-paritet** (ger val X här samma annonser som val X på Platsbankens
realtids-UI). Det timing-känsliga hit-count-stickprovet spåras separat (TD-100b).

**Senaste körning:** `2026-06-26` → **PARITY** (yrkesgrupper 400/400, yrkesområden
21/21, 0 saknade / 0 extra / 0 label-drift / 0 parent-drift). Se
`parity-audit-report.json`.
