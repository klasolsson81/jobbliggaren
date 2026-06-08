# taxonomy-snapshot-generator

Off-build, manuellt körd generator för `src/JobbPilot.Infrastructure/Taxonomy/taxonomy-snapshot.json`.

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

## Köra

```bash
node tools/taxonomy-snapshot/generate.mjs
```

Krav: Node 18+ (inbyggd `fetch`). Ingen npm-dependency.

## Efter körning

1. Granska diffen mot `taxonomy-snapshot.json` (ska vara additiv + version-bump).
2. Kör seeder-/snapshot-testerna:
   `dotnet test --project tests/JobbPilot.Application.UnitTests` (MapRows/LoadSnapshot)
   + `TaxonomyReadModelIntegrationTests` (seed mot Testcontainers).
3. Committa både snapshot och ev. script-ändring. Seedern (`TaxonomySnapshotSeeder`,
   idempotent + version-gated) re-seedar vid app-start eftersom `taxonomyVersion` bumpats.

## Versionshistorik

| Version | Datum | Ändring |
|---|---|---|
| 29 | 2026-05-17 | Initial — Län (region) + Yrkesområde→Yrke (occupation-name). |
| 30 | 2026-06-08 | + Kommun (municipality, ~290) + Yrkesgrupp (ssyk-level-4, ~400). ADR 0043-amendment / ADR 0067 Fas B1. |
