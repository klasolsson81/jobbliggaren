# `scb-tatorter` — Swedish tätorter for the CV place lexicon

Generates `src/Jobbliggaren.Infrastructure/Resumes/Parsing/scb-tatorter-2025.v1.json`. That asset holds the
localities (tätorter) the CV import recognises as places, alongside the 290 kommuner (#2343).

```bash
node tools/scb-tatorter/generate.mjs            # use the cached API responses
node tools/scb-tatorter/generate.mjs --refetch  # re-fetch both responses first
```

Node 18+, zero npm dependencies. **Off-build and run by hand, never a CI gate.** The script fetches
a live external API, which ADR 0043 Beslut B forbids in the build; `tools/sni-aliases/` carries the
same warning. The committed JSON asset is the build's source of truth. The response cache (`.cache/`)
is gitignored, and the fetch writes the date it ran to `.cache/fetched-at.txt`, which is the date the
asset carries.

## Source

SCB's statistical database, PxWeb API v1, table `MI0810A/LandarealTatortN` ("Folkmängd och landareal i
tätorter, per tätort. Vart femte år 1960 - 2025"), `Tid` 2025, content code `000003F7` (Folkmängd).
- **What ships.** The table also lists earlier years' tätorter. A tätort ships when SCB publishes a
  population for it in 2025.
- **Licence.** Statistics in SCB's statistical database are open data under CC0 1.0
  ([SCB, Öppna data, API](https://www.scb.se/om-scb/om-scb.se-och-anvandningsvillkor/oppna-data-api/),
  read 2026-11-27).
- **Names are reproduced verbatim.** They keep stray whitespace, "(preliminär beteckning)" and the
  multi-place forms ("Mölnlycke och Öjersjö"). Normalisation is a matching step in C# and never
  changes the stored string.

## What the generator refuses

It stops with an error (`FEL:`) when:
- a row belongs to another year;
- a tätort code is not 4 digits + `TB`/`TC` + 3 digits;
- a code's kommun prefix is missing from `scb-kommuner-2026.v1.json`;
- a name is empty;
- codes repeat;
- fewer than 1,900 tätorter come back. This is a parser-health floor: SCB published 2,009 for 2025,
  measured 2026-11-27.

It also refuses to rewrite a changed list under an unchanged `tatortVersion`.

## Known limits

- **Småorter** (`MI0810E`), **city districts** and **localities without a row of their own in 2025**
  are not shipped. Torslanda and Kållered are two examples, both absent from the 2025 table
  (measured 2026-11-27).
- **Short names that are also words** ("Ed", "Vi", "Bro") match only as a whole fragment.
