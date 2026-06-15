# Third-party notices

Jobbliggaren bundles the third-party components listed below. They are required
for the local, deterministic Swedish NLP tier (Fas 4 STEG 2 / F4-2, ADR 0074):
tokenisation, Snowball stemming, and Hunspell spell-checking. No AI/LLM is used
(ADR 0071). This file is the notice obligation referenced in BUILD §3.1 —
permissive licenses (MIT, BSD-3-Clause) are not notice-free, and the copyleft
licenses (MPL 1.1, LGPL) require their notices to accompany the deploy artefact.

## Copyleft separation (server-side, non-distributed)

Jobbliggaren runs server-side on a VPS (ADR 0050); the product is **not**
distributed as a binary and consumers interact only over HTTP. None of the
licenses below is AGPL (no network-use clause), so no copyleft attaches to the
product code. As additional margin, the two copyleft artefacts
(WeCantSpell.Hunspell and the sv_SE DSSO dictionary) are consumed as
**unmodified, separable** units: the Hunspell NuGet binary is unmodified and no
product code is placed into or derived from its licensed files (MPL 1.1
file-level condition), and the DSSO dictionary ships as a separate, unmodified
data file (Content, never an embedded resource — BUILD §3.1), so LGPL copyleft
does not extend to the application.

---

## Components

### libstemmer.net 2.2.3
- **License:** MIT (the .NET packaging/wrapper).
- **Copyright:** © Guoyu Wang.
- **Source:** https://github.com/guoyu-wang/libstemmer.net
- **Use:** Swedish Snowball stemmer (`Snowball.SwedishStemmer`), wired in
  `Jobbliggaren.Infrastructure.TextAnalysis.SnowballSwedishStemmer`.

### Snowball stemming algorithms (bundled inside libstemmer.net)
- **License:** BSD-3-Clause.
- **Copyright:** © Dr Martin Porter, Richard Boulton, and the Snowball
  contributors.
- **Source:** https://snowballstem.org/ · https://github.com/snowballstem/snowball
- **Use:** the Swedish stemming algorithm itself (generated C# inside
  libstemmer.net). The same algorithm family backs PostgreSQL
  `to_tsvector('swedish')`, against which our stemmer is consistency-tested.

### Swedish stopword list (`swedish.stop`)
- **License:** BSD-3-Clause (Snowball stopword data).
- **Copyright:** © the Snowball project.
- **Source:** https://snowballstem.org/algorithms/swedish/stop.txt
- **Use:** embedded resource
  `Jobbliggaren.Infrastructure.TextAnalysis.swedish.stop`, shipped
  byte-identical to PostgreSQL 18.3's built-in `swedish.stop` so the analyzer
  drops exactly the lexemes `to_tsvector('swedish')` drops (stopword parity).

### WeCantSpell.Hunspell 7.0.1
- **License:** tri-license **MPL 1.1 / GPL 2.0 / LGPL 2.1** (inherited from
  Hunspell). Jobbliggaren **elects MPL 1.1** (LGPL 2.1 as fallback) and **never
  GPL 2.0**. The published NuGet binary is used unmodified.
- **Copyright:** © Aaron Dandy (the .NET port) and the Hunspell authors.
- **Source:** https://github.com/aarondandy/WeCantSpell.Hunspell
- **License texts:** MPL 1.1 https://www.mozilla.org/MPL/1.1/ ·
  LGPL 2.1 https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html
- **Use:** Swedish spell-checking, wired in
  `Jobbliggaren.Infrastructure.TextAnalysis.HunspellSwedishSpellChecker`.

### sv_SE Hunspell dictionary — "Den stora svenska ordlistan" (DSSO)
- **License:** **LGPL-3.0.** License text: https://www.gnu.org/licenses/lgpl-3.0.html
- **Copyright:** © 2003–2019 Göran Andersson `<goran@init.se>`.
- **Source:** obtained (UTF-8, verbatim) via
  https://github.com/wooorm/dictionaries (`dictionaries/sv`), generated from the
  LibreOffice "Swedish Spelling Dictionary — Den stora svenska ordlistan"
  extension.
- **Use:** the `sv_SE.dic` / `sv_SE.aff` files shipped **unmodified** as a
  separate Content data file (BUILD §3.1). SHA-256 of the shipped files is pinned
  in `DssoDictionaryIntegrityTests` to enforce the unmodified constraint.

### JobTech Taxonomy (Arbetsmarknadstaxonomin) — labour-market reference data
- **License:** **EPL-2.0** (Arbetsförmedlingen open data — free for anyone to
  use). License text: https://www.eclipse.org/legal/epl-2.0/
- **Copyright:** © Arbetsförmedlingen (the Swedish Public Employment Service).
- **Source:** JobTech Taxonomy v1 GraphQL, https://taxonomy.api.jobtechdev.se ·
  https://jobtechdev.se/en/products/jobtech-taxonomy
- **Use:** committed, frozen reference-data snapshots derived from the public
  taxonomy and reprojected into our own JSON shape — `taxonomy-snapshot.json`
  (regions/occupations/ssyk-4, F2-P9/B1/B2) and `jobad-skill-taxonomy.v30.json`
  (skill/competence + ESCO skill concepts, F4-4). Generated off-build by the
  `tools/taxonomy-snapshot/` and `tools/jobad-skill-taxonomy/` scripts;
  version-pinned `v30`. EPL-2.0 is a weak (file-level) copyleft with no
  network-use clause; the product runs server-side and is **not distributed**
  (ADR 0050), and the snapshots are our own reprojection, not modified EPL source
  files — so no copyleft attaches to the application (same posture as the MPL/LGPL
  components above).
