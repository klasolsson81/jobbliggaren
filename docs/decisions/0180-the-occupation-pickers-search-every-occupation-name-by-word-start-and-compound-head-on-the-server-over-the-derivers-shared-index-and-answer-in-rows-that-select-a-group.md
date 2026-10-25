# ADR 0180 — The occupation pickers search every occupation-name by word start and compound head on the server, over the deriver's shared index, and answer in rows that select a group

**Status:** Accepted · **Date:** 2026-10-23 · **Deciders:** Klas Olsson (product owner: the #2198 direction of 2026-10-22; the
hotspot, order and E1 answers of 2026-10-23), `senior-cto-advisor` (the ruling and its addendum, after plan reads by
`dotnet-architect`, `security-auditor` and `design-reviewer`)
**Amends:** ADR 0043 and ADR 0067, by their Amendments of 2026-10-23 (#2198). ADR 0141 D7 holds and is not superseded.
**Related:** [#2198](https://github.com/klasolsson81/jobbliggaren/issues/2198) (PR 1 of 2) · #2199 (the parked name-axis
filter) · ADR 0018 (no state change on GET) · ADR 0040 Beslut 4 (the user confirms every group) · ADR 0042 Beslut C (the
short-lived client read) · ADR 0043 Beslut A and D · ADR 0045 (the 150 ms class (b) budget and the document-size budget) ·
ADR 0047 (rendered output before a design verdict) · ADR 0055 (the popover, amended in PR 2) · ADR 0067 Beslut 1, E2a and
VAL 2b · ADR 0120 (a rendered count is exact or absent) · ADR 0134 (the personnummer profile) · ADR 0137 (sv and en,
register data) · ADR 0141 D7 (the deriver) · ADR 0144 D5 (the empty state)
**Measured against:** `origin/main` `b7c971c01` (the plan reads); taxonomy v30; the branch head `d68279f8c` (the handler
timings and the full solution run), all 2026-10-23.

> **Provenance.** Written by adr-keeper from the rulings cited here. The `docs/reviews/` files named in this ADR are local
> (ADR 0072): they are absent from a fresh clone and from GitHub, so every operative number is restated here, with the
> command that regenerates it where one exists. Where the CTO's addendum and his ruling differ, the addendum wins; the
> ruling is cited as "CTO (a)–(j)" and the addendum as "addendum A–AD".

---

## Context

The occupation pickers (the Matchning dialog and the onboarding rail, both through `OccupationSection`, and the `/jobb`
Yrke popover) let a user choose ssyk-level-4 groups (yrkesgrupper) by browsing occupation field, then group (ADR 0067
Beslut 1). Their only search is a client-side substring filter over group labels (`filterOptions`). That finds a user
who types a group's own words and misses one who types the word they think in: group labels are plural
(`Grundutbildade sjuksköterskor`), so the singular `sjuksköterska` and `undersköterska` hit no group label (measured
2026-10-23 over v30). #2198 asks that a typed word find
its group (`projektledare`, `sjuksköterska`, `programmerare`, `backend`, `UX`) and that the user can see, at the moment of
choosing, which group a name selects. Klas's prompt asked for "a11y (combobox-mönstret)".

**The data.** Taxonomy v30 holds 2,323 occupation-names and 400 groups. The frozen
`occupation-name-to-ssyk-level-4.v30.json` maps 2,153 of the names to a group present in the snapshot; 170 names have no
mapping and cannot be rows, and 4 groups have no mapped name.

**The engine that exists.** `OccupationCodeDeriver` (ADR 0141 D7) resolves a title to groups over exactly that join. It is
stemmed and spread-gated: a hit resting only on a scattered word (`operatör`, spread across 7 groups) is dropped, which is
right when an engine proposes from a CV and wrong where the user chooses (CTO (c)). ADR 0141 accepts that "a word yields
nothing until it is complete" on its own surface; the pickers' acceptance does not.

**The constraints from the record.**
- ADR 0043 Beslut A: no external hop per keystroke, and the user never sees a concept id.
- ADR 0067 Beslut 1 and E2a: no third occupation-name level in the UI, and the tree DTO drops names.
- ADR 0040 Beslut 4 and AGENTS.md §5: a group is never picked for the user.
- ADR 0042 Beslut C and AGENTS.md §4: a short-lived client read is a debounced GET with an `AbortController`, never a
  Server Action and never TanStack Query.
- ADR 0045: class (b) typeahead is 150 ms p95, Klas-locked; the document budget is 30 kB (30,720 B).
- ADR 0141's rejected alternatives: a token-aware matcher over group labels (D2 M1) and a bulk payload matched in the
  client (D3 B).

**The scope.** PR 1 (this one) is a vertical slice: the backend and `OccupationSection`, which reaches the dialog, the card
(through the dialog) and the rail. PR 2 is `/jobb` Yrke (D5, Implementation). `/foretag` is not adopted (D6).

## Decision

### D1. The matching runs on the server, in C#, over the deriver's index, behind one authenticated GET (CTO (a); addendum M, N, O, P, Q, R)

`GET /api/v1/job-ads/taxonomy/occupations/search?q=`, a sibling of `skills/search` on the `/job-ads/taxonomy` family.

- **Authenticated twice.** The route inherits the group's `RequireAuthorization`, and `SearchOccupationsQuery` implements
  `IAuthenticatedRequest` (addendum M). That is defence in depth for one line, and it matters: the bucket is partitioned
  on `sub`, an anonymous caller gets `NoLimiter`, so without authentication the bulkhead protects nothing. The nearest
  precedent, `ResolveOccupationDivisionsQuery`, implements it; `SearchSkillsQuery` lacks it, which is an omission and not
  a rule.
- **`Cache-Control: private, no-store`.** The answer varies per term and per user. An ETag or `max-age` belongs to a
  static index, and would make each term's URL a stored browser-cache entry. Never `public`.
- **Its own bucket, `occupation-search`: 30 per 10 s in 6 segments, a token bucket per `sub`, `QueueLimit` 0** (numbers set
  by security-auditor, addendum N). One request per settled 300 ms debounce caps continuous typing at about 3.3 requests
  a second, and a 13-letter word typed with a pause after every key costs at most 12 requests, inside the burst of 30.
  Once the index is built the handler is an in-memory scan with no database read, so the bucket contains floods rather
  than protecting cost. It is
  never `taxonomy-read` (20 per 60 s is 3 tokens per 10 s and would starve the tree read) and never `occupation-divisions`
  (another surface's bulkhead; ADR 0043 Beslut D, least common mechanism). The numbers may be raised after the timing
  below and never lowered after shipping. The defaults live in code (`RateLimitingOptions.OccupationSearch`), so no
  configuration key is added and CLAUDE.md §11's dev-boot contract is not triggered. PR 2 shares this bucket: one user
  drives one picker at a time.
- **The parameter is exactly `q`**, a key the Caddy edge deletes from the log it writes for its own 5xx. A route reading
  `term`, `query` or `word` would put the term into a stored log. The Next route handler reads only `q`, and its test
  pins that `?q=a&term=b` forwards only `q`.
- **The validator measures the trimmed query, 2 to 100 characters** (`MinQueryLength` and `MaxQueryLength` on the query).
  Its messages interpolate only those two constants and never echo the term. 100 because every mapped name label fits
  (the sweep asserts it), so "typed in full" holds for the input's `maxLength` too. The client gates on a trimmed length
  of at least 2 and runs no tokenizer of its own: tokenisation has one home, in C# (Hunt/Thomas 1999, DRY). A query whose
  longest token is a single character returns no rows (D3).

**The term is never logged or stored.** Measured on `origin/main`, layer by layer (security-auditor's plan read):
Caddy writes no access log, only its own 5xx, with `q` deleted; Next has no server hook that logs; the API's
`LoggingBehavior` logs the type name, the elapsed time and the exception; validation errors go to the 400 body with fixed
messages; the 429 log writes the path only. One framework line would print it: the hosting diagnostics' "Request starting
… ?q=<term>" at Information, kept out only by the `Microsoft.AspNetCore` Warning rule, which no test referred to. Three
tests make the property executable, and each was shown red once (commit `d68279f8c`):
- **The canary.** A canary term reaches no log record, and not the 400 body, on the ok, 400, 429 and 500 paths
  (`SearchOccupationsEndpointTests` for ok, 400 and a 500 from a faulting read model, a state production can reach with the
  database down at cold start; `OccupationSearchRateLimitTests` for 429 on the production numbers). The 500 case is
  required: the exception path is where a URL is most likely to be logged.
- **The hosting-diagnostics fitness fact.** `ApiRequestLoggingConfigurationTests` runs the shipped Api configuration
  through the real MEL filter engine and asserts `Microsoft.AspNetCore.Hosting.Diagnostics` is off at Information in the
  base, Development and Production overlays, with a vacuity guard (an ordinary product category is on at Information).
  The canary runs one environment's configuration; this is what covers Production.
- **The architecture pin.** `OccupationSearchLayerTests.Nothing_on_the_search_path_depends_on` asserts that
  `OccupationNameIndex`, `OccupationSearchRule`, `OccupationSearcher`, `OccupationCodeDeriver` and
  `SearchOccupationsQueryHandler` depend on neither `Microsoft.Extensions.Logging` nor `System.Net.Http`. A dependency
  check also catches static use, and it keeps the per-keystroke path free of an external hop (ADR 0043 Beslut A). Each pin
  folds compiler-generated types (async state machines, closures) into their declaring class, or an `async` method would
  hide the reference.

**Where the term may go.** Only into the component's state in the browser: never a URL, a search parameter, storage, a
Server Action argument or the DTO.
- The BFF is a Next GET route handler, `app/api/me/occupation-search/route.ts`, a sibling of `me/occupation-divisions`,
  mapping ok, 401, 429 with `Retry-After`, and 502 (AGENTS.md §4: never a Server Action).
- The fetcher lives in its own module, `lib/api/occupation-search.ts`, through `authedFetch` and
  `new URLSearchParams({ q })`, with `cache: "no-store"` and never `next.revalidate`. It is never placed beside
  `getTaxonomyTree`: a cached fetch stores the URL in the web container's writable layer, and a failed cache write logs
  the input (security-auditor read this in Next 16.2.9's `patch-fetch.js`; it was not re-measured on the pinned 16.3.8).
  The two have different caching policies and so different reasons to change. Every BFF branch sets
  `private, no-store` explicitly.
- ADR 0134's personnummer profile does not apply: it covers `?q=` where the value persists to `recent_job_searches.q` and
  is rendered again, and this term is never persisted. **The day it is persisted, the profile applies**
  (`PersonnummerGapProfileCallSiteTests` pins the call sites).
- **Carried to PR 2:** the Yrke term never enters the `/jobb` URL. A new key there is unscrubbed at the edge and sits in
  browser history; reusing `q` would mix it into the free-text search that ADR 0134 captures. Either re-triggers
  security-auditor.
- **GDPR (AGENTS.md §8 point 8, security-auditor).** No new stored category, recipient, sub-processor or third-country
  path; no DPIA trigger; no change to the privacy policy or the processing register. She found 0 Blockers and 0 Majors
  against the design. Four Major build conditions bind PR 1 (the logging tests, the fetcher, the auth and bucket wiring,
  the key `q`), and she rules on the final diff.

**Wiring.** `SearchOccupationsEndpointTests.The_route_carries_its_own_occupation_search_policy` reads the built
`EndpointDataSource` and asserts the route carries `occupation-search` and neither `taxonomy-read` nor
`occupation-divisions`. `OccupationSearchRateLimitOptionsTests` pin the key and the 30/10/6 defaults, and
`OccupationSearchRateLimitTests` shows one user's burst ending in a 429 with `Retry-After` while another user still
searches. An anonymous call is a 401 and the searcher never runs. A test that pinned every `/job-ads/taxonomy*` route,
`skills/search` included, to its current bucket was refused: it would turn wiring nobody has ruled on into specification
(AGENTS.md §5, Tests).

### D2. One shared index, two thin ports (CTO (b); addendum A, C, D, E, L)

- **`OccupationNameIndex(ITaxonomyReadModel)`** is extracted from the deriver's `BuildAsync`: an `internal sealed`
  singleton in `Infrastructure.Taxonomy` that takes the port, never the concrete `TaxonomyReadModel` (which also
  implements `ITaxonomyLabelMatcher`, so `TaxonomyAclLayerTests` would correctly go red).
  - It holds the matchable entries `{name label, group id, group label}` in tree order and the 400 groups once each, with
    the Ordinal-minimum group label.
  - It holds **no lexemes and no tokens**: each reader builds its own view.
  - The build moved verbatim: the iteration order, the filter "mapped and the group exists", the Ordinal-minimum label,
    and the lazy, retry-on-fault cache (published only after the await).
- **`OccupationCodeDeriver`** takes `(OccupationNameIndex, ITextAnalyzer)` and keeps its gate, `MaxCandidates`, enum, port
  and callers. **No assertion in its tests changed**; only how its tests construct it did (the construction sites in
  five test files, and the words that name the reader in a comment and an exception message). The proof of behaviour
  was a one-off characterization: a hash of `DeriveAsync` over every mapped name and every head word, `68749D8B…C00B`,
  identical before and after the extraction. It was committed (`cf9c0e713`) and deleted (`e8adf796e`) in this PR: kept
  permanently, an opaque hash would fail on legitimate taxonomy work without saying why (Winters et al. 2020, ch. 12).
  The permanent guard is `OccupationNameIndexEquivalenceTests`: the index equals the committed snapshot joined to the
  frozen map, read by resource name through `OccupationNameOracle`, which shares no code with the index. The count is
  derived from the files.
- **`IOccupationSearcher`** (Application, `JobAds/Abstractions`) is the picker's port. **`OccupationSearchRule`** is a pure
  `internal static` class (`Prepare`, `Search`), so its rule is unit-testable without a database (AGENTS.md §2.4;
  Cohn 2009). **`OccupationSearcher`** is only a lazy, retry-on-fault cache of the prepared index and one call to the
  rule, with no `ILogger` and no `HttpClient`. `SearchOccupationsQuery`, its handler and its validator are thin, after
  `SearchSkillsQuery`; the handler injects only the new port, so the `ITaxonomyReadModel` allowlist and the
  `ITaxonomyLabelMatcher` test are untouched.
- **Why two ports and not one engine with two modes.** A mode parameter is a flag argument (Martin 2008, ch. 3; Fowler
  2018, "Remove Flag Argument"). The two share only the index; their rule, gate, output, cap and callers differ, so they
  have two change-reasons (SRP, Martin 2017, ch. 7), and under ISP (ch. 10) the deriver's callers and their mocks
  should never see a picker method. `SkillTaxonomyIndex` and ADR 0043's amendment of 2026-10-11 are the precedents.
  `LocalTextAnalyzer` was rejected for this job: it stems, and its stoplist holds `av` and `utan`.
- **The counting basis.** There are four occupation indexes before this PR and four after:
  1. the deriver's index (a title to a group, stemmed and spread-gated; ADR 0141 D7), now the shared `OccupationNameIndex`;
  2. the curated `q` map, `SearchSynonyms:Occupations` (recall expansion of the free-text search; another bounded
     context's map);
  3. `TaxonomyReadModel.SuggestByPrefixAsync` (the hero typeahead, a whole-label `StartsWith`);
  4. `TaxonomyReadModel.MatchExactLabelsAsync` (a minus-word by exact label, ADR 0167).

  The PR adds **one matching rule**, which reads index #1. It adds no index, and so no third word-to-occupation mechanism.
  The measurements behind that claim: the frontend holds no occupation index or rule (a grep acceptance in the PR body),
  and `OccupationSearchLayerTests.The_frozen_map_loader_has_exactly_one_dependent_the_shared_index` pins that
  `OccupationGroupMappingLoader`, each dependent folded to its outermost declaring type, is read by exactly
  `OccupationNameIndex`. The call to the loader sits in a synchronous `private static` helper on the index so the
  reference is on the declaring type itself; the pin was shown red with a second call site (commit `d68279f8c`).
- **The resource has two readers:** the loader (at runtime; its only dependent is `OccupationNameIndex`) and the C2
  migration (through its own constant). ADR 0043's amendment records that role-widening.
- **Two group-spread quantities that must not be merged.** The deriver's `GroupSpread` counts stemmed lexemes over the
  whole taxonomy. The picker's Group-vs-Word decision counts head words over the names that matched. They are different
  quantities with different questions, and each reader builds its own.
- **The difference from ADR 0141's rejected M1.** M1 was a separate index over group labels, a third matcher beside a
  delivered one. Here group labels are matched inside the one shared index, which this surface's acceptance requires:
  today's label search must keep working, and the four groups with no mapped name are reachable only through their label.
- **ADR 0141 D7 holds.** `/foretag` still resolves through the deriver, and this decision applies D7's own principle of one
  authority per question.

### D3. The rule and its parameters, with their dated derivation (CTO (f); addendum D, F, G, I, AC)

**Tokenisation.** NFC, then lower case. Split on everything that is not a letter or a digit, so a hyphen, slash, comma,
parenthesis, period, colon and `&` all split. Diacritics are not folded (parity with the deriver's Decision 4).

**Matching.**
- The **start arm**: a query token hits a label token at its start.
- The **end arm**: or at its end, when the query token is at least `SuffixFloor` = 4 characters. Swedish puts a compound's
  head last, so `sjuksköterska` reaches `Anestesisjuksköterska`, while `ux` never reaches `vuxenutbildning`.
- Every query token must hit (AND).

**Parameters, derived 2026-10-23 over v30** (inline `node -e` scripts over the committed snapshot and map, kept in the
local report `docs/reviews/2026-10-23-2198-map-measure-matching.md`; the reproducible instruments in the repository are
the C# tests named below).
- **`SuffixFloor` 4.** At floor 3, `ker` reaches 87 rows and 167 names. Floor 5 turns the compound head off for every
  four-letter word: `chef`, `vård`, `tolk`, `spel`, `smed`, `kock` and `vakt`; `kock` would lose *Grillkock snabbmat*.
  A bracket test pins it: `kock` reaches *Grillkock snabbmat*, and a token one letter shorter gains no hit through the end
  arm alone.
- **Minimum query token 2.** The acceptance words `it`, `hr` and `UX` are two letters, and a one-letter query gives a
  median of about 52 rows and a maximum of 186 (with the stoplist) to 235 (without). A query whose longest token is under
  two returns no rows.
- **Label stoplist exactly `{och, inom, samt, m, fl}`.** Without it `och` reaches 207 rows (180 groups, 79 names), `m` 214
  and `fl` 101 (91 of them `m.fl.` groups); with it `och` and `inom` reach none. **Never `av`:** with it,
  *AV-tekniker* gives 60 rows, without it 3. **Keep `utan`:** it tells variants apart (*…, utan yrkesbevis*). A query drops
  a stopword only when another token remains, so a lone `fl` still reaches *Florist*. The set is an `internal` constant
  and `OccupationSearchRuleTests.LabelStopwords_AreExactlyTheFiveMeasuredWords` pins its exact membership.
- **Cap 50 rows, with the exact `TotalRows`.** `chef` reaches 60 rows and `in` 63 (the timings below). The cap counts rows;
  variants are uncapped, and the largest Word row is 37.

**Ordering.** The tiers, in order: an exact name, an exact group label, the start arm, rows reached only through the end
arm. Within a tier, **sv-SE collation, then Ordinal, then group id or head word**, so the order is total and the server is
the only ordering authority; the frontend never re-sorts. A row's tier is the best tier of the names and the label that
reached it. `MatchedNames` sort by tier, then collation; variants sort with the exact match first. Ordinal alone puts Ä
before Å, which is wrong for a Swedish civic product, so `OccupationSearchRuleTests.Collation_IsSwedish` pins Å < Ä < Ö.
The collation is ICU and **fails loud under invariant globalization**: the comparator is built in the rule's static
initialiser, and ICU is present in the `aspnet:10.0-noble` image with no `InvariantGlobalization` flag in any project or
props file (addendum F).

**Rows.** Matched names are grouped by **head word**, the label's first run of letters, digits and hyphens in its source
spelling (`st-läkare` keeps its hyphen). Measured: 1,785 distinct head words, 1,676 with one variant and 109 with more.
- If the names that matched under one head word sit in **one group**, they join that group's **Group** row.
- If they span **several groups**, they become one **Word** row: at least 2 variants in at least 2 groups
  (`projektledare` is one row of 17 variants over 14 groups).
- **Group labels are matched by the same rule** and merge into their group's row; a row from a label match alone is
  allowed. That reaches the four groups with no mapped name and keeps today's label search.
- A row holding a name or a group label equal to the whole query is **exact**. In 13 cases the exact name lands only
  inside a Word row (*Elingenjör*, *Försäkringshandläggare*, *Gravör* and ten more): that Word row ranks first and carries
  the exact flag. A variant is never lifted out of its row, so the rule stays one row per head word.
- Labels are identity on the wire (D4), so the sweep pins that mapped name labels are unique ignoring case. When spellings
  of a head word differ only in case, its spelling is the Ordinal minimum.

**Two "exact" rules, on purpose.** The deriver's exact pass trims only (OrdinalIgnoreCase, no folding); the picker
normalises first (NFC, lower case, whitespace collapsed). Nobody should unify them: it would change the deriver.

**§5 classification (addendum AC).** `SuffixFloor`, the minimum token and the stoplist are a picker's search-rule
parameters, not the CV-review rubric data that AGENTS.md §5's "CV & matching engines" block covers. They are named
`internal` constants pinned by tests, after `OccupationCodeDeriver.MaxGroupSpread`, with their derivation dated here and
not in a code comment. If a reviewer reads §5 differently, that is a §12 matter and goes to Klas verbatim.

### D4. The wire and the selected state (CTO (b); addendum H, I, S, U)

- **A row is `Group` or `Word`.** A **Group** row selects one group (its id and label); the names that reached it are
  evidence (`MatchedNames`, and a `LabelHit` flag). A **Word** row carries the head word in its source spelling and its
  variants, each `{name label, group id, group label}`. A Word row **expands and is never selectable; each variant names
  and selects its own group** (ADR 0040 Beslut 4). Opening a Word row by default never selects anything.
- **No occupation-name concept id goes on the wire.** A variant therefore cannot be stored, the browser never holds the
  crosswalk, and the chip is the group by construction. What the user selects is a set of group ids, as before.
- **The answer** is one ordered list of rows plus the exact `TotalRows`. `Kind` is a string enum
  (`JsonStringEnumConverter<OccupationSearchRowKind>`, the house form). Rows are built only through `ForGroup` and
  `ForWord`; `ForWord` throws unless the variants number at least two and span at least two groups. Collections are empty,
  never null. The API sets no global `DefaultIgnoreCondition`, so the inactive kind's keys are written as null and the zod
  `discriminatedUnion` types them `z.null()`. The endpoint's wire-shape tests pin both kinds with the other kind's keys
  present.
- **Contracts unchanged:** the URL, `SearchCriteria`, `MatchPreferences.PreferredOccupationGroups`, saved and recent
  searches, and the match profile. The proof the PR carries: `git diff origin/main...HEAD` over the contract paths shows
  only comment hunks, and the existing payload tests (`match-preferences-dialog.test.tsx`,
  `match-setup-rail-modal.test.tsx`, `match-preferences-card.test.tsx`) pass with no edited assertion.
- `OccupationMatchKind` is untouched and `OccupationMatchKind_is_pure_V2_no_group_label_kind` stays green: pure V2 was a
  CV-derivation decision, and the picker has its own row kind.

### D5. The interaction contract that PR 2 must reproduce (CTO (e) as amended; addendum U, V, W, X, Y, Z, AA, AD; design-reviewer Majors 1 to 7)

**Klas chose E1 on 2026-10-23: "Husets mönster (Rek.)".** The acceptance's "combobox-mönstret" is met by the house
pattern, verified as WCAG 2.1.1, 2.4.3, 2.4.7, 4.1.2 and 4.1.3, and **not** by a literal `role="combobox"`. The reasons:
APG models the combobox as a single value, has no multi-select combobox, and publishes no tree-popup example to test
against; an editable combobox keeps DOM focus in the input, so Space types a space, which multi-word AND queries
(`projektledare it`) need, so Enter would have to toggle and could no longer accept, leaving a control no screen-reader
user can predict; a tree laid out in two visual columns risks a DOM order that differs from the visual order (WCAG 1.3.2,
2.4.3); and the house has ruled the same way six times.

**The field.** A `<Label>` and `<Input type="search" autoComplete="off" maxLength={100}>`, with **no hint and no
`aria-describedby`**: below 2 characters nothing is rejected, the search simply does not run, so a hint would describe a
refusal that never happens. The en label carries the register note, "Search occupations (names in Swedish)". **One
persistent `role="status"`, polite and `aria-atomic`,** owned by the field and never remounted; never `role="alert"`
(ADR 0144 D5). It says: searching ("Söker…", no spinner and no skeleton), the count, no hits, unavailable, the cap line.

**The hook.** A state union `idle | pending | ok(result) | unauthorized | unavailable`. A 429, a 5xx and a network failure
all map to `unavailable`; 401 is told apart because its cause and action differ. `pending` is derived from
`answer.word !== word`, never kept as a flag; the answer is stored with the trimmed word it answers, so rows from an
earlier word are never shown. Debounce 300 ms, one `AbortController` per attempt, gate at a trimmed length of 2.

**The rows.**
- **Group rows and variants** are checkboxes (`CheckItem`, `role="checkbox"`), `aria-checked` from
  `selected.includes(groupId)`, named by the primary line through `aria-labelledby` and described by the secondary line
  through `aria-describedby`. `CheckItem` gains these as optional props; existing callers stay byte-identical.
  - A Group row: primary = the group label; secondary = "träff på {names}", at most 3 names (exact first), plus "och {n}
    till" with n exact; a row hit only through its label has no secondary line.
  - A variant: primary = the occupation name; secondary = "Yrkesgrupp: {group label}", the same text as the chip, so the
    selected state reads at the moment of choosing.
- **Word rows** are `.jp-popover-row` buttons with `aria-pressed` and `aria-controls`, a chevron, and the selection dot
  when any variant's group is selected. Secondary = "{n} yrkesgrupper" in words, derived on the frontend from the
  variants, which are uncapped, so n is exact (ADR 0120); never "(n)", which is PR 2's facet-count form. Activating one
  moves focus to the right column's heading (`tabIndex=-1`), which bounds the Tab path in a 50-row list and scrolls the
  heading into view for touch users.
- **No match highlighting.** The server returns no offsets, and highlighting on the client would re-run the rule in
  TypeScript, a second authority for it. **No "Välj alla" in search mode**: a "Välj alla 14" would make a Word row
  checkable in practice.

**The columns.** The left column is a `role="group"` named by its heading, "Träffar", with no count (the count lives in
the status region). The right column, while a Word row is open, is a `role="group"` named by its heading, the head word,
in `.jp-popover__title`; while none is open it holds only the idle line and has no heading to be named by.
- **Row 1 is opened by default** when it is an exact Word row or the only row. It is always row 1, and a Word row that is
  not row 1 is never auto-opened. Opening by default calls `onToggle` and `onReplace` zero times (tested).
- **Word rows exist but none is open:** two columns, with an idle line on the right.
- **No Word rows:** one full-width column in the same bordered box.
- **Below 560 px of container width** the columns stack, left list then right column, DOM order unchanged. A container
  query, not a viewport query, because the component sits in two hosts of different widths (the precedent is
  `admin.css:365`); the render decides whether the value holds. One scroller per column; no motion.
- The cap line is the last element of the left list and is not focusable.

**The degraded and empty states.** Pending keeps the box's reserved height and shows no rows from an earlier word. **No
hits, unavailable and 401 bring the browse cascade back into the same box** with no extra control, so there is no empty
box. The status text names the cause and the action. The 401 text reuses the wording already shipped in the section, "Du
är inte inloggad. Logga in och försök igen." (`matchPrefs.occupation.unauthorized`), so one cause reads the same way in
one dialog. The copy lives under `components.occupationSearch.*` with sv and en at parity.

**Escape.** Radix catches Escape on the document before any handler on the component can, so the hosts act: both call one
shared helper from `onEscapeKeyDown`, which calls `preventDefault()` when the event target is the component's search field
(marked with a data attribute) and that field holds text. `preventDefault` also cancels the browser's native search-field
clear, so the field's own key handler clears the query itself. **Escape with text clears the field; a second Escape closes
the host.** Tested in both hosts.

**The selected state.** Chips stay `labelsForSelected` over the groups. Variants that share a group are checked together
(*Projektledare, el* with *Projektledare, telekom*), and so are an Undersköterska Group row and its variant. After the
field is cleared the cascade shows the same groups checked. Only a Group row or a variant ever selects.

**Facet counts (PR 2)** go only on elements named by the group, never beside a variant's label as if they were the
variant's count (ADR 0120, ADR 0047).

### D6. `/foretag` is not adopted (CTO (c))

The component does not fit that surface. Its output is SNI leaf codes; the group is a single, required choice (ADR 0141
D8); the map is built per group, from its share of ads; and the word is typed inside the SNI filter. The resolver might
fit its candidate step ("Vilket yrke menar du?"), but that is a separate change-reason: it would amend ADR 0141 D7 and its
Consequences ("a word yields nothing until it is complete — accepted"), and it needs its own design round because a
14-group list goes into a single-choice block. **PR 1 amends nothing in ADR 0141.**

By #2198's own premise ("the gate is wrong where the user chooses"), the `/foretag` behaviour is a defect in delivered
code. **One issue is filed after PR 1 merges**, by the PR 1 session, **measured and dated first**: `projektledare` gives
no block on `/foretag`, taken from a test or a local stack and never from user data on the box. Because it is a defect in
delivered code it falls outside the session's net backlog cap (CLAUDE.md §9.6).

### D7. Everyday words (CTO (g))

Step 1 ships the plain no-hits state: the browse cascade returns (D5), and `chaufför` finds nothing because no v30 label
contains it. Two follow-ups are written as scheduling and make no truth claim: **`alternative_labels` (69 new curated
words) and a refresh of the v29-era occupation layer are not MVP scope, not verified.** They need a data-pipeline
change-reason (the generator passes no `version` today, and the refresh would also cover the 170 snapshot names with no
mapping). The PR 2 session files that issue when it closes #2198, so the backlog nets to zero. **`job-title` is rejected
for the picker:** JobTech says it is "not a synonym", it holds about 9.7k noisy entries (133 kB gzip), and a title that
points to many names is guidance, which would need row semantics of its own.

### D8. The 2026-07-28 bind is partly superseded (CTO (d); addendum, consolidated list 21)

`docs/reviews/2026-07-28-foretag-sok-pr5-bransch-form-cto.md` said `JobbFilterPopover` gets no filter field. **Its
conclusion is superseded for Yrke** by Klas's directive in #2198. **Its SRP principle stands, and it dictates the form:**
the field and the results belong to the shared component, not to the popover. PR 2 therefore gives `JobbFilterPopover`
**one optional composition prop** that carries nodes the host renders: the field above the columns, and a results node that
takes the body's place while it is not null. The popover holds no query state, no matching and no fetch. With the prop
omitted, Ort and `/foretag/sok` Ort stay byte-identical and their tests do not change.

## Measurements

**Handler timing, 2026-10-23.** Warm index, 200 calls per query, through `OccupationSearcher.SearchAsync` (the server-side
handler latency ADR 0045 Beslut 1 measures, not edge to edge):

| Query | p50 | p95 | Rows | Bytes |
|---|---|---|---|---|
| `chef` | 0.674 ms | 2.360 ms | 50 of 60 | 16,250 B |
| `in` | 0.362 ms | 0.582 ms | 50 of 63 | 12,058 B |
| `utvecklare` | 0.325 ms | 0.387 ms | 15 of 15 | 3,510 B |
| 98-character adversarial query (33 × `in`: every hitting label runs the AND to its end) | 0.645 ms | 0.809 ms | 50 of 63 | 12,058 B |

The class (b) budget is 150 ms p95 (ADR 0045 Beslut 1). The cost the user feels is the 300 ms debounce and the round
trip. Performance stays **observe-only** (ADR 0045 Beslut 5 and
6): the numbers are recorded and not asserted, and the test asserts only that the row count stays at most 50. The command
(the lines are in the TRX output):

```
dotnet test --project tests/Jobbliggaren.Api.IntegrationTests -- --filter-class "*OccupationSearchSweepTests" --filter-method "*Timing*" --explicit only --report-xunit-trx --results-directory <dir>
```

**The sweep, 2026-10-23** (`OccupationSearchSweepTests`, over the real seeded snapshot and the real searcher, expected
answers from the oracle): every mapped name (2,153 at v30, derived from the files and never hard-coded), typed in full,
lands in **row 1** and selects its own group; every head word of at least two characters reaches every name it heads; the
four names whose head word is a single character (*F&B Manager* and *2:e Styrman* among them) are reached by their longest
word. A query token is a prefix of itself, so the sweep only becomes a real test once a cap and an ordering exist: it
asserts what they must not break. If a name fails, the rule is fixed and the test is never relaxed. The derivation run
agreed: 2,153 of 2,153, with at most 7 rows for a full-label query (p50 1).

**The acceptance words** (integration tests): `projektledare` is one Word row with every variant naming its group;
`programmerare`, `mjukvaruutvecklare`, `backend`, `frontend`, `fullstack`, `lagerarbetare`, `produktägare` and `scrum` find
their group; `UX` finds *UX-designer* and never *vuxenutbildning*; `sjuksköterska` and `undersköterska` reach every compound
they head; `chaufför` gives none.

**Not measured:** the end-to-end latency a user perceives (RTT, the Next hop); the Next fetch-cache behaviour on 16.3.8;
the render matrix, which design-reviewer's verdict waits for (AGENTS.md §8 point 4).

## Alternatives considered

- **A. Inline the index in the picker pages (CTO (a), A).** Rejected. The tree plus the cheapest index shape measures
  32,592 B gzip against ADR 0045's 30,720 B document budget, before any other byte on the page, and `/jobb` would resend it
  with every `?_rsc` filter navigation.
- **B. A lazy client index with the matching in TypeScript (B1 to B3).** Rejected. It makes the browser a second reader of
  the frozen crosswalk and puts the rule in the one layer that #2199 and a later `/foretag` cannot share. **ADR 0141's
  rejected D3 B is upheld on its "two authorities for one question, diverging by construction" ground**; its Snowball
  ground ("cannot be ported") does not apply to a rule without stemming. B1 ships a second copy of a migration-owned
  resource in the frontend build; B2 and B3 need a long-cache policy the house has never granted (the tree read says
  "never `public`").
- **C. A server search endpoint over the deriver's index (chosen).** It matches ADR 0042 Beslut C and AGENTS.md §4, sends
  the client answers and never the substrate (Evans 2003, ch. 14), and keeps one reader of the crosswalk.
- **One engine with two modes, or the deriver itself.** Rejected (D2): a flag argument, and the deriver's gate is wrong
  where the user chooses.
- **A separate index over group labels (ADR 0141 D2 M1).** Rejected (D2): a third matcher; group labels are matched inside
  the shared index instead.
- **Reuse `taxonomy-read` or `occupation-divisions` as the bucket, or cache the answer with an ETag or `max-age`.**
  Rejected (D1): one starves the tree read, the other is another surface's bulkhead, and a cache would store each term.
- **The fetcher beside `getTaxonomyTree`.** Rejected (D1): a cached fetch stores the term.
- **A literal `role="combobox"` with `aria-haspopup="tree"` and `role="tree" aria-multiselectable="true"`, Enter toggling.**
  Rejected by Klas's E1 (D5). It stays the conformant fallback; its price is a manual NVDA and VoiceOver pass before the
  design verdict.
- **Match highlighting, a static hint, and "Välj alla" in search mode.** Rejected (D5).
- **A backend-only PR 1, or one PR for both hosts.** Rejected (CTO (d)): an endpoint with no consumer breaks ADR 0042
  Beslut F's discipline, and one PR means two design rounds and two hotspot sets, with the bind question belonging to the
  popover diff.
- **Adopting `/foretag` now, `job-title` as a source, `alternative_labels` now.** Not adopted (D6, D7).

## Consequences

### Positive
- A user who types the word they think in reaches its group, with the matching names as evidence, and sees which group a
  variant selects before choosing it. Every acceptance word reaches its group, and every mapped name typed in full is row 1.
- The pickers' contracts are untouched. No variant can be persisted, so the selected state is the group by construction.
- One index, one reader of the crosswalk, and a rule that is a pure function with unit tests on real v30 labels, so the
  rule can be changed without a database.
- The term's non-retention is executable (the canary, the configuration fact, the architecture pin), and the bucket is
  pinned to its route.
- The claim at `OccupationGroupMappingLoader.cs:15`, which cited an ADR 0043 amendment-note that did not exist, becomes true.

### Negative and risks
- **The typed word reaches our own server**, where it is neither logged nor persisted (D1). The property holds only while
  the three tests stay green and no new layer logs or caches the request URL.
- **Speed.** Rows arrive about 300 ms (debounce) plus the round trip plus the handler after the last keystroke, against
  about 0.1 to 0.5 ms for the handler alone.
- **Results jump at the last letter.** `sjuksköters` gives 3 names and `sjuksköterska` gives 24; `ingenjö` 5 and `ingenjör`
  115. The end arm fires only once the whole word is typed. This is inherent to the rule and honest, and it is not
  engineered around.
- **The Undersköterska duplication.** A Group row and a variant can point to the same group (79 of 2,722 whole-token
  queries; `chef`, `arbetsledare`, `civilingenjör`, `forskare` and `konsult` among them). Both take their checked state from
  the group set, so they cannot disagree.
- **The hero typeahead diverges.** It keeps `SuggestByPrefixAsync`'s whole-label `StartsWith` (index #3) and is out of
  scope, so the same word can behave differently there.
- **Suspended-hyphen labels** (*Mjukvaru- och systemutvecklare*) leave a bare token `mjukvaru`, so `mjukvaruutvecklare`
  reaches its group through the occupation-name and never through the label. Not a sweep failure.
- **170 names have no mapping** and cannot be rows. `/foretag` keeps the deriver's behaviour until its issue is resolved.
- **The collation depends on ICU** in the runtime image (D3); an image without it fails loud on first use.
- **The rate-limit numbers** are a floor: they are raised after evidence and never lowered after shipping.

## Implementation

**Delivered at `d68279f8c` (the backend).** The full solution run on that head passed on 2026-10-23: total 31,702, failed 0,
skipped 5.
- **Infrastructure** (`Taxonomy/`): `OccupationNameIndex.cs`, `OccupationSearchRule.cs`, `OccupationSearcher.cs`;
  `OccupationCodeDeriver.cs` now reads the index; `OccupationGroupMappingLoader.cs` (the false clause deleted);
  `DependencyInjection.cs` registers the index and the searcher.
- **Application** (`JobAds/`): `Abstractions/IOccupationSearcher.cs`, `Abstractions/OccupationSearchResult.cs` (the result,
  `OccupationSearchRowKind`, `OccupationSearchRow`, `OccupationSearchVariant`), and `Queries/SearchOccupations/` (query,
  handler, validator).
- **Api:** `Endpoints/JobAdsEndpoints.cs` (the route), `RateLimiting/RateLimitingOptions.cs` and
  `RateLimitingExtensions.cs` (the policy).
- **Tests:** `OccupationSearchRuleTests`, `OccupationSearchRowTests`, `SearchOccupationsQueryHandlerTests` and
  `SearchOccupationsQueryValidatorTests` (Application.UnitTests); `OccupationSearchSweepTests`,
  `OccupationNameIndexEquivalenceTests` with `OccupationNameOracle`, `SearchOccupationsEndpointTests`,
  `OccupationSearchRateLimitTests` and `OccupationSearchRateLimitOptionsTests` (Api.IntegrationTests);
  `OccupationSearchLayerTests` and `ApiRequestLoggingConfigurationTests` (Architecture.Tests, which now links the Production
  appsettings overlay); the deriver's test construction sites.

**The web half** (`web/jobbliggaren-web/`):
- **Data access:** `src/lib/dto/occupation-search.ts` (the zod `discriminatedUnion`), `src/lib/api/occupation-search.ts`
  (the server-only fetcher, `authedFetch` and `URLSearchParams({ q })`), `src/app/api/me/occupation-search/route.ts` (the
  GET route handler), `src/lib/occupation-search/resolve-occupation-search.ts` (the client resolver) and
  `src/lib/hooks/use-occupation-search.ts` (the hook).
- **Components:** `src/components/occupation-search/occupation-search-field.tsx` (the field and its status region),
  `occupation-search-results.tsx` (the rows and the columns) and `keep-host-open.ts` (the Escape helper), placed by
  `OccupationSection` in place of the `filterOptions` branch. `CheckItem` (`section-helpers.tsx`) gained an optional
  secondary line; its one-line form is unchanged. Both hosts (`match-preferences-dialog.tsx`, `match-setup-rail-modal.tsx`)
  pass the Escape helper to `onEscapeKeyDown`.
- **Fixes in existing code, by deletion or code only:** the inset focus ring gains `.jp-wizard--rail` (WCAG 2.4.7);
  `.jp-occpicker__cta` gets a 44 px minimum at 768 px and below; the right cascade column gets `role="group"`; the dead
  `[role='option']` selector goes; the `filterOptions` re-export in `match-preferences-card.tsx` goes; the comments in
  `lib/dto/taxonomy.ts` that said the frontend never consumes occupation-names are corrected by deletion (where names do
  reach the frontend is recorded in ADR 0067's amendment, not in a code comment).
- **Copy and CSS:** `components.occupationSearch.*` in sv and en; `matchPrefs.occupation.filterLabel` and `noMatch`
  deleted. New CSS is in `src/app/(app)/app.css`; the container query sits on the picker panel, so the browse cascade
  stacks below 560 px of container width as well.

**The record:** this ADR; the amendments to ADR 0043 and ADR 0067 of 2026-10-23; one edit in BUILD.md §10.6, which keeps
occupation-names (yrkesbenämningar) Swedish in every locale as register data, for the reason it gives for place and group
names (ADR 0137 is local, so its annotation is made in the main copy and not in the PR); and the index row in
`docs/decisions/README.md`.

**Constraints.** No new library or package, test packages included (BUILD.md §3.1). No migration (db-migration-writer is not
triggered). `hotspot:di` claimed for the Infrastructure DI root and the rate-limit registration; `hotspot:i18n` taken from
#1977 (Klas, 2026-10-23); `hotspot:css` only if `globals.css` has to change. The deploy rides standing GO A2 (ADR 0154)
with no `.env` change and no box write.

**Mandatory agents, PR 1** (CLAUDE.md §9.2): `code-reviewer` and `dotnet-architect` (more than 5 files, architectural
choices, a BUILD.md spec edit); `design-reviewer` (an a11y veto, ruling against the render matrix: hosts H1, the Matchning
dialog, and H2, the onboarding rail; states S0 to S14; the listed viewports; forced dark; en; the computed-DOM
measurements); `test-writer` (a new handler and port); `security-auditor` (triggered: a new authenticated endpoint carrying
user-typed text, new rate-limit numbers, the Caddy `q` dependency, and the non-retention claim; she rules on the final
diff). `senior-cto-advisor` has ruled.

**PR 2 (a new session, which closes #2198):** `/jobb` Yrke. `JobbFilterPopover` gains the one composition prop (D8), with
the field in the `__boolaxis` slot and focus to the field on open (WCAG 2.4.3); Escape follows D5; the right column is a
named `role="group"` with no "Välj alla" while searching; "(n)" appears only on Group rows; `bransch-popover.tsx` point 2
("no filter field") becomes false and PR 2 rewrites it; the ADR 0055 amendment (Beslut 1's columns hold words and variants
in search mode); render at 1280×720, where the field, 60vh and the footer have to fit under the hero pill, and at 3440. It
reuses this endpoint and bucket, the term never enters the `/jobb` URL (D1), and it files the `alternative_labels` issue
(D7). Mandatory agents: `code-reviewer`, `design-reviewer`, and `dotnet-architect` if it touches more than 5 files;
`security-auditor` is not triggered unless the request surface changes.

**Issues:** the `/foretag` occupation step, filed by the PR 1 session after merge (D6), labels `area:frontend`, `P2`,
`BE+FE`, `mvp`; and the synonym-source issue, filed by the PR 2 session (D7), labels `area:matching`, `area:jobads-cv`,
`P3`, `BE`, `mvp` (a test user meets the no-hits state, and ties resolve toward labelling, CLAUDE.md §6.5).

## References

- **The plan reads (local, ADR 0072):** `docs/reviews/2026-10-23-2198-plan-cto.md` (the ruling, (a) to (j));
  `docs/reviews/2026-10-23-2198-plan-cto-addendum.md` (points A to AD, which win where they differ);
  `docs/reviews/2026-10-23-2198-plan-dotnet-architect.md`; `docs/reviews/2026-10-23-2198-plan-security-auditor.md`;
  `docs/reviews/2026-10-23-2198-plan-design-reviewer.md`; `docs/reviews/2026-10-23-2198-map-measure-matching.md` (the
  measurements behind D3); `docs/reviews/2026-10-23-2198-map-adrs.md`.
- **The two earlier rulings (local):** `docs/reviews/2026-07-28-foretag-sok-pr5-bransch-form-cto.md` (the bind, D8) and
  `docs/reviews/2026-09-14-1682-form-cto.md` (ADR 0141's form ruling).
- **ADRs:** 0018, 0040, 0042, 0043, 0045, 0047, 0055, 0067, 0120, 0134, 0137, 0141, 0144; also 0154 (the deploy) and 0072.
- **Standards, read 2026-10-23:** W3C WAI-ARIA Authoring Practices, combobox and listbox patterns
  (w3.org/WAI/ARIA/apg/patterns/combobox/ and …/listbox/); WAI-ARIA 1.2 (w3.org/TR/wai-aria-1.2/). External links can rot.
- **Books:** Martin, *Clean Architecture* (2017), ch. 7, 10, 13, 22; Martin, *Clean Code* (2008), ch. 3; Fowler,
  *Refactoring*, 2nd ed. (2018); Evans, *Domain-Driven Design* (2003), ch. 14; Hunt/Thomas, *The Pragmatic Programmer*
  (1999); Feathers, *Working Effectively with Legacy Code* (2004); Winters/Manshreck/Wright, *Software Engineering at
  Google* (2020), ch. 12; Cohn (2009); Nygard, *Release It!*, 2nd ed. (2018).
- **Spec:** CLAUDE.md §6.5, §9.2, §9.6, §11; AGENTS.md §2.4, §4, §5, §8, §12.

---

## Amendment 2026-10-23 — PR 2 (#2198): the Escape rule for the one host that is not Radix, the idle right column has a heading, D7's follow-up issue carries `mvp`, the D1 carry and the D8 byte-identity claim are met, and `revealColumn` scrolls only as far as it needs

**Status:** Accepted 2026-10-23. **Source:** `senior-cto-advisor`'s ruling for #2198 PR 2, (a), (b), (c), (f), (g), (j), (k) and (l) (local, ADR 0072, so every operative point is restated here), after plan reads by `code-reviewer`, `design-reviewer` and `security-auditor`; Klas, 2026-10-23: "Ja, predikatet (Rek.)". Additive: the text of D1 to D8, the Measurements and the Implementation section is untouched. On the seven points below this amendment is the operative text. **PR 2 closes #2198.**

1. **D5, Escape: the mechanism for the non-Radix host.** D5's Escape paragraph is the rule for the two Radix hosts, the Matchning dialog and the onboarding rail, and it holds for them: Radix catches Escape on the document in the capture phase, so each host's `onEscapeKeyDown` calls `keep-host-open.ts`. That file stays theirs. It is unchanged, and the `/jobb` popover does not use it.

   `JobbFilterPopover` is not a Radix component and has no `onEscapeKeyDown`. It is dismissed by `useDismissable`'s `keydown` listener on the document, which runs in the bubble phase, after the field's own handler, and by then the field may already be empty. A helper that reads the field's text cannot hold this host open.

   The mechanism: `useDismissable` takes an optional fourth argument, `keepOpenOnEscape?: (event: KeyboardEvent) => boolean`. When it returns true for an Escape, the hook neither calls `onClose` nor moves focus. Omitted, every Escape closes as before, so every other consumer is unchanged by construction, and a mousedown never consults it. Only `JobbFilterPopover` passes it, and only when `search` is present: `e.defaultPrevented && <the search slot>.contains(e.target)`. The scoping to the slot is what makes `defaultPrevented` readable: the field calls `preventDefault` on an Escape only when it holds text, and it clears itself, so a cancelled Escape from inside the slot means the field has handled it. An Escape from a row, a column or the footer is outside the slot and closes at once. The predicate has a stable identity (`useCallback`, no changing dependency). The hook's effect depends on it, and a predicate that changed with each keystroke would re-subscribe the document listener in the middle of the Escape dispatch, where a listener removed during dispatch is skipped (DOM Standard), so the popover would stay open by accident and not by rule. A global `if (e.defaultPrevented) return` in the hook was rejected: Radix calls `preventDefault` when it dismisses its own layer, so it would change Escape for every consumer whose panel can hold a Radix layer.

   The outcome is D5's, in the Yrke popover: Escape with text clears the field and keeps the popover and focus; Escape on an empty field closes the popover and focuses the Yrke pill; Escape from a row closes it with one press. Klas's prompt had named `keep-host-open.ts` on the popover's `onEscapeKeyDown`. The behaviour he asked for is delivered unchanged and the mechanism differs, which he approved on 2026-10-23.

   Tests: `lib/hooks/use-dismissable.test.tsx`, "useDismissable — keepOpenOnEscape (#2198)" (the default path still closes and focuses the trigger on an Escape the real `OccupationSearchField` cancelled; true keeps the panel open with focus where it is; false closes; a mousedown never consults the predicate), and the Escape describe in `components/job-ads/jobb-yrke-popover.test.tsx`. Red runs: removing the guard fails "returning true keeps the panel open and leaves focus where it is", and a browser run with the predicate argument removed shows the first Escape closing the popover; the PR body records it.

2. **D5, the idle right column has a heading.** D5 says that while no Word row is open the right column "holds only the idle line and has no heading to be named by". That does not describe the delivered code, which PR 1 shipped and PR 2 left as it was: the idle column has a heading, "Yrkesgrupper" (`components.occupationSearch.groupsHeading`), in `.jp-popover__title`, and it is a `role="group"` named by it, as it is when a Word row is open. The heading is not a focus target. The `tabIndex=-1` heading that D5 moves focus to belongs to the open Word row's column. Read D5 with this correction.

3. **D7, the follow-up issue is `mvp`.** D7 says that `alternative_labels` and a refresh of the v29-era occupation layer "are not MVP scope, not verified". The Implementation section of the same ADR lists the issue with `mvp`, and the label is the one that holds. Under CLAUDE.md §6.5's criterion ("a real test user meets it, or it blocks going live") a test user meets the no-hits state that `chaufför` gives, the state D5 and D7 ship, and ties resolve toward labelling. D7's "not MVP scope" clause is superseded for scope. "Not verified" stands: it makes no truth claim, and the issue measures first and dates its claim (`chaufför` finds no row, from PR 1's acceptance run of 2026-10-23) before it asserts anything. `P3` is unchanged, because `mvp` is scope and `P3` is severity (§6.5). The PR 2 session files the issue when it closes #2198, so the backlog nets to zero (D7). Labels: `area:matching`, `area:jobads-cv`, `P3`, `BE`, `mvp`.

4. **D8, the byte-identity claim stays exact.** D8 says that with the prop omitted, Ort and `/foretag/sok` Ort stay byte-identical and their tests do not change. PR 2 holds that, and it also changes things about Ort that are not its DOM. First, every row in a `.jp-popover` takes an inset focus ring (`.jp-popover .jp-checkitem:focus-visible` and `.jp-popover .jp-popover-row:focus-visible`), because the scrolling columns clip the global ring on browse rows; it is the inset ring the Matchning dialog and the rail already take. Second, the results footer ("Visa N annonser") now closes the open popover and returns focus to its pill, in Ort, Yrke and Filter; measured in Yrke on `origin/main`, it left focus on `<body>` (WCAG 2.4.3), and it is the same button in the other two. The first changes how Ort looks when focused, the second changes where focus goes after its footer, and neither changes its markup, so the claim, which is about the DOM, is exact.

   The proof was a one-off characterization: a hash of the open Ort popover's `outerHTML` through both real hosts (`JobbHeroFilters` with a region active, a municipality selected and counts loaded; `ForetagSokSearchbar` with a län active), `useId` tokens normalised, base constants read on `origin/main` `515b7424b`. It was green at every commit of the PR and is deleted by the PR's last code commit: kept permanently, an opaque hash fails on legitimate popover work without saying why (Winters et al. 2020, ch. 12). The permanent guards are the Ort case in `jobb-yrke-popover.test.tsx` ("Ort opens without moving focus and without a search"), `occupation-search-css.test.ts` (`.jp-popover` joined its host list), `jobb-hero-filters-footer-focus.test.tsx`, and `jobb-hero-filters.test.tsx` and `foretag-sok-searchbar.test.tsx`, whose diff in this PR is empty.

5. **D1, the carry to PR 2 is fulfilled.** "The Yrke term never enters the `/jobb` URL" is met. `JobbYrkePopover` (`components/job-ads/jobb-yrke-popover.tsx`) owns the typed word as component state and composes PR 1's `OccupationSearchField` and `OccupationSearchResults` into `JobbFilterPopover`'s `search`. `JobbHeroFilters` renders it under Yrke's existing key, so the word starts empty on every opening whatever closed it, and closing aborts a request in flight. `JobbHeroFilters` gains no state, and the word is never in the scope where `buildJobbHref` is called. A pick leaves as group ids through the same `onChange` a browse pick uses. The field has no `name` and `autoComplete="off"`. No new URL key is added and `q` is not reused, which were the two things D1 said would re-trigger security-auditor. The only request that carries the word is the search's own `q`.

   Tests, in `components/job-ads/jobb-yrke-popover.test.tsx`: "/jobb Yrke: the typed word stays in the popover (#2198 PR 2, ADR 0180 D1)" (a Group-row pick and a variant pick each push the href with only the group axis changed; no push, fetch, URL, storage or cookie carries a canary word in any encoding; typing, Enter, both Escapes, closing and reopening push nothing; the field has no name, has autocomplete off and starts empty whatever the hero search holds; a search pick and a browse pick of the same group push the same href), and "/jobb Yrke: the word's lifetime" (after each of five close paths, Escape, an outside click, the footer, the pill and another pill, reopening shows an empty field and the browse columns; a pick keeps the popover open with the word, the answer and focus on the row).

6. **D5, `revealColumn` scrolls `nearest`.** D5 says that activating a Word row moves focus to the right column's heading, which scrolls it into view for touch users. That holds, and the heading still takes focus. What changed is how the column is scrolled: `revealColumn` now calls `scrollIntoView({ block: "nearest" })` where it called `{ block: "start" }`, for every host, because it is the one home of that knowledge. Measured 2026-10-23 at 1280×720 on a local build, in `/jobb`'s Yrke popover, `start` scrolled the window until the column's top reached the scroll padding and put the search field under the sticky header. A column already in view no longer moves the page. A column partly out of view is brought in by the least distance, so when the columns stack in the dialog and the rail the whole opened column shows. PR 1's two assertions on the argument are updated, with the reason: "scrolls the opened column into view only as far as it needs" in `occupation-search-results.test.tsx`, and the browse test in `occupation-section.test.tsx`, which now reads "rullar fram kolumnen".

7. **The PR 2 line, and what it delivered.** The Implementation section's PR 2 paragraph says "the field in the `__boolaxis` slot". Read that as a position: the field's slot sits where the boolean row sits, between the popover's root and its body. Its class is a new `.jp-popover__search` in `app.css`, not `.jp-popover__boolaxis`, and `search` and `booleanAxis` exclude each other in the prop's type, since no host passes both and nothing designs the combination. D8's composition prop is `search?: { field, results }`. Both are `ReactElement`s the host builds, and `results` is `null` to show the browse columns; while it is not null it renders in `.jp-popover__results`, in the body's place.

   D5's closing sentence on facet counts is met. `CheckItem` gains an optional `count` and `OccupationSearchResults` an optional `counts`, with the popover's own `occupationGroupCounts` as the only source (a loaded map with a missing key shows "(0)"; `null` or omitted shows no number). The count is on Group rows only, never on a Word row or a variant, and it is part of the row's accessible name in both of `CheckItem`'s forms. With the props omitted both render byte-identically, which the characterization of point 4 also covered (its file hashes `CheckItem` and `OccupationSearchResults` without counts). `bransch-popover.tsx` point 2 ("no filter field") became false, and its false clause is deleted, with a pointer to D8. The ADR 0055 amendment of the same date records the popover's search mode.

   PR 2 changes no popover position. A filter popover's overflow past the viewport, measured 2026-10-23 on `origin/main` before this PR (the Yrke popover's right edge 45 px past a 1280 px viewport, and past phone widths), is a defect in delivered code. It goes to its own follow-up PR, which Klas placed directly after PR 2.

---

## Amendment 2026-10-24 — On /jobb a variant carries its occupation-name id and a variant tick will filter on it with an exact "(n)": D4's "no occupation-name concept id on the wire" and D5's ban on counts beside a variant are re-taken for /jobb only (#2199 PR 1, ADR 0067)

**Status:** Accepted 2026-10-24. **Source:** `senior-cto-advisor`'s ruling for #2199, A (ii), A (iii) and A (iv) (local, ADR 0072, so every operative point is restated here); Klas, 2026-10-24, AskUserQuestion: "Ja, enligt förslaget (Rek.)" for the variant tick, "Bara inom samma grupp (Rek.)" for a deprecated name with several replacements, "PR 1, med ADR 0180-tillägget (Rek.)" for the variant id and this amendment landing in PR 1, and "Ja, bara de tvingade (Rek.)" for the frontend lines PR 1 carries. Additive: the text of D1 to D8, the Measurements, the Implementation section and the Amendment 2026-10-23 is untouched. On the points below this amendment is the operative text, **for /jobb only**. The rule, the artefact and the measurements are in [ADR 0067's Amendment 2026-10-24](./0067-platsbanken-search-parity.md).

1. **D4, a variant carries its occupation-name id.** D4 said "No occupation-name concept id goes on the wire", so a variant could not be stored and the chip was the group by construction. That is re-taken: `OccupationSearchVariant` now carries `OccupationNameConceptId` (`occupationNameConceptId` on the wire), delivered in this PR. D4's "each variant names and selects its own group" now holds for Matchning and the onboarding rail only. On /jobb a variant tick will select its own occupation-name: the API filter takes the id as `?occupationName=` and resolves it as ADR 0067's amendment sets out. Still group-only are the Group row, a Word row (point 3), and, until #2199 PR 2, `SearchCriteria`, saved and recent searches and the match profile, which pass the axis empty. Until PR 3 the frontend does not use the id: the zod schema in `lib/dto/occupation-search.ts` does not parse it, so every host still selects a group. PR 1's frontend lines are the forced ones only (Klas: "Ja, bara de tvingade (Rek.)"): that file's comment, which said no name id is on the wire, is corrected, and `"OccupationName"` is added to `FACET_DIMENSIONS` in `lib/dto/job-ads.ts`.

2. **D5, a variant gets an exact "(n)" on /jobb.** D5's "Facet counts (PR 2) go only on elements named by the group, never beside a variant's label as if they were the variant's count" is reversed for /jobb. It banned a variant count because that count would have been its group's; that premise is gone, since a name pick is now a filter of its own. The count is `FacetDimension.OccupationName`, folded from the ads' (name, group) pairs with the same `Resolve` the filter uses, so the filter and the count cannot disagree and the number is exact, as ADR 0120 requires of a rendered count (CTO A (iii)). A count is needed because 638 of the 2,153 names have no ad (724 under the no-spill rule) and the median among names with ads is 5, so a precise tick without a count is a trap. Group rows keep their count and now stand beside the variants' counts as the broader choice; one help line says a yrke means the employer chose it, and design-reviewer owns that copy (CTO A (iv)). Wiring the "(n)" into the Yrke popover is PR 3.

3. **Unchanged: the hosts that select a group, the Group row and the Word row.**
   - **Matchning and the onboarding rail.** A variant there still selects its group (ADR 0040 Beslut 4). The shared component carries no meaning of its own and each host maps the pick (D8; CTO A (ii)).
   - **The Group row** stays equality on the ad's own group. A selected group includes its variants, which show as included and not as separately selected (CTO A (ii)). The checked state per host is PR 3's.
   - **The Word row stays unselectable and gets no "(alla)"** in this scope. "(alla)" would be honest for *Projektledare* (646 ads, 74% with a project-manager title, against 11% for today's union of groups). It would mislead wherever the word is a compound ending: the *Chef* row has 4 names and 10 ads against 1,923 titles containing "chef", which fails Klas's requirement that the fix hold "för alla andra yrkesgrupper, och olika områden" (#2199).
   - **No synonym clusters.** Curated clusters across 2,153 names would be a second authority over the employer's own coding and could not be kept up in every field (Speculative Generality, Fowler 2018, ch. 3). The 838 developer-titled ads spread over *Systemutvecklare/Programmerare* 295, *Mjukvaruutvecklare* 282, *Backend* 67, *Fullstack* 66 and more, and the UI discloses that through the counts and the Group row instead (CTO A (iv)).
   - A Word-row "(alla)" and curated synonym clusters are written as scheduling and make no truth claim: **not MVP scope, not verified.**

---

## Amendment 2026-10-24 (#2212, ADR 0184) — D8's delta is measured: the anchored DOM differs from the base in style attributes only; the stacked answer has one scroller in a sheet; the Ort guard test applies to the anchored placement

**Status:** Accepted 2026-10-24. **Source:** [ADR 0184](./0184-the-filter-popovers-are-placed-by-one-pure-policy-and-one-shell-anchored-inside-the-viewport-a-sheet-on-a-narrow-or-short-viewport-one-focus-table-and-closing-when-focus-leaves.md) D1, D6, D7, D9 and D10 (#2212, the follow-up pull request that the amendment of 2026-10-23 sent the popover overflow to), and the readings of 2026-10-24 below. Additive: the text of D1 to D8, the Measurements, the Implementation section and the amendment of 2026-10-23 is untouched. On the three points below this amendment is the operative text.

1. **D8's delta, measured 2026-10-24.** D8 says that with the composition prop omitted, Ort and `/foretag/sok` Ort stay byte-identical, and point 4 of the amendment of 2026-10-23 holds that claim to the DOM. From #2212 on the claim reads as follows, and it is measured. Anchored, Ort's normalised `outerHTML` on `/jobb` and on `/foretag/sok` at 1280×720 equals the base `5ad51c9c1`'s once style attributes are stripped. The only style changes are two. The first is the root's inline `top`, `left` and `width`: the left is clamped where the panel would cross the gutter, and at 1280×720 Ort's is unchanged, at 664 and 416 px. The second is the two columns' inline `max-height: 60vh; overflow-y: auto;`, which is now one `app.css` rule with the same value when anchored (ADR 0184 D9). In a sheet, `data-placement`, the `hidden` attribute, the band and the drill-down markup exist, and only in that placement (ADR 0184 D1, D9, D10). `JobbFilterPopover` gained no public prop: D8's one composition prop, `search`, stands (ADR 0184 D6).
2. **D5 in a sheet.** The stacked answer (D5, the container query below 560 px) has one scroller in a sheet: the stacked columns lose their caps (ADR 0184 D9). Outside a sheet D5 is unchanged.
3. **The Ort guard test.** The guard that Ort opens without moving focus (`jobb-yrke-popover.test.tsx`, "Ort opens without moving focus and without a search", named in point 4 of the amendment of 2026-10-23) applies to the anchored placement. In a sheet every host moves focus into the dialog, Ort included (ADR 0184 D7).

## Amendment 2026-10-25 — /jobb ticks an occupation-name by its own id, whichever row carries it: a Group row's evidence names carry their ids and open beside the group's checkbox, a yrke whose group is selected is included, and the right column reads "Yrken" (#2199 PR 3, ADR 0067)

**Status:** Accepted 2026-10-25. **Source:** `senior-cto-advisor`'s ruling of 2026-10-25 on the Group-row gap, rulings 1–3; `design-reviewer`'s plan read and its delta of 2026-10-25 (presentation B); `dotnet-architect`'s delta of 2026-10-25 (the wire type) — all local, ADR 0072, so every operative point is restated here. Additive: D1 to D8, the Amendments 2026-10-23 and 2026-10-24 are untouched in their text. On the points below this amendment is the operative text, **for /jobb only**; Matchning and the onboarding rail are byte-identical (ADR 0040 Beslut 4).

1. **D4, a Group row's evidence names carry their ids.** `OccupationSearchRow.MatchedNames` is a list of `OccupationSearchMatchedName(OccupationNameConceptId, NameLabel)` in place of plain strings; the wire key stays `matchedNames`, each entry `{occupationNameConceptId, nameLabel}`. The rule is unchanged: grouping by head word, the tiers, the order, the 50-row cap and `ForWord`'s two-group invariant. The id is the index's own (`OccupationNameEntry.ConceptId`), the one PR 1 put on a variant. The server does not cap the list; how many a host spells out is the host's. Matchning and the rail read `nameLabel` for "träff på …", so their markup does not change.
   - **Why:** under D3 a head word whose names sit in one group puts them on that group's row as evidence. At least 1,676 of the 2,153 v30 names (77.8%) can therefore never be a Word-row variant, and a name typed in full lands in a Word row for 13 names only (measured 2026-10-25). Without the ids the exact tick of the Amendment 2026-10-24 would hold for a fifth of the names, and "Projektledare, IT" typed in full would still select its whole group.
   - **Deploy skew, both actors named.** The previous release's API answers with plain strings, which the new schema refuses; the previous web refuses the new objects. During a forward recreate (old web, new api) and in a tab opened before the release, the search reads as D5's "unavailable" until the containers agree or the tab navigates. No union of both shapes is accepted: it would buy an untestable path for a window of seconds.

2. **D4/D5, on /jobb a name is ticked by its own id.** One rule for a variant and an evidence name: checked when its id is selected; **included** when its group is selected — `aria-checked="true"`, `aria-disabled="true"`, still focusable, inert to click, Space and Enter, its second line "Ingår i vald yrkesgrupp: {group}", a tonal `--jp-accent-50` box with a `--jp-accent-700` border and tick, its label never dimmed. A selected group includes its names, which are not separately selected (the Amendment 2026-10-24, point 3). A group pick does not remove names inside it: a name's group is known only for the names on screen, and a sole successor can reach ads outside its group. Each name shows its own exact "(n)" from `FacetDimension.OccupationName`, "(0)" for a known zero, nothing when the counts failed (ADR 0120).

3. **D5, the Group row's opener.** A Group row whose hit names occupations stays the group's checkbox, unchanged, with a sibling button after it: "{n} yrken" and a chevron, `aria-pressed`, `aria-controls` on the right column, named "{n} yrken i {group}". No control sits inside another (the `criterion-tree.tsx` precedent). Pressed, it has a `--jp-accent-50` fill, `--jp-accent-700` text and a 4px `--jp-accent-700` bar on its leading edge, so its state never rests on colour (WCAG 1.4.1); at ≤768px or a coarse pointer its hit area is 44 × 44. It shows the Word row's dot only when one of its names is picked by its own id. A Group row reached only by its label stays a bare checkbox.

4. **D5, columns and default open.** Two columns whenever any row can open (a Word row, or a Group row with names); one column only when none can. Row 1 opens by default when it can open and holds the exact word or is the only row; opening by default moves no focus and selects nothing. Opening any row moves focus to the heading under the band, as a Word row's did. Each row has at most two Tab stops and the 50-row cap is unchanged, so the left column holds at most 100 stops ("chef" holds 89).

5. **D5, the right column on /jobb.** Its band reads "Yrken" in every state. Idle, it says "Öppna en träff för att se dess yrken." (Matchning and the rail keep `chooseWord`). Open, the row is named under the band in `.jp-popover__grouphead` — the head word or the group label, the focus target — followed by a hint that describes the column: under a Word row "Kryssar du i ett yrke visas annonser där arbetsgivaren angett just det yrket. Sök på yrkesgruppen för fler.", under a Group row its first sentence only. A name in the column has no second line unless it is included.

6. **D5, the status region.** While the field is idle on /jobb and occupation-names are selected, the field's own status region says "{n} valda yrken visas bland de aktiva filtren, inte här." (singular "# valt yrke …"): the browse columns cannot show a name, and the chips are the one path that is always true, also for an id the taxonomy no longer carries. The header's "Rensa" shows whenever a group or a name is selected, and clears both in one commit.

7. **The counts' fetch.** The occupation-name counts are requested by the /jobb host only once an answer in the opening holds a name a tick could select, and then once per opening and per Yrke tick: the answer has about 1,500 keys and does not depend on the typed word. NBomber `facet_counts_occupation_name` measures the request (ADR 0067's amendment of this date).
