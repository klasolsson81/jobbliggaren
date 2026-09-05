# `sni-aliases` — search aliases over the SNI 2025 branch names

Generates `src/Jobbliggaren.Infrastructure/CompanyRegister/Reference/sni-aliases-2025.v1.json`,
the asset the bransch filter matches against alongside the SNI names themselves (#1115).

```bash
node tools/sni-aliases/generate.mjs            # use the local page cache
node tools/sni-aliases/generate.mjs --refetch  # re-fetch all 835 pages first
```

Node 18+, zero npm dependencies. **Off-build and manually run — never a CI gate.** It fetches a
live external site, which ADR 0043 Beslut B forbids in the build and which would be a flake source
besides; `tools/taxonomy-snapshot/audit-parity.mjs` carries the same warning. The committed JSON
asset is the source of truth for the build. The page cache (`.cache/`) is gitignored.

## Why a generator rather than a hand-committed file

`sni-2025.v1.json` beside it was hand-committed in a single commit and has never been regenerated.
That is the absence of a decision, not a precedent to copy — `senior-cto-advisor` Beslut 1
(2026-06-08, `docs/reviews/2026-06-08-sok-paritet-b1-cto.md`) already chose the generator shape for
exactly this class, and `tools/taxonomy-snapshot/README.md` codifies it.

The case is stronger here: this asset is an extract from 835 scraped HTML pages, so its diff is
hundreds of Swedish phrases that **cannot be reviewed by hand** — there is no way to check from the
diff that the parse was faithful. The generator is the review artefact.

## Where the terms come from

Two sources, and every row in the asset says which in its `source` field.

**`scb`** — SCB's own search vocabulary. Each 5-digit code's page at `https://snisok.scb.se/<code>`
carries a section *"Exempel på vad som ingår i denna SNI-kod"*: 20-45 activity phrases that SCB
publishes precisely so people can find the right code. Reproduced **verbatim**; normalisation is a
matching concern and never changes the stored string, so the row can show the user the phrase SCB
actually wrote.

**`authored`** — a small residue this repo wrote, for demand words SCB does not carry at all.
Enumerated with a per-term rationale in `authored-terms.json`, under five entry conditions enforced
partly by `generate.mjs` and partly in review (the `brand-groups.v1.json` discipline: a term is
there because a maintainer put it there, never by inference).

### Licence

SCB open data is **CC0** (policy since 2021-07-01; SCB recommends but does not require
"Källa: SCB"). The SNI-sök pages carry no separate licence notice of their own — verified
2026-09-05. This is the same basis on which `sni-2025.v1.json` already ships.

## Why only part of the register ships

The whole register is 16 080 phrases. Measured 2026-09-05 against the real catalogue:

| shape | gzip | gap words answered (of 30) |
|---|---|---|
| full phrase list | 162.1 kB | — |
| head-term index | 86.0 kB | 20 |
| token index | 88.0 kB | 24 |
| **demand-driven extract (shipped)** | **6.9 kB** | **24** |

The reference payload is inlined into the RSC Flight payload of `/foretag/sok` **and**
`/foretag/smarta-bevakningar`, so it is document weight. ADR 0045 Beslut 2 locks
`resource-summary:document:size` at **30 720 B** at `error` severity in `lighthouserc.json`, and the
existing 17 kB reference already spends over half of it. Any whole-register shape is ~5x that
budget, on two pages, permanently.

Measured on the shipped extract (prod build, authenticated `/foretag/sok`, fetch-cache cleared
between runs, control probe in both directions, 2026-09-05): the **document** goes from 61 192 B
gzip without the aliases to **66 648 B with them, +5 456 B**. The `/reference` payload itself goes
17 032 → 22 548 B gzip. Note both figures are already ~2x the 30 720 B budget before this asset
existed — that overrun is repo state, filed as
[#1672](https://github.com/klasolsson81/jobbliggaren/issues/1672), and `/foretag/sok` is auth-gated
so Lighthouse never audits it.

Two filters were tried and **measured worthless** before landing on the demand list: dropping terms
already contained in their own node's name retained 99.4%, and dropping terms contained in *any*
node name retained 99.4% too. SCB's phrases are too specific to collide with SNI's names.

### The limitation, stated plainly

`demand-terms.json` is a **dated sample, not a census**. It was authored by hand from 77 probed
words and has no user-query evidence behind it. Coverage is therefore a property of the demand list,
not of the register: adding a word is one line plus a regenerate, and that is the intended
maintenance path. **This does not close the vocabulary gap — it closes the part of it we can
demonstrate.**

## Why this is not a crosswalk

#560 bind 4 forbids an SNI↔SSYK crosswalk: company branches and job-ad occupation groups are
different taxonomies, and mapping them is a claim the data does not support. What keeps this asset
on the right side of that line is structural, not a promise:

- Every alias carries **only SNI codes**, checked to exist in `sni-2025.v1.json`.
- **No SSYK/JobTech concept id appears in the asset**, pinned by
  `CriterionReferenceAliasTests.RealAsset_CarriesNoOccupationIdentifier_SoItCannotBeACrosswalk`.
- Nothing derives or preselects. An alias widens what the filter *shows*; the user still picks the
  SNI node, which is what bind 4 actually guarantees. The `sni` URL axis is untouched.
- Entry condition 5 rejects **cross-industry occupations** outright. `systemutvecklare` is the
  worked example and it does **not** ship: systemutvecklare work at banks (64), retailers (47),
  municipalities (84) and hospitals (86), so `systemutvecklare → 62201` would narrow a company
  search wrongly. Zero rows beats a confidently wrong row.

**Dated measurement of the source, 2026-09-05:** `systemutvecklare` occurs in **0 of 835** SNI-sök
pages. SCB's register uses the activity form instead — 62201 carries `Agil systemutveckling`,
`Systemutveckling, data` and `Projektledning, systemutveckling, IT` verbatim. This is a measurement
of SCB, not a claim about our asset, and it is recorded here because it is the reason the activity
form was available to ship at all.

Separately: `appsettings.json`'s `SearchSynonyms:Occupations` maps the word `systemutvecklare` to
nine JobTech occupation concept-ids on the **/jobb** axis. That is a different axis in a different
bounded context, and neither map is a mapping of the other.

## Version history

| aliasVersion | date | notes |
|---|---|---|
| `2025.alias.v1` | 2026-09-05 | First extract. SNI `2025.v1`, demand `2026-09-05.v1`. 142 rows / 335 terms over 142 codes; 37.8 kB raw, 6.9 kB gzip. 9 of 37 demand terms had zero SCB coverage; 6 authored, 3 rejected with reasons in `authored-terms.json`. |

`sniVersion` in the asset is pinned equal to the SNI catalogue's own at host build
(`CriterionReferenceProvider`), so re-versioning SNI without regenerating here fails the host rather
than silently keeping a stale vocabulary.
