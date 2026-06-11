# Code-review: Fas E2e — Rensa-röda textlänkar + sorterings-labels (commit ac3e108, branch `feat/sok-paritet-fe-rensa-sortering-e2e`)

**Status:** Approved
**Granskat:** 2026-06-11
**Auktoritet:** CLAUDE.md §4 (TS/Next.js), §5.2 (FE anti-patterns), §10 (svenska); ADR 0067 rad 109; ADR 0042 Beslut D/F; ADR 0062
**Scope:** Frontend — 4 filer (globals.css, jobb-filter-popover.tsx, jobb-results-toolbar.tsx + test). Ren FE-batch.

### Blockers
Inga.

### Major
Inga.

### Minor (nice-to-fix)

1. **sortBy-bevarande genom clearAllFilters otestat på komponentnivå** — nya clear-all-testet körde default-sort (utelämnas ur URL); att icke-default sort överlever "Rensa alla filter" bevisades inte. Föreslogs testvariant med `sortBy="ExpiresAtAsc"`. *(Pre-existing gap — `search-params.test.ts` saknas; clear-all är dock ny caller.)*
2. **ADR 0067-ordalydelse vs implementerad label** — rad 109 säger "Datum (publicering)"; implementerat "Datum (nyast)" per Klas-prompt E2e 2026-06-11 (Klas-prompt överrider). Föreslogs att E2e-impl-notatet nämner slutliga label-trion så ADR-trailen inte ljuger mot UI:t.

### Verifierat (kärnfrågor)

- **clearAllFilters korrekthet:** nollar alla tre axlarna i både lokal state och URL via samma `pushState`/`buildJobbHref`-väg som `removeChip` — q-bevarande, sortBy/pageSize-bevarande, `page` utelämnas alltid (sida 1 vid filter-nollning).
- **Inga kvarvarande `.jp-popover__clear`-referenser i produktionskod** (repo-grep: endast v3-mockup-referensmaterial + dokumenterande CSS-kommentar). Kanoniskt rename, inte parallell dubblett-klass.
- **Alla /jobb-ytors Rensa-affordances bär nya klassen;** admin-granskningens "Rensa" är `Link` i annan yta — korrekt orörd.
- **Sort-labels sakligt korrekta:** "(CV-match)"-borttagningen åtgärdar faktafel (Relevance = ts_rank-FTS, ADR 0062; ADR 0042 Beslut F förbjuder CV-placeholder). ExpiresAtAsc re-verifierad on-disk: `OrderBy(ExpiresAt == null).ThenBy(ExpiresAt).ThenBy(Id)` → asc, NULLS LAST, deterministisk tiebreak — "Ansökningsdatum (sista ansökan)" stämmer.
- **"Rensa alla filter" strukturellt gated** på chips.length > 0 + test.
- **A11y-basics:** global `*:focus-visible` täcker knappen; underline ger affordance utöver färg; #BE1B1B på vit ≈ 6,3:1; dark-paret finns.
- **Konventioner:** ReadonlyArray, role-baserade test-queries, inga any/casts, ingen useEffect-fetch.

### Bra gjort

- Rename till kanonisk klass i stället för dubblett — ingen CSS-duplicering; kommentaren dokumenterar varför + kontrast-bevis.
- Faktafel-korrigeringen citerar ADR 0062/0040/0042 i kod-kommentar så "(CV-match)" inte återinförs.
- Återanvändning av `pushState`/`buildJobbHref` gör clearAllFilters trivialt korrekt (F3 B-FIX-symmetrin hålls).
- Test-uppdateringarna följde med label-bytet i alla befintliga tester.

### Sammanfattning

0 blockers, 0 major, 2 minor. Mergeklar.

---

## Åtgärds-trail (huvud-CC, 2026-06-11 — in-block samma PR)

| # | Fynd | Åtgärd |
|---|---|---|
| m1 | sortBy-bevarande otestat | Testvariant `sortBy="ExpiresAtAsc"` → `/jobb?q=backend&sortBy=ExpiresAtAsc` tillagd; 11/11 gröna. |
| m2 | ADR-ordalydelse vs label | E2e-impl-notatet i ADR 0067 dokumenterar slutliga label-trion + Klas-prompt-källan. |
