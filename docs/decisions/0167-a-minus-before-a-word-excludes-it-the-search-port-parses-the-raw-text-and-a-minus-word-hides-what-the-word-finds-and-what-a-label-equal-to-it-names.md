# ADR 0167 — A minus before a word excludes it: the search port parses the raw text, and a minus-word hides what the word finds and what a label equal to it names

**Date:** 2026-10-11
**Status:** Accepted
**Decider:** Klas Olsson for the two product choices, asked with `AskUserQuestion` on #2143, 2026-10-11. What a minus-word hides: "Text +
etiketter (Rek.)". Whether the help line says so: "Ja, en kort mening (Rek.)". The driving session decided D1–D10 under
`dotnet-architect`'s form round (`docs/reviews/2026-10-11-2143-form-dotnet-architect.md`, whose Viktigt 1–3 and Nice-to-have 1–6 are carried
here). There is no `senior-cto-advisor` ruling.
**Amends:** ADR 0062, by its Amendment 2026-10-11 (the rows the shared filter matches, the order of the q arms, the hand-written mirrors). ADR
0067, by its Amendment 2026-10-11 (Beslut 5's "never a hard exclusion", the D2 note's two sentences about where q is parsed, the E2h dash
strip). ADR 0043, by its Amendment 2026-10-11 (Beslut C's last point, "the ACL lives outside the query path").
**Related:** [#2143](https://github.com/klasolsson81/jobbliggaren/issues/2143) (the issue this ADR ships under) · ADR 0039 Beslut 1 (the filter
SPOT) · ADR 0045 (performance budgets) · ADR 0060 (recent-search capture) · ADR 0062 and its Amendments 2026-06-13 (TD-94) and 2026-08-30 ·
ADR 0067 Beslut 5, 6 and the E2h note · ADR 0076 Decision 5 (the per-user path) · ADR 0115 (the new-results count) · AGENTS.md §3.6, §5
(Tests, Comments)
**Measured against:** the #2143 worktree (branch `claude/negative-search-filter-9eb2bd`): `origin/main` at `e42c91a82`, the delivered code in
the working tree on 2026-10-11, and a local PostgreSQL for the readings in D1 and in Measured.

> **Provenance.** Written by `adr-keeper` for the driving session from Klas's two answers, the architect's form round and the delivered code
> (CLAUDE.md §9.2, §13). The two product choices are Klas's. D1–D10 are the driving session's, and the mandatory agents review the
> implementation in the PR. This ADR sits in the ignored 0071–0199 range and is promoted with `git add -f` and a README row (precedent 0154,
> 0156–0164), because a gitignored decision is invisible to the next session that touches the search port. It is numbered 0167 because open
> PR #2131 holds 0165 and its second ADR is expected to take 0166. The form-round report is local-only. ADR 0062, 0067 and 0043 are tracked
> and carry their amendments in the same PR.

---

## Context

`/jobb`'s field had no way to say "not". ADR 0067's E2h note stripped a leading `-` from a word because `websearch_to_tsquery` already read it
as NOT, and left the minus operator as a phase of its own for Klas's GO. #2143 is that phase. Klas's first answer fixed what a minus-word
means: it hides every ad the same word would find as a plus-word, and every ad whose taxonomy label equals the word. Five facts shaped the rest.

- **The grammar has to be ours.** `websearch_to_tsquery` has a negation of its own, but its tokenisation is not the one a keyboard, an
  autocorrect or a comma produces (measured in D1). A word the field shows as excluded must be one the server reads as excluded.
- **Where the text is parsed now matters.** Until now parsing was hygiene. The list, facet and remote-count handlers parsed the live q, and the
  replays (a saved search, the new-results count, the recent-search count) passed the stored q to the filter unparsed. A minus-word is meaning,
  and a stored search must mean what the live one meant. ADR 0067 Beslut 6 already stores the raw q and parses per run, so one parse inside
  the search port serves every caller.
- **The exclusion reads `search_vector`.** That is the wide stored column whose detoast cost TD-94 made a budget problem (ADR 0062 Amendment
  2026-06-13), and an exclusion over it is paid by every row the query reaches, not only by the hits.
- **A negation over a nullable column loses rows.** `NOT` over an unknown is unknown, and a job ad with no value in a taxonomy column names
  nothing and must survive.
- **The filter is name-unaware, the taxonomy is not.** `ApplyFilter` works on raw concept ids (ADR 0043 Beslut C). Turning "deltid" into the
  concept it names needs the ACL, and the question is where it may enter.

## Decision

**A word with a leading minus is excluded from the search. The search port parses the raw text itself, and each minus-word becomes a
correlated `NOT EXISTS` over the predicate the same word would have as a plus-word, plus an exact-label exclusion on the taxonomy columns.**
D1–D10 are this ADR's numbering.

### D1 — The grammar is ours, and the frontend mirrors it (`SearchQueryParser`; `search-text-grammar.ts`)

After the existing normalisation (whitespace collapsed, control and format characters stripped, length bounded), the text is split on
whitespace and comma. A token whose first rune after any leading `+` is dash punctuation (Unicode `Pd`) or U+2212 is an **excluded term**.

- **The term never starts with an operator.** Its leading run of `+`, `"`, dashes and the operators `websearch_to_tsquery` skips while it
  waits for an operand (`! & | ( ) <`) and its trailing quotes are stripped. Without that, `-"-deltid"` would become the term `-deltid`, and
  `-(-deltid` the term `(-deltid`, each of which inside the `EXISTS` reads as `!'deltid'` and inverts the set.
- **Bounds.** A term under `SearchCriteria.QMinLength`, or with no letter or digit, is dropped, so it takes none of the five places. Duplicates are dropped case-insensitively and the first casing wins. At most
  five are kept (`ParsedSearchQuery.MaxExcludedTerms`; the frontend mirrors it as `Q_MAX_EXCLUDED_TERMS`). A bare operator token, a lone dash
  included, is dropped.
- **A dash inside a word stays positive** (`e-handel`); only a token-initial dash excludes.
- **A text with no minus-word returns exactly the pre-#2143 residual**, commas included.
- **Why not hand the dash to `websearch_to_tsquery`.** Measured 2026-10-11 on a local PostgreSQL, constant expressions only: it reads
  `systemutvecklare,-deltid` as a phrase, and `–deltid` (en dash) and `−deltid` (U+2212) as plain positive words. The dash a keyboard or an
  autocorrect produces would exclude nothing, and a comma-joined minus would turn the positive word into a phrase.

### D2 — What a minus-word hides (Klas's choice 1)

Two things, joined by OR:

- **Text.** Every ad the same word would find as a plus-word: the title and employer substrings (for a word with a run of three letters or
  digits, the run pg_trgm can serve, as for a plus-word), the SSYK arm through the synonym expansion, and the full-text arm.
- **Labels.** Every ad whose kommun, yrkesområde (through its yrkesgrupper), yrkesgrupp, yrke, anställningsform or omfattning label equals the
  word: trimmed, whitespace-collapsed, case-insensitive, **never a prefix** (`-deltidsjobb` does not name Deltid).

**No region arm.** Every län label ends in " län" (pinned by `TaxonomyReadModelIntegrationTests.EveryRegionLabel_EndsInLän_WhenSeeded`) and a
term holds no whitespace, so no word can name one. A yrkesområde is indexed as its yrkesgrupper because `job_ads` has no field column and the
hero field turns a typed field into its groups the same way.

### D3 — The search port resolves the raw text, for every consumer

`JobAdFilterCriteria.Q` is the raw text. `JobAdSearchTextResolver` (the parser, `IOccupationSynonymExpander` and `ITaxonomyLabelMatcher`)
turns it into a `ResolvedJobAdFilter(Criteria, Text)` and is called first in every method of `JobAdSearchQuery` and
`PerUserJobAdSearchQuery`. `ApplyFilter` takes only that pair, so no method can resolve one text and filter on another.

- The ListJobAds, GetFacetCounts and GetRemoteAdCount handlers pass the text raw and no longer take the parser.
- The replays (`SearchCriteriaMapping` → RunSavedSearch, the new-results count, the recent-search count) now parse exactly as the list does.
  That closes a latent list-versus-replay gap: a stored q reached the filter without the normalisation the live q got.
- `IJobAdSearchQuery` (registered by `AddPersistence`) now needs `ISearchQueryParser` from `AddApplication`, which both hosts call.

### D4 — `ITaxonomyLabelMatcher`: a narrow port, one consumer, one instance

The label lookup is its own port beside `ITaxonomyReadModel` (ISP: that port has six methods and hand-written fakes). `TaxonomyReadModel`
implements it from its cached snapshot with a dictionary lookup (`BuildExactLabelIndex`, `MatchExactLabelsAsync`), so there is no per-request
database hit (ADR 0043 Beslut D).

- **One instance behind both ports.** `AddTaxonomyReadModel` registers the concrete singleton and forwards both interfaces, with `TryAdd` at
  the three sites that need it. Pinned by `ListJobAdsExcludedTermsTests.TheLabelMatcher_IsTheReadModelsOwnInstance`.
- **One consumer.** The resolver is the only class that injects the matcher, pinned by
  `TaxonomyAclLayerTests.Only_the_search_text_resolver_injects_ITaxonomyLabelMatcher`. That is the mechanical form of "the ACL enters the query
  path at exactly one point" (ADR 0043 Amendment 2026-10-11).

### D5 — The SQL form: `NOT EXISTS` for text, an explicit null test for labels

Each minus-word is a correlated `NOT EXISTS` over the **unfiltered** `job_ads` root, whose inner predicate is the plus-word's own
`MatchesText`. The exclusion is therefore the set difference of the plus-word's own match, by construction.

- **Never an inline `NOT`.** It would detoast `search_vector` for every outer row (TD-94), and `NOT` over a `NULL` SSYK arm is `NULL`, which
  drops the row.
- **Never `NOT IN`.** `Contains` over the `JobAdId` value object does not translate, and a hashed `NOT IN` subplan degrades to a per-row
  subplan past `work_mem`.
- **Labels inline**, as `col == null || !ids.Contains(col)`. These are B-tree columns with no detoast cost. An ad with no value names nothing and
  survives, written out rather than left to EF's null compensation.
- **The Active filter stays on the outer side only.** Inside the `EXISTS` it would let the status index serve the inner side and detoast per
  probe. `ListJobAdsExcludedTermsTests.TheExclusion_IsANotExistsOverTheUnfilteredTable` pins one status predicate. `ApplyFilter`'s first
  argument must be the unfiltered root.
- **The inner side is index-served by all three arms.** `JobAdPlannerUsabilityOracleTests.QExclusion_TheAntiJoinsInnerSideIsServedByEveryArm`
  pins the full-text GIN and both trigram indexes.

### D6 — Count hygiene follows what the query reads

`BitmapPlanCount` takes the `ResolvedSearchText`. `SET LOCAL enable_seqscan = off` now runs whenever the query reads `search_vector`, positive
or excluded (`HasFreeTextPredicate`). With minus-words it also runs `SET LOCAL enable_nestloop = off`.

Measured: five minus-words, three of them matching nothing, were planned as nested-loop anti joins that probe the primary key once per counted
row and detoast `search_vector` at every probe, the same cost the planner cannot see, in another node. The count took 205 ms, and list plus
count 458 ms at p95. With the nested loop prohibited each minus-word is a hash anti join over the GIN bitmap, built once, and the count took
45 ms.

**An item read with minus-words runs under the same pair** (`BitmapPlanCount.ReadWithExclusionPlanAsync`); without minus-words it still runs
bare. Under its `LIMIT` the planner picks the nested loop anti join, expecting twenty survivors early. Measured 2026-10-11 at 32 000 active ads,
`EXPLAIN ANALYZE` of the item read: for `-svenska`, a word in every active ad, it probed and detoasted every row, 253 ms; under the pair, 15 ms.
The price falls on the common case: `-stockholm`'s item read went from 0.3 ms to 12 ms, and `lärare -deltid`'s from 2 ms to 5 ms.

### D7 — The full-text arm is evaluated last

In `MatchesText` the substring arms (title, employer) and the SSYK arm come first and the full-text arm last. PostgreSQL 18's executor
evaluates the arms as written and stops at the first true (measured, not guaranteed: docs §4.2.14), so a row already matched by title, employer or SSYK never has its `search_vector` detoasted in a recheck or
a filter. `-are`, which matches every title, went from 663 ms to 84 ms at p95. The order also applies to the plus-word path: the same rows
and the same index eligibility, so no result changes.

### D8 — LIKE patterns are escaped

`LikePatterns.Contains` escapes `%`, `_` and `\` (the escape in `EmployerDisambiguationQuery` moved there), and every
LIKE passes `LikePatterns.EscapeCharacter`: EF Npgsql emits `ESCAPE ''` for the two-argument `EF.Functions.Like`, which had turned the escape
off in the employer typeahead since #1546. **Rows change** for a q containing
one of them: "100%" now matches literally instead of as a wildcard. This applies to the plus-word path too, and ADR 0062's amendment records it.

### D9 — Recent-search capture

A q of only minus-words is captured raw, because it filtered the search. Relevance is downgraded to PublishedAt desc when there is no positive
residual, as it already was for a residual the parser nulls, because relevance ranks the positive words. The personnummer scan stays on the raw
q, so it covers excluded terms. **A named asymmetry, not closed:** an explicit saved search (`SearchCriteria.Create`) still accepts Relevance
with a q of only minus-words; it runs as PublishedAt desc.

### D10 — The frontend (Klas's choice 2 included)

- The tokenizer keeps `-term` as an excluded q-word in the canonical ASCII-hyphen form, never matched against the taxonomy, in a run of its
  own. `-göteborg` therefore no longer adds Göteborg as a filter, which the E2h strip did, and the word after an excluded word is not glued to
  the word before it.
- The minimum length is measured on the positive words and per excluded term, as the server does. A sixth exclusion gets a notice
  (`ui.heroSearch.excludeLimitNotice`).
- Typeahead asks nothing for a dash-led caret word, and a title suggestion never appends a dash-led word: either would add what the user is
  taking away, or put an exclusion into q that the user never typed.
- The chip shows `-deltid`, and its remove button reads "Ta bort det uteslutna ordet {label}". Relevance sort is offered only with a positive
  word.
- The help line gains "Skriv minus före ett ord för att utesluta det, till exempel -deltid."

## Alternatives considered

- **Text only, without the labels.** *For:* the simplest reading; every hidden ad says the word. *Against:* `-deltid` would leave an ad whose
  omfattning is Deltid when its text never says so. Klas chose the labels (D2).
- **Parse in the handlers and carry parsed terms and concept ids in `JobAdFilterCriteria`.** *For:* no new Infrastructure class. *Against:*
  parse plus asynchronous label resolution at six construction sites in Application, among them the static `SearchCriteriaMapping` and an N+1
  loop; the record grows id lists that `GeoUnionLabelParityTests` and `DeriveLabel` must classify. More failure points for the same guarantee.
  Rejected (D3).
- **Pass the dash to `websearch_to_tsquery`.** *Against:* measured in D1. Rejected.
- **An inline `NOT`, or `NOT IN`.** See D5. Rejected.
- **A label lookup as a seventh method on `ITaxonomyReadModel`.** *Against:* ISP, and the hand-written fakes of that port (D4). Rejected.
- **A region arm.** *Against:* unreachable while terms hold no whitespace, which makes it Speculative Generality (the reason ADR 0067 used to drop
  its dimension fields), and a test of it would rest on a premise production cannot create (AGENTS.md §5, Tests). Left out until quoted phrases
  exist.

## Consequences

### Positive

- Klas's case works: `lärare -deltid` hides what "deltid" finds as text and what the Deltid omfattning labels, on the list, the counts, the
  facets, a saved search, a recent search and the new-results count alike, because the port parses once for all of them.
- No migration. ADR 0067 Beslut 6 stores the raw q and parses per run, and the columns the labels compare against already exist.
- The exclusion cannot drift from the plus-word it complements (D5), and `ApplyFilter` cannot be handed a text resolved from another criteria
  (D3). The ACL has one entry into the query path, and an architecture test keeps it so (D4).
- Within ADR 0045's class (a) budget, p95 300 ms, at the measured corpus (Measured).

### Negative

- **`-stockholm` also hides an ad whose description merely mentions Stockholm.** That is the text half of Klas's choice: "kontor i Stockholm,
  tjänsten är i Malmö" disappears.
- **Quoted phrases are not supported.** The grammar splits on whitespace, so a phrase is not a unit: `-"stockholms län"` reads as the minus-word
  `stockholms` followed by a positive word, and the region arm stays unreachable (D2).
- **A contradiction returns nothing**, literally: `lärare -lärare`.
- **Input in decomposed form (NFD) does not match a label**, as it does not for plus-words today.
- **Up to five anti joins per query**, on every list, count and facet call. The measurements are observe-only (ADR 0045) and no fitness
  function ratchets them. A plus-word in the same text dominates the cost.
- **Two implementations of one grammar.** The field and the server are kept in step by hand; each side has its own tests. A divergence shows an
  excluded word the server reads as positive, or the reverse.
- **A host that registers `AddPersistence` without `AddApplication` can no longer resolve `IJobAdSearchQuery`** (D3). Both hosts call both.
- **D8 changes rows** for existing searches containing `%`, `_` or `\`.
- **The saved-search asymmetry stays open** (D9).

## Measured

On 2026-10-11, 40 000 ads (32 000 active), a developer machine, by `ExcludedTermLatencyMeasurement`, which is observe-only (ADR 0045): the
durations go to the test output, and the test asserts that each exclusion removed ads and that the count's plan is an anti join over the GIN.
A dated reading of a finished run, not a live number: re-run the class to regenerate it. Handler latency through the shared search port, list
and count together, against ADR 0045 class (a), read and list: p95 300 ms.

| q | p50 | p95 | |
|---|---|---|---|
| (none) | 5.6 ms | 7.0 ms | |
| `stockholm` | 5.6 ms | 6.1 ms | plus-word baseline |
| `-stockholm` | 26 ms | 27 ms | |
| `lärare -deltid` | 14 ms | 15 ms | |
| `-are` | 55 ms | 67 ms | excludes every ad |
| `svenska` | 8.6 ms | 9.0 ms | plus-word baseline |
| `-svenska` | 27 ms | 32 ms | a word in every active ad |
| five minus-words | 38 ms | 43 ms | |
| `-c++` | 18 ms | 19 ms | no trigram, so no substring arm |
| five minus-words with no letter | 4.9 ms | 5.7 ms | dropped by the grammar (D1) |
| new-results count, `-svenska` | 14 ms | 14 ms | `CountNewSinceAsync`, last hour |

The corpus sizes the descriptions to be stored out of line, as production's are, so the detoast cost is in the number, and it seeds the
taxonomy so the label arm runs. It is vacuumed after seeding, as production's autovacuumed table is; before that, the plus-word `svenska`
read 10 ms in one run and 170 ms in another. Three changes in this PR made
these numbers, each read before against the unvacuumed corpus: D6's count (five minus-words 458 ms p95 without it) and item read (`-svenska`
253 ms without it), and D7 (`-are` 663 ms p95 with the full-text arm first). The planner-usability oracle pins that the anti join's inner
side is servable by all three GIN indexes (D5).

## References

- `docs/reviews/2026-10-11-2143-form-dotnet-architect.md` (local-only)
- Code: `src/Jobbliggaren.Application/JobAds/Internal/SearchQueryParser.cs` · `src/Jobbliggaren.Application/JobAds/Abstractions/`
  (`ParsedSearchQuery`, `ITaxonomyLabelMatcher`, `TaxonomyLabelMatchDto`, `JobAdFilterCriteria`) · `src/Jobbliggaren.Infrastructure/JobAds/`
  (`JobAdSearchTextResolver`, `JobAdSearchComposition`, `BitmapPlanCount`, `LikePatterns`, `JobAdSearchQuery`, `PerUserJobAdSearchQuery`) ·
  `src/Jobbliggaren.Infrastructure/Taxonomy/TaxonomyReadModel.cs` · `src/Jobbliggaren.Application/RecentJobSearches/Behaviors/RecentJobSearchCaptureBehavior.cs` ·
  `web/jobbliggaren-web/src/lib/job-ads/` (`search-text-grammar.ts`, `tokenize.ts`)
- Tests: `tests/Jobbliggaren.Api.IntegrationTests/JobAds/` (`ListJobAdsExcludedTermsTests`, `ExcludedTermLatencyMeasurement`,
  `JobAdPlannerUsabilityOracleTests`) · `tests/Jobbliggaren.Architecture.Tests/TaxonomyAclLayerTests.cs`
- ADR 0039 Beslut 1 · ADR 0043 Beslut C, D · ADR 0045 · ADR 0062 · ADR 0067 Beslut 5, 6 · AGENTS.md §3.6, §5 · CLAUDE.md §9.2, §13
