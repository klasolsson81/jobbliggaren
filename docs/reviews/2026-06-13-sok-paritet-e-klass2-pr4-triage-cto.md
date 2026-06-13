# senior-cto-advisor — Fas E Klass 2 PR-4 finding-triage (BESLUTSFATTARE)

**Datum:** 2026-06-13
**Roll:** Decision-maker (§9.2). CC gav ingen egen rekommendation.
**Klas-GO:** CTO-triage vald (AskUserQuestion 2026-06-13).
**Relaterat:** BESLUT 4 (`2026-06-13-sok-paritet-e-klass2-cto.md`), ADR 0039-amendment
2026-05-20, ADR 0043, CLAUDE.md §5/§9.6.

## Beslut: **DEFERRA-MED-TRIGGER**

PR-4 (saved-search Klass 2-`*Labels`) byggs **INTE nu**. Trigger-villkor: **"när
saved-search-list-UI:t (ListSavedSearches-konsumerande FE-yta) faktiskt byggs."**
Lyfts EJ som numrerad TD — registreras i samma deferral-not som den redan kända
SavedSearch-FE-skulden (current-work.md Pending operativt #3). **Korrigerar
BESLUT 4** (som dömde PR-4 till "ren in-fas Fas E-leverans efter PR-3").

## Verifiering on-disk (det som vägde om beslutet)

- **Routen är `/sokningar`, inte `/sparade`** — `sokningar/page.tsx` renderar
  `RecentSearchList` via `getRecentSearches()` (pivot SavedSearch→RecentSearch,
  ADR 0039-amendment 2026-05-20). Saved-search-listan har **ingen route-render**.
- **Ingen `ListSavedSearches`-API-klient** i `lib/api/`/`lib/actions` (grep: noll).
- `savedSearchDtoSchema` (FE) konsumeras av ingen komponent.
- **De befintliga `OccupationGroupLabels`/`MunicipalityLabels`/`RegionLabels`
  renderas redan ingenstans.** PR-4 vore labels till en 100% odöd DTO-yta.
- Backend bekräftar: `SavedSearchDto.cs:12-13` "SavedSearch-API:t konsumeras inte
  av FE".

## Motivering

- **YAGNI (Beck) + Speculative Generality (Fowler 2018 kap. 3):** infrastruktur för
  en konsument som inte är schemalagd. Samma grund som BESLUT 1 avvisade Variant A.
- **§5 död kod:** att utöka odöd label-yta för "konsistens med annan död kod"
  fördubblar skulden — inte konsistens-värde.
- **CCP/REP (Martin 2017 kap. 13) — avgörande:** när saved-search-list-UI byggs
  läggs **alla fem** dimensioners label-rendering i ETT svep (samma change-reason).
  Trigger-deferral är mer CCP-troget än att bygga 2/5 nu och 3/5 sen.
- **SoC (memory `feedback_one_concern_per_pr_soc`):** PR-4 har ingen aktiv
  change-reason nu (FE-konsument saknas).

## Avvisade alternativ

- **BYGG NU (DTO-symmetri):** konsistens med död kod (§5), YAGNI-brott. Ingen
  schemalagd nära leverans (tracker + Pending #3 säger framtida/oschemalagd).
- **SKIPPA permanent:** för starkt — saved-search-list-UI ej cancelerat, bara
  oschemalagt; ren skippa tappar spårbarheten.
- **Numrerad TD:** §9.6 — PR-4 är icke-leverans av framtida feature, ej brist i
  levererad kod. Skulden bor redan i Pending #3; egen TD fragmenterar samma arbete.

## Trigger-not (deferral-registret)

När `ListSavedSearches`-konsumerande FE byggs, i samma touch (CCP): (a)
`getSavedSearches`-API-klient + `saved-searches.ts`-schema-konsumtion, (b)
rendering av **alla fem** dimensioners `*Labels` (occupationGroup/municipality/
region **+ employmentType/worktimeExtent**), (c) backend `EmploymentTypeLabels`/
`WorktimeExtentLabels`-resolution i `ListSavedSearchesQueryHandler` via
kind-agnostisk `ResolveLabelsAsync` (trivial — Klass 2 seedat sedan PR-1).
Ex-BESLUT-4-PR-4 absorberas hit.

## Referenser

- Beck (YAGNI); Fowler 2018 kap. 3 (Speculative Generality); Martin 2017 kap. 13
  (CCP/REP); CLAUDE.md §5/§9.6; ADR 0039-amendment 2026-05-20; ADR 0043
- On-disk: `SavedSearchDto.cs:16-20,35-37`, `saved-searches.ts:50-70`,
  `sokningar/page.tsx`, `lib/api/` (ingen saved-searches-klient)
