# ADR 0055 — Platsbanken-popover ersätter disclosure-filtersektion

**Datum:** 2026-05-19
**Status:** Accepted
**Kontext:** JobbPilot v3 UI-refactor (HANDOVER-v3.md §0.3-veto, §5.4, §9). Filter ska följa Platsbankens popover-mönster — obligatoriskt. ADR 0042 Beslut A specade tidigare en kollaps-disclosure-filtersektion.
**Beslutsfattare:** Klas Olsson (produktägare; explicit Accepted-flip-GO 2026-05-19)
**Supersedes:** [ADR 0042](./0042-search-surface-information-architecture.md) Beslut A (disclosure-filtersektion)
**Relaterad:** [ADR 0042](./0042-search-surface-information-architecture.md) (övriga beslut — typeahead C, multi-värde, relevans-sort — **består**), [ADR 0043](./0043-taxonomy-acl-for-search-surface.md) (taxonomi-ACL — träd-källa/ACL **oförändrad**, endast presentation omprövas), ADR 0052 (v3 designsystem), ADR 0040 (Smart CV-härlett filter); HANDOVER-v3.md §0.3/§5.4/§9; målbild 05

> **Livscykel-/proveniens-not:** Skriven 2026-05-19 av Claude Code (adr-keeper)
> på explicit Klas-begäran — medveten override av CLAUDE.md §9.4
> webb-Claude-verbatim-konventionen (memory `feedback_klas_can_override_adr_verbatim_source`).
> Besluts-substansen är transkriberad från HANDOVER-v3.md (auktoritativ
> designspec med §0-veto) + senior-cto-advisor-dom Fas 0 (Beslut 4). Inga
> nya beslut konstruerade. Status **Accepted** per Klas explicit
> Accepted-flip-GO 2026-05-19.

---

## Kontext

HANDOVER-v3.md §0.3 är ett veto: filter-popovers enligt Platsbankens mönster
är obligatoriskt. ADR 0042 Beslut A specade tidigare en kollaps-disclosure-
filtersektion. ADR 0042:s övriga beslut (typeahead Variant C, multi-värde-
kriterier, relevans-sort) och ADR 0043:s taxonomi-ACL (träd-källa,
Anticorruption Layer, domänkontrakt) är oberoende av presentationsformen och
påverkas inte i sak — endast renderingen i popover-kontext omprövas.

senior-cto-advisor (Fas 0, Beslut 4) bekräftade avgränsningen (supersession
gäller endast Beslut A; ACL/domänkontrakt orört).

## Beslut

### Beslut 1 — Tvåkolumns-popover (Platsbanken-mönster)

> **⚠ Amendad 2026-05-19** — se [Amendment 2026-05-19 — Data-/fas-gating av
> Ort + Filter](#amendment-2026-05-19--data-fas-gating-av-ort--filter)
> nedan. Originaltexten nedan står oförändrad; Ort-popovern renderas
> **enkelkolumns** (Län, ingen kommun) och Filter-pillen är **deferred helt**
> i F4 — tvåkolumns län→kommun är planerat slutmål, ej aktuell rendering.

Ort / Yrke / Filter renderas som en tvåkolumns-popover, bredd 580px:

- **Vänster:** kategorier (län / yrkesområden).
- **Höger:** val (kommuner / yrken).
- Aktiv vänsterrad: fylld leaf-grön + vit text + chevron.
- Höger kolumns första rad: "Välj alla X"-checkbox.
- Per-kolumn header: titel + Rensa (Rensa visas **endast** vid aktivt val).

### Beslut 2 — Ingen footer, live-commit

- **Ingen** footer, **ingen** Använd/Stäng-knapp.
- Markeringar sparas live vid klick.
- ESC och klick utanför stänger popovern.

### Beslut 3 — Ingen spara-sökning-knapp i `/jobb`

Ingen spara-sökning-knapp i `/jobb` (HANDOVER §9 — senaste sökningar fångas
automatiskt; korsref ADR 0040 Smart CV-härlett filter / SavedSearch-fas).

### Beslut 4 — Taxonomi-källa återanvänds

Server-side taxonomi-träd per ADR 0043 återanvänds oförändrat (ACL,
träd-källa, domänkontrakt orört) — endast presentationen är ny.

## Amendment 2026-05-19 — Data-/fas-gating av Ort + Filter

> **Amendment 2026-05-19 (Klas-godkänd, data-/fas-triagead — memory feedback_adr_mechanism_vs_env_phase_triage):** ADR 0055:s tvåkolumns Ort-popover + Filter-popover ärvde v3-prototypens mock-data (data.jsx `komm:[…]` + Filter-grupper). Verifierad domän-verklighet (ADR 0043 Variant A): `TaxonomyTree.regions` är **enkelnivå (Län, ingen kommun)**; `occupationFields→occupations` är tvånivå; backend job-ads-query har endast `q/ssyk/region/sortBy/since` — inga anställningsform/omfattning/arbetsplats/publicerad-params.
> - **Ort-popover renderas ENKELKOLUMNS** (Län-lista, "Välj alla län", per-kolumn Rensa, leaf-grön aktiv, 580px-chrome, live-spar, ESC/klick-utanför, chips) tills kommun-taxonomi byggs. Tvåkolumns län→kommun är planerat slutmål, EJ aktuell rendering; höger kommun-kolumn läggs additivt med samma skal i framtida fas — ingen ny designomgång.
> - **Filter-pillen deferred HELT** tills minst en filter-dimension (anställningsform/omfattning/arbetsplats/publicerad) finns som backend-param. Ingen Filter-pill i hero i F4. Tom popover/mock-grupper = teater, bryter no-mock-doktrinen + civic-ton (samma logik som ADR 0053 Beslut 5 match-deferral).
> - **Yrke-popover (tvåkolumns Yrkesområde→Yrken, "Välj alla yrken"), sort-inline-dropdown i result-toolbar, JobAdFilters-borttagning** byggs exakt per Klas F4-direktiv — ingen konflikt med data.
> - Senaste sökningar / Sparade annonser-hero-chips förblir deferred (no-mock, ADR 0053-amendment-precedens).
> - ADR 0042 Beslut B-domänkontrakt (concept-id `string[]` ssyk/region repeated URL-params) OFÖRÄNDRAT. ADR 0043 taxonomi-ACL OFÖRÄNDRAD — endast presentationen byts (disclosure→popover).
> ADR förblir **Accepted** — amendment, ej supersession; ADR 0055:s supersession av ADR 0042 Beslut A består.

> **Amendment-proveniens:** Klas-godkänd verbatim 2026-05-19 (memory feedback_klas_can_override_adr_verbatim_source). Grundad i F4-discovery-data-verklighet (taxonomy.ts ADR 0043 Variant A + job-ads-query-params) + Klas F4-direktiv. ADR-mekanik-vs-data-/fas-triage (feedback_adr_mechanism_vs_env_phase_triage) → Klas-beslut, ej CC-omdöme.

## Konsekvenser

### Positiva

- Matchar v3-målbild 05 och ger Platsbanken-IA-paritet (igenkänning för
  svenska jobbsökare — civic-utility-värdet i ADR 0016).
- Live-commit utan Använd-knapp minskar interaktionssteg.
- Taxonomi-domänlagret (ADR 0043) återanvänds — ingen backend-ändring.

### Negativa + mitigering

- `JobAdFilters` samt region-/occupation-picker byggs om. Mitigering: ADR
  0043 ACL/träd-källa oförändrad — ombyggnaden är ren presentation, inte
  domänarbete.
- Live-spar utan Använd-knapp ändrar `searchParams`-commit-mönstret (varje
  klick commit:ar). Mitigering: medvetet val per §0.3-vetot; debounce/
  URL-sync-disciplin enligt ADR 0042-typeahead-mönstret återanvänds.

## Alternativ övervägda

### Alternativ A — Platsbanken-tvåkolumns-popover, live-commit (valt)

Se Beslut 1–4.

**Valt:** enda varianten som efterlever §0.3-vetot och ger
Platsbanken-paritet. (Källa: senior-cto-advisor Beslut 4; HANDOVER §5.4.)

### Alternativ B — Behåll disclosure-filtersektion (ADR 0042 Beslut A)

Behåll kollaps-disclosure-sektionen.

**Avvisat:** direkt veto-brott mot HANDOVER §0.3. (Källa:
senior-cto-advisor Beslut 4.)

### Alternativ C — Popover med Använd-knapp

Tvåkolumns-popover men med explicit Använd/commit-knapp.

**Avvisat:** veto-brott mot §0.3 ("markeringar sparas live") — en
Använd-knapp gör commit explicit istället för live. (Källa:
senior-cto-advisor Beslut 4.)

## Implementationsstatus

- **Beslut accepterat 2026-05-19** (Klas Accepted-flip-GO).
- Implementation: JobbPilot v3 UI-refactor (`JobAdFilters` + region-/
  occupation-picker ombyggd som tvåkolumns-popover, live-commit,
  searchParams-sync). `pnpm build`-gate per AGENTS.md.
- ADR 0042 Beslut A markeras superseded i index; ADR 0042 övriga beslut +
  ADR 0043 förblir Accepted och orörda.

---

## Amendment 2026-05-20 — Senaste-hero-chip BE-stöd levererat (per ADR 0060)

Per [ADR 0060](./0060-recent-job-searches-auto-capture.md) Beslut 8 (F6 P4a backend-leverans 2026-05-20) är backend-stödet för "Senaste sökningar"-hero-chip nu levererat: `RecentJobSearches`-domän + `/api/v1/me/recent-searches` GET/DELETE + `RecentJobSearchCaptureBehavior` post-handler auto-capture på `ListJobAdsQuery`. ADR 0055:s tidigare deferral av Senaste-chip (amendment 2026-05-19 — "deferrad tills BE-stöd") **flyttas från deferral-listan**. Frontend-rendering av Senaste-chip kan aktiveras i F6 P4a frontend-batch (separat session efter F6 P4a backend-merge).

**"Sparade"-chip kvar deferred** tills F6 P4b SavedJobAds-domän (per-annons-bookmark) är levererad — separat backend-prompt. Övriga ADR 0055-beslut (tvåkolumns-popover, live-commit, ingen Använd-knapp, ingen Spara-sökning-knapp i /jobb, supersession av ADR 0042 Beslut A) **består oförändrade**.

---

## Amendment 2026-10-23 — The /jobb Yrke popover has a search mode: Beslut 1's columns hold hits and an open word's variants, with no Välj alla and no Rensa; Beslut 2's "no footer" clause is superseded by ADR 0067 E2c (#2198 PR 2, ADR 0180)

**Status:** Accepted 2026-10-23. **Source:** [ADR 0180](./0180-the-occupation-pickers-search-every-occupation-name-by-word-start-and-compound-head-on-the-server-over-the-derivers-shared-index-and-answer-in-rows-that-select-a-group.md) D1, D5 and D8, as amended 2026-10-23; [ADR 0067](./0067-platsbanken-search-parity.md) implementation note E2c (2026-06-11). Additive: Beslut 1 to 4 and the two earlier amendments are untouched in their text. Ort on `/jobb` and on `/foretag/sok` passes no search, and this amendment does not change it.

- **Search mode.** `/jobb`'s Yrke popover can show the answer to a typed word in the place of its browse columns. In that mode Beslut 1's columns hold **"Träffar"** (the hits) on the left and the **variants of the open Word row** on the right. The rest of the mode's layout (a Word row opened by default, no Word rows, the idle right column, stacking) is ADR 0180 D5's and is not restated here. Browsing comes back as it was when the word is cleared, and when the answer is no hits, unavailable or a missing session.
- **No "Välj alla" and no Rensa while results show.** Beslut 1's "Välj alla X" row and the per-column Rensa are not rendered in search mode. A "Välj alla" over a Word row would make the row checkable in practice, and what a search selects is the group of a Group row or of a variant, never the row itself (ADR 0180 D5).
- **"(n)" on Group rows only.** The facet count of the browse rows (ADR 0067 E2c) shows in search mode on Group rows only. It is read from the popover's own group counts, with no new request. A Word row says "{n} yrkesgrupper" in words and never carries "(n)", and a variant never does either, since its count would be read as the variant's own (ADR 0180 D5, ADR 0120).
- **The field, focus and one height.** The field sits above the columns, in `.jp-popover__search`, at the position the boolean row takes; a popover is given the one or the other. It takes focus when the popover opens, once the popover is positioned (WCAG 2.4.3); a popover without a search moves no focus on opening. The field's slot and the box under it share one reserved height in both views, so the footer stays where it is between browsing and results (`.jp-popover__frame` in `app.css`).
- **The term is never in the URL.** The typed word lives in the Yrke popover's own state, starts empty on every opening and reaches nothing but the search's own `q` (ADR 0180 D1). Beslut 2's live commit is unchanged: a pick commits at the click, as group ids through the same path a browse pick takes, and the popover stays open.
- **Escape.** Beslut 2's "ESC … stänger popovern" holds, with one addition in search mode: an Escape in a field that holds text clears the field instead of closing. That behaviour and its mechanism have one home, ADR 0180 D5 as amended 2026-10-23.
- **Beslut 2's "Ingen footer, ingen Använd/Stäng-knapp" is superseded by ADR 0067 E2c:** the popover has a footer, "Visa N annonser", which closes it. Live commit and the click-outside close stand.
