# ADR 0190 — An indexable `/junior`: only its default view is `index, follow`, and only while a floor computed per request holds; the line that follows is fixed in order, with filters on `/lediga-jobb` by Klas's override, and never free text or an employer key

**Date:** 2026-11-22
**Status:** Accepted 2026-11-22 (Klas's eight answers of the same day, D6 below, on the plan reads of `senior-cto-advisor`, `security-auditor` and `dotnet-architect`). The ADR 0166 amendment of the same date, which carries the legal outcome, is Proposed until `security-auditor` signs it at PR 1's final head, and PR 1 does not merge before she has (ADR 0154: a merge is a deploy). The mandatory agents review the delivered diff in the PR.
**Deciders:** Klas Olsson (controller and product owner), by AskUserQuestion on 2026-11-22, verbatim in D6 ·
`senior-cto-advisor` (plan read G2, `docs/reviews/2026-11-22-2293-plan-senior-cto-advisor.md`, marked "(CTO F1)" and so on; a scoped follow-up on three points, `…-plan-senior-cto-advisor-followup.md`, marked "(CTO follow-up 1)" and so on; and the ruling on the floor's latency, `…-floor-latency-senior-cto-advisor.md`, marked "(CTO latency ruling)"; all report-only, local) ·
`security-auditor` (plan read G1, `…-plan-security-auditor.md`: 0 Blocker, 6 Major, 3 Minor, marked "(security-auditor M1)", "(security-auditor S1)" and so on; the floor-key ruling, `…-floor-key-security-auditor.md`, marked "(security-auditor Ruling 1)" and "(Ruling 2)"; and her scoped reading of the junior bucket's cost, `…-bucket-cost-security-auditor.md`, whose outcome is the section *The junior bucket* below) ·
`dotnet-architect` (plan read, `…-plan-dotnet-architect.md`: 0 Kritiskt, 6 Viktigt, 2 Nice-to-have, marked "(architect Viktigt 1)" and so on)
**Amends:** ADR 0064 (rule (d): a third class and its bound), ADR 0186 (D5, D8, *What is not measured*, *Consequences*), ADR 0174 (D3: the pages part holds 16 URLs), ADR 0106 (the D9 read-path table gains the floor's read) and ADR 0176 (D9: the page gains one plain link to `/junior` under its band; D14's trigger 2 is read for the indexable `/junior`), each by its dated amendment of 2026-11-22. ADR 0166 gets the amendment that carries the legal outcome. Nothing above those amendments is rewritten, except four pointers marked in place in ADR 0186.
**Related:** [#2293](https://github.com/klasolsson81/jobbliggaren/issues/2293) (the line, Klas-direct; PR 1 is this change) · [#2294](https://github.com/klasolsson81/jobbliggaren/issues/2294) (M6) · [#2090](https://github.com/klasolsson81/jobbliggaren/issues/2090) (the SEO epic and its phase-2 gate B1) · #2285 and #2287 (the CTO's order of 2026-11-21) · ADR 0186, 0187 and 0188 (the junior list, its stored verdict and its count) · ADR 0166 (D10 and its amendments) · ADR 0160 (D6: no cache of a listed read) · ADR 0176 (the catalogue) · ADR 0174 (the sitemap) · ADR 0165 (D5: the continuation document) · ADR 0045 (budgets) · ADR 0113 (the lifecycle registry) · ADR 0154 (every merge is a deploy) · ADR 0062 and ADR 0039 (one filter definition)
**Measured against:** `feat/junior-indexable` in `C:/DOTNET-UTB/JobbPilot/.claude/worktrees/beautiful-moser-ccfe43`, cut from `455500ab9` (`origin/main`), read in its uncommitted tree on 2026-11-22.

> **Provenance.** Written by `adr-keeper` for the driving session from its brief (CLAUDE.md §9.2, §13); it adds no decision of its own. D1–D6 are Klas's answers and the rulings of `senior-cto-advisor`, `security-auditor` and `dotnet-architect`, each marked with the one whose it is. Facts about the code were read on 2026-11-22 in the tree named above. The figures under *Measurements* are the driving session's, taken by `JuniorItFloorLatencyMeasurement` on the dates shown; none was re-run for this ADR. What no source measures is listed under *What is not measured*. The legal outcome has one home, ADR 0166's Amendment 2026-11-22; this ADR points there and restates none of it beyond what the code computes. The ADR lies in the ignored 0071–0199 range and needs `git add -f` and a README row (precedent 0167–0188).
>
> **Numbering.** 0190 was measured free on 2026-11-22, before this file was written: in this worktree, in the main copy, in every other worktree listed by `git worktree list`, in `C:/tmp/jbl-2199-pr3` and on `origin/main`. ADR 0189 exists locally and is not in the index; this ADR does not touch it.

---

## Context

- **What Klas said, 2026-11-22 (#2293, verbatim).**
  - "Nu är det en live sida, som uppdateras själv. Dvs jag har för mig vi la noindex på junior, men detta måste absolut ha det bästa SEO. Många söker ju t ex juniora jobb, juniora systemutvecklare etc. Så detta kommer vara en viktigt SEO sida."
  - "Då vi jobbar i .NET så skall vi absolut inte  skapa massa funktioner flera gånger, om de kan återvändas (generiska klasser, clean code, clean arch, och allt det där)."
  - "Borde vi inte ha det, och då med samma gemensamma funktioner, för både jobb och junir-sidan (samt framtida offentliga jobb-sidor) ?"
- **Where `/junior` stood.** `noindex, follow` on every answer and in no sitemap (ADR 0186 D8; ADR 0166 trigger 20). Klas chose `noindex` on 2026-11-19, and approved `security-auditor`'s sentence that the floor of N ≥ 100 and E ≥ 25 applies the day `noindex` is removed: "Tas noindex bort gäller golvet." (ADR 0166, Amendment 2026-11-19.)
- **The line sits outside the tracker.** It is outside the tracker's sequence (#1665 → #1664) and outside the CTO's order of 2026-11-21 (#2287 → #2285 → #1665). It was flagged to Klas the same day (CLAUDE.md §1.5), and his answer is the first row of D6.
- **#2090's phase-2 gate.** B1 needs all five conditions (audit §13). An indexable `/junior` is a curated landing page, so B1 governs it (D2).
- **What the box held, counts only, 2026-11-22** (per #2293): 149 listed junior ads, equal to the anonymous `/junior` total, from 71 employers under the session's key, none above half. The floor holds for the national list, and 0 of the 33 kommuner meet it on their own. 139 of the 149 ads come from the reading of 2026-11-19, which ages as its ads close: with no new juniors the floor still holds at the start of 2026-W49 (110 ads, 56 employers) and fails from 2026-W50 (2026-12-07: 77 ads, 44 employers), and `/junior` then goes `noindex` by itself until the admin queue or the rules refill it. The merge-day reading takes E again under `security-auditor`'s definition (D3); the figure of 71 carries nothing over (security-auditor Ruling 1).
- **One composition, not many functions.** Klas's second quote is a design rule for the whole line, and it is why the line is cut into PR 1 and a shared composition (D1).

## Decision

**`/junior`'s default view (all kommuner, newest first, page 1) is `index, follow` with its own canonical and the pages part's 16th URL, and only while a floor holds at the request's own `now`: at least 100 listed ads from at least 25 employers, no employer holding more than half. One statement over the list's own base computes it, and only a boolean leaves the database. Every other answer of `/junior` stays `noindex, follow` with no canonical. #2090's phase-2 gate B1 is lifted for `/junior` only. The line after PR 1 is fixed in order: M6, then one shared composition for the public lists with `/junior`'s filters, then filters on `/lediga-jobb` (Klas's override of the CTO's "Nej"); never free text and never an employer key.**

### D1 — The line, in order (Klas's answers; CTO F1–F5, follow-up 1 and 2; security-auditor M4–M6)

1. **PR 1, an indexable `/junior` (#2293, this change).** The default view only, only while the floor holds (D3, D4); the sitemap's pages part gains `/junior`; the copy and one plain link (D5); the ADR 0166 re-take. It adds no filter, sort or search to any public list. It merges only after `security-auditor` has signed ADR 0166's Amendment 2026-11-22, Klas has approved the text of the seventh DPIA #456 §8-B addendum, and the merge-day readings of that amendment's D11 are in.
2. **M6, #2294, a follow-up PR with its own change-reason:** *a selection that reveals a faith never reaches a stored edge-log entry* (CTO follow-up 1; security-auditor M6, PLAUSIBLE until measured). It is not PR 1's, because her P8 bars a `deploy/` diff there, and it is not PR 2's, because the exposure exists on `/jobb` today. Order: after #2285 (it may run beside it, since they share no hotspot) and before #1665 and PR 2. PR 1 does not wait for it. PR 2 merges only after M6's fix is merged and live, meaning the release that carries it is applied and `--status` reads consistent, because PR 2's merge is its deploy (ADR 0154). M6's PR measures first under CLAUDE.md §9.2 (b) and never reads a log line on the box; if the measurement shows the finding does not hold, #2294 closes on it. The tracker does not list M6, so the order is flagged to Klas (CLAUDE.md §1.5).
3. **PR 2, the public lists share `/jobb`'s composition, with `/junior`'s filters.** Its own issue is filed when PR 1 merges, written as scheduling ("not built, not verified"), without `mvp`. It starts after #2285 merges, because the URL-contract extraction touches `search-params.ts`, and merges after M6 is live. It is a trigger-22 re-take (ADR 0166).
   - *The composition (CTO F1).* `JobAdSearchComposition.ApplyFilter`'s body becomes one core that takes a closed lifecycle gate, with two named entries: the existing `ApplyFilter(root, filter)` keeps `Status == Active`, its signature and all seven call sites byte-unchanged, and a new `ApplyListedFilter(root, filter, now)` composes `JobAdPublication.IsListed(now)` and never copies it. The gate is applied to `source`, never to `root`, so a minus-word's `NOT EXISTS` keeps the unfiltered root. One entry per class of surface is how ADR 0062's Amendment 2026-05-23 (no lifecycle filter in each consuming handler) is met.
   - *The closed subset (CTO F1).* `PublicJobAdFilter` in Application carries kommun, län, distans, yrkesgrupp, anställningsform and omfattning, one value each. One mapper builds `JobAdFilterCriteria` from it with `Q = null`, `Employer = []` and `OccupationName = []`. `JuniorIt` is set by the surface and never by the URL.
   - *The port.* `IJuniorItJobAdQuery` folds into one public list port that reads through `ApplyListedFilter`, `ApplySort` and `FacetCountsAsync`'s `ExcludeDimension`; both of the port's consumers move to it, the page and `GetJuniorItJobAdCountQueryHandler`. Rows stay per surface: seven members on `/junior`, six in the catalogue.
   - *Counts.* `/junior` stays per request for rows, total, options and floor (trigger 22). New options come from `ExcludeDimension` under the listed gate and never from memory. If PR 2's measurement exceeds ADR 0045 class (a), it comes back to the CTO, and a snapshot is not the fallback.
   - *The frontend (CTO F3).* A public family of Server Components: one plain GET form of native controls, no session, no client fetch, options from the public read. It shares exactly three things with `/jobb`: one URL-contract module (axis names, value grammar, defaults, one fixed key order), the dimension copy, and the house primitives (`JobAdPagination`, `jp-sortfield`). Its files join `PUBLIC_SURFACE`.
   - *`sida` moves here whole* (CTO follow-up 2, under first-page-only). `sida` becomes the page parameter of every public list, `sida=1` answers a 308, a stale `page` becomes an ignored key that carries the base's canonical (security-auditor S1), the hop's key moves to `sida`, `sida` gets its own edge-log verdict (M5 (g)), and all of it is read in the trigger-22 re-take. PR 1 keeps `page`.
   - *What PR 2 and PR 3 may not do* (security-auditor M5, S2). (a) The outer conjunct is `JobAdPublication.IsListed(now)`, never `Status == Active` alone. (b) A public DTO of its own, never `JobAdDto` or `ToDto()`. (c) A new anonymous route on its own IP bucket, never a signed-in route with `RequireAuthorization` dropped, and never `list-read`, `facet-counts`, `suggest`, `job-ad-suggest` or `taxonomy-read`. (d) No `ICapturesRecentSearch` and no `IAuthenticatedRequest`, and no shared component that brings a capture or a Server Action onto a public page. (e) Public islands call only anonymous reads through `pickForwardedHeaders`. (f) Counts come from the rows' own membership, never `/jobb`'s facet counts and never keyed by employer. (g) Every new public query key gets an edge-log verdict. Breaking (a), (b) or the first half of (c) is a Blocker. On the dimensions, a kommun or län, distans, anställningsform and omfattning are public on a `noindex` view; an occupation filter is public except the four `ReligiousOccupationGroups` and the three groups of ADR 0166's Amendment 2026-10-11 (whether Klas's decision of that date covers a group × kommun view is his to say, never inferred); the deadline order on the catalogue adds `ExpiresAt` to the six-member row, which is trigger 10.
4. **PR 3, the same filters on `/lediga-jobb`: Klas's override of the CTO's "Nej".** The CTO recommended "Nej, inte i den här linjen (Rek.)" (CTO K2): filtered addresses become `noindex` and are not found in search engines anyway; a filter over about 44 000 ads for a visitor without an account is a new search surface that needs its own re-take and a new decision on how the counts are made without loading the database; and whoever wants to filter the whole supply signs in and uses `/jobb`. Klas answered "Ja, som en egen PR efter /junior-filtren" (D6). PR 3 is built on the shared functions of PR 2, but only after a load measurement, a new ADR 0064 rule (d) amendment backed by it, a trigger-12 re-take (ADR 0166) and an answer to ADR 0176 D14 trigger 1. Its own issue is filed when PR 2 closes. **How the catalogue's filter counts are made is not decided here:** security-auditor's S2 reads "a per-place view in the snapshot's read (ADR 0176 D3)", while the CTO's F2 rejects in-memory snapshot views as a second filter implementation and live filtered reads as a break of rule (d); PR 3's plan reads decide.
5. **Never:** free text on a public list, and an employer key (Klas K3; security-auditor M4, S2). A public `q` reaches the description, which brings the free-text risk of W1 to search scale, makes W5 a per-person oracle (search a name, watch the ads that name it drop in the tick its erasure lands) and reaches Art. 9 (1 385 personal-assistance ads carry a health word in their text, vocabulary count 2026-10-11). An employer key is a lookup by organisation number, which for a sole trader is a personnummer (#841; Dataskyddslagen 3 kap. 10 §; ADR 0160 D4; ADR 0087 D8 (c)); ADR 0166 trigger 1 names "an employer profile". Each is Blocker-class until assessed, and each later PR is its own re-take (trigger 12 on the catalogue, 22 on `/junior`, 1 for an employer profile or any page at a path).
6. **Phase-2 hubs** (place pages, occupation pages) stay behind B1 (D2).

**Left out of PR 1** (CTO "left out"): new dimensions, the `ApplyFilter` refactor, the port fold, any `/jobb` file, anything in `/lediga-jobb` beyond one link, free text, employer, hubs, the hop, and `sida`.

### D2 — #2090's phase-2 gate B1 is lifted for `/junior` only (CTO F5, K1; Klas)

An indexable `/junior` belongs to the class of curated landing pages, so B1 governs it, and B1 does not hold. All five conditions are needed. The catalogue has been live four weeks, and that condition is met; no weekly reading of page indexing is recorded since 2026-10-18 (`docs/runbooks/seo-measurement.md` §4), ADR 0174 D5's readings 1 and 2 are both "Not yet taken" (reading 2 is due 2026-11-23), and the CTO has not chosen branch A. Klas's directive is therefore an override of B1 for this one page, and it was never condition 5, which cannot stand in for conditions 2–4. The override is his to make. Reasons the CTO gave for recommending it: it is one page and not a pattern; the membership is our own classification, which the source does not publish; B1's instrument reads only the job-sitemap URLs, so the page does not disturb it; and the floor guards the privacy side on every request. He answered "Ja, bara /junior (Rek.)" (D6): place pages and occupation pages wait for B1. The override is recorded here and is to be recorded as one comment on #2090 (CTO F5).

### D3 — The floor: one statement over the list's own base, only a boolean leaves (CTO F7 and follow-up 3; security-auditor S1, Ruling 1, Ruling 2; architect conditions 1–3)

- **The rule.** At least 100 listed ads (N), at least 25 employers (E), and no employer holding more than half: 2·S ≤ N. The numbers and the definition of an employer are `security-auditor`'s, and Klas approved the floor on 2026-11-19 ("Tas noindex bort gäller golvet"). The ground, the residuals and the reading of each are ADR 0166's Amendment 2026-11-22, which is the single home.
- **An employer, as the code computes it (security-auditor Ruling 1).** Two ads are one employer when they share an organisation number or share a company name, the name compared as `lower(btrim(company_name))`. N counts every ad. E is the distinct numbers plus the distinct names minus the distinct (number, name) pairs, over the ads that carry a number. S is the larger of the largest group by number and the largest group by name, over the ads that carry a number, plus every ad without a number. Her G1 sentence ("an employer is its organisation number, or its company name where it has none") is withdrawn: it split an employer in all three of the architect's cases (Viktigt 2). A safeguard merges when the key is unsure, because merging can only close the floor.
- **Where it lives (architect Viktigt 1; CTO follow-up 3).** `PublicListFloor`, a static class in `Application/JobAds/Publication/` beside `JobAdPublication`, not named `*Service`. It holds the named thresholds `MinimumAds = 100` and `MinimumEmployers = 25` (values in code and not `IOptions`: a configurable floor is trigger 20), `Holds`, the rule as one expression over an `EmployerSpread` (`Ads`, `Employers`, `LargestEmployerAds`), and `SpreadOf`, which builds the spread from a query of ads in grouped core LINQ. The spread never leaves the query, and its type has no `Dto` name because it never reaches the wire. The CTO withdrew F7's clause that future hubs reuse the type (YAGNI): the type carries no C# evaluator beyond the expression the query composes, and a hub's band is a different rule on stored state (ADR 0166 trigger 24).
- **One statement, one `now`, one boolean (CTO follow-up 3).** `IJuniorItJobAdQuery.MeetsFloorAsync(now, ct)` takes no kommun, order or page, so the floor is national by its signature. `JuniorItJobAdQuery` composes `PublicListFloor.SpreadOf` over `ListedJuniors(...)`, filters it by `Holds` and asks `AnyAsync`, which is one `EXISTS`; N, E and S come from one snapshot of that statement, so `2·S ≤ N` is never compared across two. The handler takes `clock.UtcNow` once and passes it to the count, the page and the floor, and reads the floor after the `NoSuchPage` return. An empty list has no group, so the answer is `false` and never an exception.
- **The shared base (CTO follow-up 3 (a)).** The count, the page and the floor read the same private base, `ListedJuniors`: `JobAdPublication.IsListed(now)` and `JuniorMembership.Of(assessments, groups)`. Each member passes its own `db.JobAds` read to it, so each keeps its own `get_JobAds` call for `JobAdLifecycleReadRegistry`, which gains a third `Active` row, `JuniorItJobAdQuery.MeetsFloorAsync`. The count's and the page's statements are pinned equal to the chains they composed inline before the helper (`TheCountAndThePage_SendTheStatementsOfTheirOwnInlineBase_SoTheSharedBaseChangesNoSql`), which is what keeps ADR 0188's R1 and the count's bucket valid (CTO latency ruling).
- **Only the outcome travels (architect Viktigt 3; security-auditor P7).** The port returns a `bool`, the envelope `Available(Rows, Places, FloorHolds)` carries one non-nullable boolean and its wire is exactly `rows`, `places` and `floorHolds`. The member is named for what it measures ("the whole list meets the floor at `now`") and not for a decision the API does not make, so the web combines it with Klas's rule for which view may be indexed (D4). A test pins one `bool` column at the reader; `JuniorItJobAdQuery.cs` and `PublicListFloor.cs` are on `OrganizationNumberSurfacingGuardTests`'s list of files that read a raw organisation number, so "no E or S in a log" is a build gate.
- **A failed read never gives `index`.** The API's failure is an exception and so a 5xx; the web's schema requires `floorHolds: z.boolean()`, so a response without the member or with a non-boolean is `unavailable`, which renders `noindex, follow` and a 5xx page and never `index`.
- **A printed reading (security-auditor Ruling 2).** It carries N and E as national totals, "E under 25" in place of E below the threshold, S only as yes or no, and never a per-employer grouping, a largest employer's count or share, or the pair count's terms. The committed reading is `docs/runbooks/junior-list-floor-count.sql`; `JuniorItJobAdQueryReadTests` runs it as committed over its seeds and requires its `floor holds` to equal the port's answer. `seo-supply-count.sql:23` measures the hub gate and not this floor.
- **No state (CTO F7).** The floor is stateless at the opening numbers, so it closes at them. A recorded decision with a lapse trigger lapses silently, and a hysteresis band is a stored value (trigger 24) or list state held between requests (trigger 22's class). The accepted consequence is that near 100 ads `/junior` can alternate between indexed and `noindex` across crawls (*Lapse triggers*).

### D4 — The indexing rule, and what stays as it was (security-auditor S1; Klas Q1; CTO F4 and follow-up 2; F6)

- **One view is indexable.** `isIndexableJuniorItView(query)` in `junior-it-params.ts`: no kommun, the default order `PublishedAtDesc`, page 1. `generateMetadata` gives `index, follow` and `alternates.canonical = publicJuniorUrl()` when that holds and `floorHolds` is true, and `noindex, follow` with no canonical otherwise. Klas's rule is one pure function and the API knows nothing of it.
- **Duplicate spellings** (`?page=1`, the explicit default `sortBy`, an empty `municipality=`, an unknown key such as `utm_source`) are the same parse and carry the base's canonical. `?page=n` for n > 1, a kommun, the deadline order, the empty kommun and every 404 and 5xx stay `noindex`. A `noindex` answer names no canonical, because a canonical beside `noindex` to another address is a conflicting signal (ADR 0176 D1).
- **The canonical and the sitemap's `loc` are one function.** `publicJuniorUrl()` is the page's canonical and the 16th entry of the pages part (ADR 0174's amendment); the entry is `loc` only and no parameter address is ever listed. The entry is static while the floor moves, so Search Console's "Submitted URL marked noindex" is the signal when the floor fails (CTO F8 (4)).
- **`robots.txt` is unchanged.** No rule for `/junior` or `/junior?`: a rule would hide the variants' `noindex`, and a blocked URL can be indexed from links alone.
- **Kept.** The title, description and link preview are fixed text with no row and no count (trigger 21); there is no JSON-LD of any kind; the count line, the rows and the pager sit in the one `data-nosnippet` block, and the kommun form now sits inside it (security-auditor m2: an option's count is a number a snippet could quote).
- **The cross-site hop is unchanged, and `proxy.ts` is byte-unchanged (CTO F6; security-auditor S4).** A search-result click is a cross-site navigation, so a signed-out searcher gets the continuation document and a 307 before the list; that is information for Klas and not a finding. Crawlers send no Fetch Metadata, so they are not bounced (ADR 0165 D5, pinned in tests, not yet measured against the live site), and the smoke pins that a crawler-shaped request without Fetch Metadata gets the list with `index`. After deploy Klas runs the URL Inspection live test on `/junior`, in Search Console and in Bing. It is manual and outside trigger 5. If it shows the continuation document, or "excluded by noindex" while the floor holds, that is a STOPP with a fix of its own.

### D5 — The copy, and the one link on `/lediga-jobb` (Klas; CTO F8; design-reviewer)

- **The copy** is Klas's "Alternativ A (Rek.)". The page's `<title>` is `Juniora IT-jobb – lediga jobb för dig som är ny inom IT` (`publicjob.junior.metaTitle`), which the root's title template renders as `… | Jobbliggaren`; the `h1` stays "Juniora IT-jobb" (`publicjob.junior.title`); `publicjob.junior.metaDescription` is new text in sv and en. The band's line, `publicjob.junior.lede`, is Klas's second answer (D6, row 10): Alternativ A's line was two sentences and three lines at 1280 px, and DESIGN.md §6 keeps a band's line one sentence on one line (`design-reviewer`, 2026-11-22). The strings live in `messages/{sv,en}/publicjob.json` and are not quoted here (ADR 0176 D9). The text says IT, because the list is Data/IT only and the Art. 9 reasoning rests on that (CTO F8 (2)), and it carries no row and no count.
- **The one link** (Klas's words, D6 row 8; its place, D6 row 11). `catalogue.juniorLink` is a paragraph of its own, the first in the page's content under the band, outside the `data-nosnippet` block, on every page of the catalogue and not in the empty state, where the list has no listed ad to lead to. "juniora IT-jobb" is a plain `<a href="/junior">` with the house's `underline`, never `next/link`, so nothing prefetches. It cannot sit in the band's line: DESIGN.md §6 keeps that line one plain sentence and never a link, and `band-lines.test.ts` refuses one (`design-reviewer`, 2026-11-22). It exists because a page reached only through the sitemap is an orphan (CTO F8 (1)). It is never in the header, the footer or the landing (ADR 0176 D11; audit §6.7). No CSS is added. The change is a declared R3 delta of ADR 0166's amendment and ADR 0176's amendment of the same date.

### D6 — Klas's answers, 2026-11-22, verbatim, each with its question's owner

All eight were given by AskUserQuestion in the plan stage of #2293. Any answer other than the recommended one is recorded as a Klas-override (Rule 5). Q1, Q2 and K1 are quoted as their owners wrote them in ADR 0166's Amendment 2026-11-22, which carries the legal outcome; K2 and K3 are quoted below the table.

| # | Question (its owner) | Klas's answer, verbatim |
|---|---|---|
| 1 | The tracker flag: the line lies outside the tracker's #1665 → #1664 and the CTO's order #2287 → #2285 → #1665 (the driving session) | "Ja, kör linjen nu (Rek.)" |
| 2 | Q1: re-take ADR 0166 so that `/junior` can be found through search engines, and in what scope (`security-auditor`) | "Ja, bara förstasidan (Rek.)" — the default view only (all kommuner, newest first, page 1), and only while the floor holds |
| 3 | Q2: a seventh addendum to DPIA #456 §8-B, no new full DPIA and no prior consultation with IMY (`security-auditor`) | "Ja, sjunde tillägget (Rek.)" — he approves the text when it is written |
| 4 | K1: lift #2090's phase-2 gate B1 for `/junior` only; it also answers her conditional Q3 (`senior-cto-advisor`) | "Ja, bara /junior (Rek.)" — place pages and occupation pages wait for B1 |
| 5 | K2: filters on `/lediga-jobb`; the CTO recommended "Nej, inte i den här linjen (Rek.)" (`senior-cto-advisor`) | "Ja, som en egen PR efter /junior-filtren" — **Klas's override of the CTO's recommendation** |
| 6 | K3: free-text search on the public lists (`senior-cto-advisor`) | "Nej, bara filter (Rek.)" |
| 7 | The SEO text for `/junior` (the driving session, PR 1) | "Alternativ A (Rek.)" — D5 |
| 8 | The link in `/lediga-jobb`'s lede (the driving session, PR 1) | "”… Är du ny inom IT? Se juniora IT-jobb.” (Rek.)" — `catalogue.juniorLink` |
| 9 | The text of the seventh addendum to DPIA #456 §8-B (`security-auditor` M2) | "Ja, som den står (Rek.)" |
| 10 | `/junior`'s band line, since Alternativ A's two sentences broke DESIGN.md §6 (`design-reviewer`) | "Juniortjänster, traineeprogram och IT-jobb utan krav på erfarenhet. (Rek.)" |
| 11 | The link's place, since §6 forbids a link in a band's line (`design-reviewer`) | "Ja, direkt under bandet (Rek.)" |

K2 and K3 as `senior-cto-advisor` wrote them (plan read G2, "Questions for Klas"):

> **K2 — Filter på /lediga-jobb.** Ska /lediga-jobb få samma filter som /junior: ort, yrkesgrupp, anställningsform, omfattning och distans?
> - *Nej, inte i den här linjen (Rek.):* filtrerade adresser blir ändå noindex och syns inte i sökmotorer. Filter över cirka 44 000 annonser för besökare utan konto är en ny sökyta. Den kräver en egen omprövning och ett nytt beslut om hur antalen räknas utan att belasta databasen. Den som vill filtrera hela utbudet loggar in och använder /jobb. Ortssidor kommer som egna adresser i fas 2.
> - *Ja, som en egen PR efter /junior-filtren:* den byggs på samma delade funktioner, men först efter en mätning och en ny omprövning.

> **K3 — Fritextsökning på de publika listorna.** Ska de publika listorna få ett sökfält som /jobb?
> - *Nej, bara filter (Rek.):* fritext i annonstexten låter vem som helst söka fram annonser som nämner en viss person, till exempel en kontaktperson. Det är också den tyngsta frågan för en besökare utan konto, och fritextadresser blir ändå noindex. security-auditor behöver bedöma den först.
> - *Ja:* den byggs i en egen PR, efter security-auditors bedömning och din omprövning.

## Measurements

**What these are.** Dated readings in the form of ADR 0182 R2 and ADR 0188 R1, taken on **a developer machine over Testcontainers.** They are not a bound on the box's figures in either direction (*What the cost follows*, under *The junior bucket*). They are not live numbers: re-run the class to regenerate them (`dotnet test --project tests/Jobbliggaren.Api.IntegrationTests -- --filter-class "*JuniorItFloorLatencyMeasurement"`, AGENTS.md §7; the trait is `SmokeTest` and the collection `JobAdBrowsePlan`). The run-to-run spread on the same machine is about 20 %.

**The corpus** (`JuniorLatencyCorpus`): 40 000 ads, the shipped rules through the real classifier, a synthetic organisation number on each of 997 employers with about 1 % of the ads carrying none, and `VACUUM (ANALYZE)` on `job_ads` and `job_ad_junior_assessments` after seeding. It holds 13 380 ads in the Data/IT field's 12 groups, each with one assessment, 9 615 of them listed, and 2 526 listed juniors.

| Run (UTC, 2026-11-22) | State of the code | Default view, three statements, p50 / p95 | Page 100 in the deadline order, three statements, p50 / p95 | The floor alone, p50 / p95 | Statement plans, execution |
|---|---|---|---|---|---|
| 09:32Z | The run the CTO and `security-auditor` ruled on | 36.0 / 42.3 ms | not measured | 27.2 / 36.2 ms | floor 27.8 ms |
| 09:55Z | After the CTO's in-block fix 1 (the costliest path added) | 36.6 / 44.7 ms | 42.2 / 52.5 ms | 26.5 / 33.9 ms | floor 26.8 ms, count 7.0 ms, page 100 7.2 ms |
| 10:12Z | Re-run on the final code | 45.8 / 51.9 ms | 51.6 / 59.9 ms | 33.0 / 38.8 ms | floor 32.5 ms, count 7.1 ms, page 100 7.7 ms |

- **"Page 100 in the deadline order"** is the costliest path a caller can choose: every kommun, the order `ExpiresAtAsc`, and page `min(lastPage, MaxPage)`, which is 100 on this corpus. A bucket is priced at its worst case (CTO latency ruling, fix 1).
- **The plans.** Every scan of `job_ads` in all three plans is a Bitmap Index Scan on `ix_job_ads_occupation_group_concept_id`, with no Seq Scan on `job_ads`; `TheFloorStatement_ScansJobAdsOnlyThroughTheOccupationGroupIndex` pins this for the floor's statement. `job_ad_junior_assessments` is read by a sequential scan in each statement, printed as `rows=3449.00` with `Rows Removed by Filter: 9931`, about 1 ms. That is observed and lies outside bound b2 of ADR 0064's amendment, which covers `job_ads` only.
- **Inside ADR 0045 class (a)**, p95 300 ms: every p95 above is at most 59.9 ms.
- **Database time one address can spend at the bucket's 60 per 60 s,** as 60 × p95:

| Run | At the costliest path | At the default view | Of which the floor |
|---|---|---|---|
| 09:32Z | not measured | 2 538 ms a minute | 2 172 ms a minute |
| 09:55Z | 3 153 ms a minute | 2 683 ms a minute | 2 033 ms a minute |
| 10:12Z | 3 594 ms a minute | 3 117 ms a minute | 2 329 ms a minute |

  (The 09:32Z row is derived, 60 × 42.3 and 60 × 36.2; the other two are the measurement's own printed figures.) These are above ADR 0188's per-account anchor of about 1 s of database time a minute from one route, **which never was this bucket's basis** (CTO latency ruling, point 2; `security-auditor`'s reading of the bucket's cost, §1). The bucket's number and the method it is judged by are `security-auditor`'s, and her reading is the section *The junior bucket* below.
- **The box against the corpus.** The corpus holds more listed juniors than the box. How the two compare on what the scans read is taken on the merge day:

| | The corpus | The box (counts only) |
|---|---|---|
| Listed juniors | 2 526 | 149 (2026-11-22 07:14Z) |
| Listed ads in the Data/IT groups | 9 615 | <!-- MERGE-DAY: in-field listed total --> |
| Ads in the Data/IT groups, any status | 13 380 | <!-- MERGE-DAY: in-field total, any status --> |
| Rows of `job_ad_junior_assessments` | 13 380 | <!-- MERGE-DAY: assessment rows --> |

  The box's totals are read on the merge day, as totals only (CTO latency ruling, fix 8; `security-auditor`'s reading of the bucket's cost, 3 (c)). Every row is a baseline of return condition r4 in ADR 0064's amendment.

## The junior bucket (`public-job-ad-junior-it`)

`security-auditor` read the bucket against the figures below on 2026-11-22. **It stays 60 per 60 s per relayed client address, accepted as built.** Its two halves and its recompute trigger are in the docblock of `RateLimitingOptions.PublicJobAdJuniorIt`; this section records what they rest on.

**(a) The record.**
- **The reading.** 2026-11-22, 10:12 UTC, on the final code: a developer machine, Testcontainers, regenerated by `JuniorItFloorLatencyMeasurement`. An earlier pass at 09:55 UTC read lower on every figure; the record keys on the later one.
- **The corpus.** `JuniorLatencyCorpus`: 40 000 ads, 13 380 of them in the Data/IT field's 12 groups with one assessment each, 9 615 of those listed, 2 526 listed juniors, 997 synthetic employers. The box listed 149 juniors that day.

| p50 / p95, ms | 09:55 UTC | 10:12 UTC |
|---|---|---|
| The default view, three statements | 36.6 / 44.7 | 45.8 / 51.9 |
| Page 100, the deadline order, no kommun, three statements | 42.2 / 52.5 | 51.6 / 59.9 |
| The floor alone | 26.5 / 33.9 | 33.0 / 38.8 |

- **One address at the sustained 60 a minute:** 3 594 ms of handler time a minute at page 100's p95, and 3 117 ms at the default view's, of which the floor is 2 329 ms. A full burst of 60 spends about 3.6 s more, at once.
- **Against the per-account anchor.** That is about 3.6 times the house's ~1 s a minute per account (`RateLimitingOptions.CompanyWatchCriteriaList`), which was never this bucket's basis. An address is not an account: it can carry many readers, and one caller can hold many addresses, so database time per address bounds neither one reader's share nor the total (`senior-cto-advisor`, 2026-11-22, alternative B rejected). The bucket bounds one address's rate. No limit on this route bounds many addresses together; ADR 0064's amendment for #2293 names the remedy (r6).
- **Not measured.** The box's figures and plans; calls made concurrently, since every figure above is one call at a time; and how the box's data compares with the corpus's on what the cost follows.

**What the cost follows.** In the recorded plans, every scan of `job_ads` goes through `ix_job_ads_occupation_group_concept_id`, whose only predicate is `occupation_group_concept_id IS NOT NULL`, and `job_ad_junior_assessments` is read by a sequential scan in each statement. The scan therefore visits every ad in the field's groups whatever its status, and `JobAdPublication.IsListed` filters them after it. No code in `src/` deletes a `job_ads` row (searched 2026-11-22), so an ad that ends stays in the field's groups, and on the box their number grows while the list's does not. The corpus holds more listed juniors than the box (2 526 against 149). Whether it holds more ads in the field's groups is not measured, and its rows are fixture text, narrower than imported ads. The figures above are therefore not a bound on the box's in either direction.

**(b) What sends the bucket back to `security-auditor`:** the recompute trigger in the docblock of `RateLimitingOptions.PublicJobAdJuniorIt`. That is its one home, and it is not restated here.

**(c) The readings.** At each `/junior` reading in `seo-measurement.md`'s schedule, in the form of ADR 0174 D5 (c): counts only (CLAUDE.md §9.2 (a)), and no line is printed (§9.2 (b)).
- **Rejections on this policy, per day,** in the `jobbliggaren-api` container's log. The command prints a count and a date on each line, and nothing else:
  `ssh jp-vps 'sudo -n docker logs -t jobbliggaren-api 2>&1 | grep "Path=/api/v1/public/job-ads/junior Method=" | cut -c1-10 | sort | uniq -c'`
  `OnRejected` logs only the path and the method (`RateLimitingExtensions.cs:94-97`). The log does not survive a release (ADR 0174 D5, the baseline), so its window starts when the container started; the rest of the reading's window is recorded as not measured, never as an estimate.
- **5xx on `/junior` in Search Console's Crawl stats,** read as `seo-measurement.md` assigns it. A 429 on this route is a 500 on the page, and Crawl stats is the window that outlives a release.
- **Recorded** as dated amendments to this ADR. No issue is filed (CLAUDE.md §9.6).
- **At each merge-day reading in this line,** beside the listed ads in the field, two totals: the ads in the field's groups of any status, and the rows of `job_ad_junior_assessments`.

## Lapse triggers and STOPP conditions

Nothing detects these automatically. Each is read when the event is proposed or reported, and Klas is the named human reader (ADR 0166 D10).

1. **Search Console shows `/junior` alternating** between indexed and not indexed across crawls (CTO F7's accepted trade-off of a stateless floor near 100 ads). The alternative, a band, is stored state and is ADR 0166 trigger 24, which needs its own reading by `security-auditor`.
2. **Klas's URL Inspection live test after deploy** shows a continuation document, or "excluded by noindex" while the floor holds: a STOPP to Klas with a fix of its own (D4; CTO F6).
3. **"Submitted URL marked noindex" for `/junior`** is the expected reading when the floor fails while the static sitemap entry stays. With no new juniors the floor fails from 2026-W50 (2026-12-07). It is an event to read and not a defect.
4. **ADR 0064's return conditions r1–r6** (its Amendment 2026-11-22) bring the question back to the CTO.
5. **ADR 0166 D10 triggers 1–24**, as the Amendment 2026-11-22 rewrites 20 and extends 21. That amendment is their single home and Klas is their reader.
6. **A later PR of the line (D1)** is a re-take of its own trigger: 22 for `/junior`'s filters, 12 for the catalogue's, 1 for an employer profile or a page at a path. A public employer key or a public `q` built without its assessment is a Blocker (security-auditor M4).
7. **The bucket's recompute trigger** lives with the bucket, in `RateLimitingOptions`' docblock for `PublicJobAdJuniorIt`, and is not repeated here (CTO latency ruling).

## What is not measured

- **The box's own query plan and timings.** The figures are a developer machine's. The corpus holds more listed juniors than the box; how the box's data compares on what the cost follows is not measured (*The junior bucket*).
- **Whether Googlebot or Bingbot send cross-site Fetch Metadata** (security-auditor S4). If they do, they are given the `noindex` continuation document and the page is never indexed, which decides whether the directive works and not privacy. Klas's URL Inspection reads it.
- **Whether search engines other than Google and Bing quote rows** (security-auditor W2, *Left*).
- **The personnummer-shaped count on 2026-11-22.** It was not taken that day (it read 0 on 2026-11-21) and is owed on the merge day.
- **How often `/junior` will alternate near 100 ads.** Nothing has been observed.
- **The corpus's behaviour at PR 2's added statements.** Each facet adds one aggregate per request to the class ADR 0064's amendment defines, and r2 there brings the measurement back before PR 2 merges.

## Alternatives considered

### Alt A — The chosen design (D1–D6)
**For:** the page's own audience, people who search for junior IT jobs, can find it; the privacy safeguard is checked on every request with no stored state; one composition is shared by every public list; each step of the line is its own re-take.
**Against:** see *Consequences*.

### The scope of indexing (security-auditor Q1; Klas chose the first)
- **The default view only (chosen).** D1's "a place such a search lands" is met by one URL, and seven more pages would put the other 129 rows into index associations for no purpose the sitemap and the catalogue do not already serve (Art. 5(1)(c), 25(1)).
- **Every page in the default order.** Acceptable on the catalogue's precedent and not recommended: more addresses carry employer names in the search engines' index without the purpose requiring it.
- **Keep `noindex`.** No re-take, and the findability is not built.

### The floor's shape (CTO F7 and follow-up 3)
- **Three aggregates cross the port (F7 as first written)** and a stateless Application policy evaluates them. Rejected: the largest employer's count is a per-employer value, and once it is in process memory any log line, exception or dump could carry it, which fails `security-auditor`'s signature condition (Art. 5(1)(c), 25(1)).
- **A separate Application LINQ query for the floor.** Rejected: a second composition of the base, equal to the rows' base only by coincidence.
- **Unnamed literals in the query.** Rejected: the legal values would lose their named, ADR-pointed home.
- **A recorded decision with a lapse trigger.** Rejected: it lapses silently, and 139 of the 149 ads come from an ageing reading.
- **A band with state (hysteresis).** Rejected: a stored value is trigger 24, list state between requests is trigger 22's class.
- **The floor computed in the web.** Rejected: the organisation number is never on the wire.
- **Skipping the floor when the count's N is below 100, or a query key set by the web that skips it on views that would discard it.** Rejected (CTO latency ruling): the first is a second copy of `MinimumAds` that compares N across two statements, and the second lowers the average cost, not one address's worst case, and adds a public query key to a signed contract.

### The employer key (security-auditor Ruling 1)
- **(a) `COALESCE(organization_number, company_name)`, her G1 sentence.** Withdrawn: it splits one employer in all three of the architect's cases.
- **(b) ADR 0182 D1 as built.** Not adopted: it chooses rows for a section and leaves out an ad without a number, where `/junior` shows the row.
- **(c) The hub script's name-only key.** Not adopted: it splits one number across names.
- **(d) E as the smaller of the distinct counts by the two keys, S as the larger of the two maxima.** Not adopted: it counts too many in more cases than a chain and would weaken the floor.
- **The union definition (chosen).** Ground and residual are in ADR 0166's amendment.

### `sida` in PR 1 or PR 2 (CTO follow-up 2)
- **In PR 1** (F4 as first ruled, for index timing). Under first-page-only no page URL is ever indexable, so the premise fell, and the rename would add four declared deltas to her re-check (the parse, the builder, the hop's key through `proxy.ts`'s imports, and the bounce's edge-log verdict).
- **In PR 2, whole (chosen).** It belongs with the URL-contract module and D8's revoked premise (the hop drops the page). It is never dropped.

### The latency and the bucket (CTO latency ruling)
- **A, accept the floor as built and do nothing more.** Rejected as written: the first cost measurement this route ever had would not reach the bucket's owner, and the comment describing the route's cost would stay false.
- **B, re-derive the bucket against a database-time anchor.** Rejected: it applies an account's share to a bucket that is partitioned by address, it cuts availability for shared addresses just as the page becomes indexable, it bounds no caller who uses many addresses, and it decides her method for her.
- **C, accept the statement and its place in rule (d) as built, and send the measured cost to the bucket's owner (chosen).** An index now is DDL (her P8 bars it) and nothing measured needs it; a bulkhead now has no traffic to answer (YAGNI) and is r6's remedy.

### The public filters' shape (CTO F1, F2, F3)
- **(b) the gate in `ApplyFilter` as `Active ∧ IsListed`.** Rejected: it is correct only through the unstated rule that a listed ad is always Active, and a later narrowing of `/jobb`'s gate would silently narrow the public lists (CCP).
- **(c) a second composition for the public lists.** Rejected: it duplicates the dimension code.
- **One row for every list.** Rejected: it reopens two signed row decisions (ADR 0166 triggers 10 and 21; ADR 0186 D6) and gains no reader.
- **An in-memory snapshot view per filter, or live filtered reads on the catalogue in this line.** Rejected for the catalogue by the CTO's F2 (a second filter implementation; a break of rule (d), trigger 12); PR 3's plan reads reopen the question under Klas's override.
- **Mode-aware `/jobb` components, or primitives extracted from `/jobb`'s popovers.** Rejected: two reasons to change for each, a session-bound graph in the public render, and a collision with #2285.

## Consequences

### Positive
- `/junior` can be found by the people who search for junior IT jobs, with a canonical of its own and a sitemap entry, and it drops out of the index by itself when the list is too small or too concentrated to publish.
- The safeguard is a per-request fact: no stored state, no cache, nothing to forget to update, and only a boolean ever leaves the database.
- The floor's definition has one home and the code and the merge-day reading compute the same thing, pinned by a test that runs the committed SQL over the same seeds.
- The line is fixed in order with one composition for every public list, so the filters are written once, and each step is its own re-take.

### Negative and accepted trade-offs
- **A sole trader's company name, which can be a personal name, is findable through a search engine on `/junior` until the engine recrawls** (ADR 0166 W2 and W4). The rows, the count and the pager are `data-nosnippet`, and every name is already on the ad's own indexed page and in the catalogue. The residuals are named in the amendment.
- **Near 100 ads `/junior` can alternate** between indexed and `noindex` across crawls, and without new juniors the floor fails from 2026-W50 (2026-12-07).
- **Search visitors pay the hop** (the continuation document and a 307) because a search click is cross-site.
- **The floor runs on every view,** though the web uses it only on the default view. Every way to avoid that moves Klas's indexing rule into the API or adds a key callers can set (CTO latency ruling).
- **A request now runs three statements** instead of two. The cost is measured (*Measurements*) and its class is ADR 0064's third.
- **Two row types, six and seven members,** are kept, and a stale `?page=n` is crawled as `noindex` until PR 2 renames it.
- **r4 and r6 of ADR 0064's amendment depend on people reading them;** no test catches them.
- **PR 2 waits** for #2285 and for M6 to be live.

## Implementation

**PR 1 on `feat/junior-indexable`, #2293, lane BE+FE.** No migration, no package, no `deploy/` diff, no ACL change, and no edit to `proxy.ts`, `robots.ts` or the rules file. Read from the tree on 2026-11-22:
- **Application and Infrastructure.** `PublicListFloor` and `EmployerSpread` (new); `IJuniorItJobAdQuery.MeetsFloorAsync`; `JuniorItJobAdQuery`'s private `ListedJuniors` base and the third member; the handler's one `now` and `PublicJuniorItJobAdsPage.Available(Rows, Places, FloorHolds)`; the false reason in `IJuniorItJobAdQuery`'s docblock removed; the comment at `RateLimitingExtensions`' junior policy made true.
- **Api and tests.** The registry row, the guard-test list, the contract, wire and boundary pins, `JuniorItJobAdQueryReadTests` (three statements, the floor's boundary grid, the removal paths, the committed SQL over the same seeds), `PublicListFloorTests`, and `JuniorItFloorLatencyMeasurement` with `JuniorLatencyCorpus`. `docs/runbooks/junior-list-floor-count.sql` is the merge-day reading.
- **Web.** `isIndexableJuniorItView` and `publicJuniorUrl` in `junior-it-params.ts`; the page's `generateMetadata`; the schema's `floorHolds`; the sitemap's 16th URL; the form moved inside the `data-nosnippet` block; the new copy, and `catalogue.juniorLink`'s paragraph under the catalogue's band; `lighthouserc.json`'s `/junior`; the smoke and its stub; the group, canonical and sitemap tests.
- **Hotspots touched** (CLAUDE.md §6.5; the borrow and its return are the driving session's): `messages/{sv,en}/publicjob.json` (`hotspot:i18n`). `globals.css` is not touched.
- **Carried by PR 1 outside this ADR set** (the driving session's own changes; some are in the tree already, and none is claimed finished here):
  - the seventh addendum to DPIA #456 §8-B, local, written in the main copy, its text approved by Klas (security-auditor M2);
  - the records the indexed view makes false (security-auditor m1): `recruiter-pii-erasure.md` §2.4a and the processing register's JobTech row (local), and ADR 0186 D8 by its amendment of this date;
  - `seo-measurement.md` (security-auditor m3; CTO F8 (3)): `/junior` with every parameter among the URLs that carry ad data, the pages part's entry, and a `/junior` reading row;
  - the merge-day floor reading's section in `public-job-page-publication-count.md`;
  - the merge-day readings of ADR 0166's amendment D11, and the in-field total in *Measurements*;
  - the mandatory agents in the PR and `agents-done` only after they have reported;
  - Klas's URL Inspection after deploy (D4);
  - one comment on #2090 (D2), and PR 2's issue when PR 1 merges (D1).

## References

- **Reports (local, `docs/reviews/`):** `2026-11-22-2293-plan-senior-cto-advisor.md`, `…-plan-senior-cto-advisor-followup.md`, `…-floor-latency-senior-cto-advisor.md`, `…-plan-security-auditor.md`, `…-floor-key-security-auditor.md`, `…-bucket-cost-security-auditor.md`, `…-plan-dotnet-architect.md`.
- **Issues:** #2293, #2294, #2090, #2285, #2287, #1665, #1664.
- **ADRs:** ADR 0039 Beslut 1 · ADR 0045 Beslut 1 · ADR 0062 (Beslut 2–4, Amendment 2026-05-23) · ADR 0064 (rule (d) and its amendments) · ADR 0087 D8 (c) · ADR 0106 D9 · ADR 0113 · ADR 0154 · ADR 0160 D4, D6 · ADR 0165 D5 · ADR 0166 D10 and its amendments · ADR 0174 D3, D5 · ADR 0176 D1, D9, D11, D12, D14 · ADR 0182 D1, D4, R2, R5 · ADR 0186 · ADR 0187 · ADR 0188 R1.
- **Local documents:** `docs/research/2026-10-18-seo-aio-audit.md` §6.4, §12, §13; `docs/runbooks/seo-measurement.md` §4; DPIA #456 §8-B, fifth and sixth addenda (main copy).
- **Literature (as the CTO cited it):** Martin, *Clean Architecture* (2017), ch. 7, 10 and 13 · Evans, *Domain-Driven Design* (2003), "Making Implicit Concepts Explicit" and the Specification pattern · Hunt and Thomas, *The Pragmatic Programmer* (1999), DRY · Ford, Parsons and Kua, *Building Evolutionary Architectures* (2017), fitness functions · Nygard, *Release It!* 2nd ed. (2018), ch. 5 · Saltzer and Schroeder (1975), least common mechanism.
- **Google Search Central,** as read by the plan's research agent on 2026-11-22 and not re-read for this ADR: faceted navigation, crawl budget, blocking indexing, pagination, job posting structured data, spam policies.
